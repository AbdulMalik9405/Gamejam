using System.Collections.Generic;
using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// Tells the past player when the present player solves a puzzle (the present player sees the reward's cutscene; the
// past player doesn't): a short time warp ripples over their screen and a line appears in the middle of it, one line
// after another as the present player gets further. A cutscene hides the HUD, so the warp waits until it's back.
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
	[Export] public float Duration { get; set; } = 5f;
	[Export] public float WarpTime { get; set; } = 1.4f;
	// Extra wait before checking for a cutscene, so one that starts just after the solve (the passage opening) is seen.
	[Export] public float CutsceneGrace { get; set; } = 1.5f;
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
		// Not over a cutscene (on either machine: any cutscene pauses the shared timer), nor one that's about to start
		// (the passage opening comes a moment after the keys turn).
		bool Busy() => (hud != null && IsInstanceValid(hud) && !hud.Visible) || GameState.Instance?.IsTimerPaused == true;
		await ToSignal(GetTree().CreateTimer(CutsceneGrace), SceneTreeTimer.SignalName.Timeout);
		while (Busy() && waited < MaxWait)
		{
			await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
			waited += 0.25f;
		}
		if (Busy())
			return;
		Warp(line);
	}

	// The time rupture's ripple (net/time_rupture.gdshader), swelling and fading, with the line coming up in the middle
	// of the screen as it peaks and staying a while after.
	private void Warp(string line)
	{
		var layer = new CanvasLayer { Name = "PartnerWarp", Layer = 15 };
		AddChild(layer);
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://net/time_rupture.gdshader") };
		var ripple = new ColorRect { Material = material, MouseFilter = Control.MouseFilterEnum.Ignore };
		ripple.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(ripple);

		var label = new Label
		{
			Text = line,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = Colors.Transparent,
		};
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } });
		label.AddThemeFontSizeOverride("font_size", 40);
		label.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.7f));
		label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.05f, 0.02f));
		label.AddThemeConstantOverride("outline_size", 8);
		layer.AddChild(label);

		Tween tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(k => material.SetShaderParameter("strength", Mathf.Sin(k * Mathf.Pi) * 0.7f)), 0f, 1f, WarpTime);
		tween.Parallel().TweenProperty(label, "modulate:a", 1f, WarpTime * 0.6f).SetDelay(WarpTime * 0.3f);
		tween.TweenInterval(Duration);
		tween.TweenProperty(label, "modulate:a", 0f, 0.8f);
		tween.TweenCallback(Callable.From(layer.QueueFree));
	}
}
