using System.Collections.Generic;
using Gamejam.Core;
using Gamejam.Gameplay;
using Godot;

namespace Gamejam.Audio;

// The soundtrack, on each machine: the menu theme loops in the lobby, the background music loops once the game has
// started, a short sting plays for both players whenever either solves a puzzle (the music dips under it), and the
// ending (gameplay/GameEnding.cs) fades the music out for the win or lose theme, which fades out as the screen ends.
public partial class Music : Node
{
	[Export] public AudioStream MenuTrack { get; set; }
	[Export] public AudioStream GameTrack { get; set; }
	[Export] public AudioStream PuzzleSting { get; set; }
	[Export] public AudioStream WinTrack { get; set; }
	[Export] public AudioStream LoseTrack { get; set; }
	[Export] public float MenuVolumeDb { get; set; } = -6f;
	[Export] public float GameVolumeDb { get; set; } = -12f;
	[Export] public float StingVolumeDb { get; set; } = -4f;
	[Export] public float EndingVolumeDb { get; set; } = -4f;
	// How far the background music dips while the puzzle sting plays.
	[Export] public float DuckDb { get; set; } = -14f;
	[Export] public float CrossfadeTime { get; set; } = 1.5f;
	// The ending theme fades out over the end of the ending screen.
	[Export] public float EndingFadeTime { get; set; } = 3f;

	private const float Silent = -60f;

	private AudioStreamPlayer _menu;
	private AudioStreamPlayer _game;
	private AudioStreamPlayer _sting;
	private AudioStreamPlayer _ending;
	private Tween _gameTween;
	private GameEnding _gameEnding;
	private readonly List<Puzzle> _puzzles = new();

	public override void _Ready()
	{
		MenuTrack ??= GD.Load<AudioStream>("res://audio/Main_Menu_Soundtrack.wav");
		GameTrack ??= GD.Load<AudioStream>("res://audio/Background_Music.wav");
		PuzzleSting ??= GD.Load<AudioStream>("res://audio/Victory_Soundtrack_Puzzles.wav");
		WinTrack ??= GD.Load<AudioStream>("res://audio/Victory_Soundtrack_Finale.wav");
		LoseTrack ??= GD.Load<AudioStream>("res://audio/You_Have_Lost.wav");

		_menu = AddPlayer("Menu", MenuTrack, loop: true);
		_game = AddPlayer("Game", GameTrack, loop: true);
		_sting = AddPlayer("Sting", PuzzleSting, loop: false);
		_ending = AddPlayer("Ending", null, loop: false);

		_menu.VolumeDb = MenuVolumeDb;
		_menu.Play();

		if (GameState.Instance != null)
			GameState.Instance.GameStarted += OnGameStarted;
		Callable.From(FindGameEnding).CallDeferred();
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.GameStarted -= OnGameStarted;
		if (_gameEnding != null && IsInstanceValid(_gameEnding))
			_gameEnding.EndingStarted -= OnEndingStarted;
		foreach (Puzzle puzzle in _puzzles)
		{
			if (IsInstanceValid(puzzle))
				puzzle.Solved -= OnPuzzleSolved;
		}
	}

	private AudioStreamPlayer AddPlayer(string name, AudioStream stream, bool loop)
	{
		var player = new AudioStreamPlayer { Name = name, Stream = stream };
		AddChild(player);
		// The tracks aren't imported as loops, so they're restarted when they end.
		if (loop)
			player.Finished += () => player.Play();
		return player;
	}

	private void FindGameEnding()
	{
		foreach (Node node in GetTree().GetNodesInGroup("game_ending"))
			_gameEnding ??= node as GameEnding;
		if (_gameEnding != null)
			_gameEnding.EndingStarted += OnEndingStarted;
	}

	private void OnGameStarted()
	{
		Tween tween = CreateTween().SetParallel();
		tween.TweenProperty(_menu, "volume_db", Silent, CrossfadeTime);
		tween.Chain().TweenCallback(Callable.From(_menu.Stop));
		_game.VolumeDb = Silent;
		_game.Play();
		FadeGame(GameVolumeDb, CrossfadeTime);
		ListenToPuzzles();
	}

	// Every puzzle in the level (the ceiling collapse is a cutscene, not one the players solve).
	private void ListenToPuzzles()
	{
		if (_puzzles.Count > 0)
			return;
		Node level = GetTree().CurrentScene;
		if (level == null)
			return;
		foreach (Node node in level.FindChildren("*", "Node", true, false))
		{
			if (node is Puzzle puzzle && node is not CeilingCollapse)
			{
				_puzzles.Add(puzzle);
				puzzle.Solved += OnPuzzleSolved;
			}
		}
	}

	// Puzzles are solved on the solver's machine only, so the sting is sent to both.
	private void OnPuzzleSolved()
	{
		if ((_gameEnding != null && _gameEnding.HasEnded) || GameState.Instance?.SkippingStages == true)
			return;
		Rpc(MethodName.PlaySting);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlaySting()
	{
		if ((_gameEnding != null && _gameEnding.HasEnded) || PuzzleSting == null)
			return;
		_sting.VolumeDb = StingVolumeDb;
		_sting.Play();
		if (!_game.Playing)
			return;
		_gameTween?.Kill();
		_gameTween = CreateTween();
		_gameTween.TweenProperty(_game, "volume_db", GameVolumeDb + DuckDb, 0.3f);
		_gameTween.TweenInterval(Mathf.Max(0.5f, (float)PuzzleSting.GetLength() - 0.8f));
		_gameTween.TweenProperty(_game, "volume_db", GameVolumeDb, 1.5f);
	}

	private void OnEndingStarted(bool won, float duration)
	{
		_menu.Stop();
		FadeGame(Silent, 0.6f);
		_gameTween.TweenCallback(Callable.From(_game.Stop));
		_sting.Stop();

		_ending.Stream = won ? WinTrack : LoseTrack;
		if (_ending.Stream == null)
			return;
		_ending.VolumeDb = EndingVolumeDb;
		_ending.Play();
		Tween tween = CreateTween();
		tween.TweenInterval(Mathf.Max(0f, duration - EndingFadeTime));
		tween.TweenProperty(_ending, "volume_db", Silent, EndingFadeTime).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(_ending.Stop));
	}

	private void FadeGame(float volumeDb, float time)
	{
		_gameTween?.Kill();
		_gameTween = CreateTween();
		_gameTween.TweenProperty(_game, "volume_db", volumeDb, time);
	}
}
