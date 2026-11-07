using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// Keeps Target hidden until Puzzle is solved, then raises it into the spot it was placed at in the editor.
// Target is usually a Pickup (e.g. gameplay/Key.tscn), which can be picked up once it has finished rising.
public partial class RiseOnSolved : Node
{
	[Export] public SlotSequence Puzzle { get; set; }
	[Export] public Node3D Target { get; set; }
	// Where Target starts relative to its placed position, e.g. below the floor so it comes up through it.
	[Export] public Vector3 StartOffset { get; set; } = new Vector3(0f, -0.4f, 0f);
	[Export] public float Duration { get; set; } = 1.5f;
	// Played at Target through Events.PlaySfx. Empty = no sound.
	[Export] public string SfxName { get; set; } = "";
	// The solver's view rumbles, cuts to Target as it rises and glows, then returns (gameplay/RevealCinematic.cs).
	[Export] public bool PlayCinematic { get; set; } = true;
	[Export] public Color GlowColor { get; set; } = new Color(1f, 0.78f, 0.3f);

	private Vector3 _endPosition;
	private uint _layer;
	private bool _revealed;

	public override void _Ready()
	{
		if (Puzzle == null || Target == null)
		{
			GD.PushWarning($"{Name}: set both Puzzle and Target.");
			return;
		}
		Target.Visible = false;
		// Collision is changed after every _Ready, so Pickup has already cached its own layer.
		CallDeferred(MethodName.Setup);
	}

	public override void _ExitTree()
	{
		if (Puzzle != null && IsInstanceValid(Puzzle))
			Puzzle.Solved -= OnSolved;
	}

	private void Setup()
	{
		_endPosition = Target.Position;
		if (Target is Pickup pickup)
		{
			if (pickup.CreateSlotAtStart)
				GD.PushWarning($"{Name}: turn off CreateSlotAtStart on {pickup.Name}, or its hidden slot can be aimed at.");
			// Keeps a RigidBody from falling while hidden and while rising.
			pickup.Freeze = true;
		}
		// Not hittable by the interact ray while hidden.
		if (Target is CollisionObject3D body)
		{
			_layer = body.CollisionLayer;
			body.CollisionLayer = 0;
		}

		Puzzle.Solved += OnSolved;
		if (Puzzle.IsSolved)
			OnSolved();
	}

	private void OnSolved()
	{
		if (_revealed)
			return;
		_revealed = true;

		Target.Position = _endPosition + StartOffset;
		Target.Visible = true;
		if (SfxName != "" && Events.Instance != null)
			Events.Instance.EmitSignal(Events.SignalName.PlaySfx, SfxName, Target.GlobalPosition);

		// The cutscene starts the rise itself, once the view is on Target.
		Vector3 end = Target.GetParent() is Node3D parent ? parent.GlobalTransform * _endPosition : _endPosition;
		if (PlayCinematic && RevealCinematic.Play(this, Target, end + Vector3.Up * 0.03f, Duration, GlowColor, StartRise))
			return;
		StartRise();
	}

	private void StartRise()
	{
		Tween tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(Target, "position", _endPosition, Duration);
		tween.TweenCallback(Callable.From(OnRiseFinished));
	}

	private void OnRiseFinished()
	{
		if (Target is CollisionObject3D body)
			body.CollisionLayer = _layer;
	}
}
