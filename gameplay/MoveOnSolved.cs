using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// Moves Target by Offset once Puzzle is solved, e.g. a wall sinking into the floor to open a passage.
// The solver's view cuts to it while it moves (gameplay/RevealCinematic.cs, without the gold glow).
public partial class MoveOnSolved : Node
{
	[Export] public Puzzle Puzzle { get; set; }
	[Export] public Node3D Target { get; set; }
	// In Target's parent space.
	[Export] public Vector3 Offset { get; set; } = new Vector3(0f, -2.8f, 0f);
	[Export] public float Duration { get; set; } = 3.5f;
	// Wait after solving before it starts, e.g. to see keys turn first.
	[Export] public float Delay { get; set; } = 0.8f;
	[Export] public bool PlayCinematic { get; set; } = true;
	// What the cutscene camera looks at; it stands a little in front of it, on the solver's side.
	// Unset = Target's origin.
	[Export] public Node3D ShotFocus { get; set; }
	// Played at Target through Events.PlaySfx. Empty = no sound.
	[Export] public string SfxName { get; set; } = "";
	// Shown once the move is over and the view is back with the player, e.g. what to do next. Empty = nothing.
	[Export] public string DoneMessage { get; set; } = "";
	[Export] public float DoneMessageTime { get; set; } = 6f;

	private Vector3 _start;
	private bool _started;

	public override void _Ready()
	{
		if (Puzzle == null || Target == null)
		{
			GD.PushWarning($"{Name}: set both Puzzle and Target.");
			return;
		}
		_start = Target.Position;
		Puzzle.Solved += OnSolved;
		if (Puzzle.IsSolved)
			CallDeferred(MethodName.OnSolved);
	}

	public override void _ExitTree()
	{
		if (Puzzle != null && IsInstanceValid(Puzzle))
			Puzzle.Solved -= OnSolved;
	}

	private async void OnSolved()
	{
		if (_started)
			return;
		_started = true;

		if (Delay > 0f)
			await ToSignal(GetTree().CreateTimer(Delay), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(Target))
			return;

		if (SfxName != "" && Events.Instance != null)
			Events.Instance.EmitSignal(Events.SignalName.PlaySfx, SfxName, Target.GlobalPosition);
		Vector3 focus = ShotFocus != null ? ShotFocus.GlobalPosition : Target.GlobalPosition;
		// Black adds nothing, so the cutscene shows the move without a glow.
		if (PlayCinematic && RevealCinematic.Play(this, Target, focus, Duration, Colors.Black, StartMove))
		{
			// The HUD is hidden during the cutscene, so the message waits until it's over.
			Node cinematic = GetNodeOrNull("RevealCinematic");
			if (cinematic != null)
				await ToSignal(cinematic, Node.SignalName.TreeExited);
			ShowDoneMessage();
			return;
		}
		StartMove();
		await ToSignal(GetTree().CreateTimer(Duration), SceneTreeTimer.SignalName.Timeout);
		ShowDoneMessage();
	}

	private void ShowDoneMessage()
	{
		if (DoneMessage != "" && Events.Instance != null && IsInsideTree())
			Events.Instance.EmitSignal(Events.SignalName.ShowMessage, DoneMessage, DoneMessageTime);
	}

	private void StartMove()
	{
		CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut)
			.TweenProperty(Target, "position", _start + Offset, Duration);
	}
}
