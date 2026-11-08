using System.Collections.Generic;
using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// Tells the past player when the present player solves a puzzle: a line on their screen (Events.ShowMessage, so the
// same HUD message and font the present player gets), one after another as the present player gets further. A cutscene
// hides the HUD, so the line waits until it's back.
public partial class PartnerNews : Node
{
	// In order: the first puzzle the present player solves shows the first line, and so on; the last one repeats.
	[Export] public string[] Lines { get; set; } =
	{
		"You feel success coming closer.",
		"Somewhere far ahead in time, stone grinds against stone.",
		"The end is finally within sight.",
	};
	// Wait after the solve before showing it (the puzzle sting plays first).
	[Export] public float Delay { get; set; } = 1.5f;
	[Export] public float Duration { get; set; } = 6f;
	// How long a line waits for a cutscene to end before it's dropped.
	[Export] public float MaxWait { get; set; } = 40f;

	private readonly List<Puzzle> _puzzles = new();
	private int _shown;

	public override void _Ready()
	{
		if (GameState.Instance != null)
			GameState.Instance.GameStarted += OnGameStarted;
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.GameStarted -= OnGameStarted;
		foreach (Puzzle puzzle in _puzzles)
		{
			if (IsInstanceValid(puzzle))
				puzzle.Solved -= OnPuzzleSolved;
		}
	}

	// Every puzzle in the level (the ceiling collapse is a cutscene, not one the players solve).
	private void OnGameStarted()
	{
		if (_puzzles.Count > 0 || GetTree().CurrentScene == null)
			return;
		foreach (Node node in GetTree().CurrentScene.FindChildren("*", "Node", true, false))
		{
			if (node is Puzzle puzzle && node is not CeilingCollapse)
			{
				_puzzles.Add(puzzle);
				puzzle.Solved += OnPuzzleSolved;
			}
		}
	}

	// Puzzles are solved on the solver's machine only, so this runs on the present player's machine when they solve one.
	private void OnPuzzleSolved()
	{
		if (GameState.Instance == null || GameState.Instance.LocalEra != Era.Present || Lines.Length == 0)
			return;
		string line = Lines[Mathf.Min(_shown, Lines.Length - 1)];
		_shown++;
		Rpc(MethodName.Show, line);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private async void Show(string line)
	{
		if (GameState.Instance == null || GameState.Instance.LocalEra != Era.Past)
			return;
		await ToSignal(GetTree().CreateTimer(Delay), SceneTreeTimer.SignalName.Timeout);
		var hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		float waited = 0f;
		while (hud != null && IsInstanceValid(hud) && !hud.Visible && waited < MaxWait)
		{
			await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
			waited += 0.25f;
		}
		if (hud != null && (!IsInstanceValid(hud) || !hud.Visible))
			return;
		Events.Instance?.EmitSignal(Events.SignalName.ShowMessage, line, Duration);
	}
}
