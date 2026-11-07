using Godot;

namespace Gamejam.Gameplay;

// Holds the selected hotbar item in front of the local player's camera and animates it: it rises into view when
// equipped, drops out of view when put away, and `inspect` (Y) twirls it like the CS karambit inspect. It also
// sways a little: it trails behind when you turn and bobs as you walk.
// An item with a child Node3D named "Grip" is placed so that the grip sits at the hand, at the grip's angle (move the
// grip to move the item in view); others keep the block pose.
public partial class HeldItemView : Node
{
	// Where the hand is, in camera space (right, up, forward is -Z).
	[Export] public Vector3 HoldOffset { get; set; } = new(0.25f, -0.2f, -0.6f);
	[Export] public float EquipTime { get; set; } = 0.3f;
	[Export] public float UnequipTime { get; set; } = 0.2f;
	// Where a put-away item sits relative to the hand, and how far it tips forward on the way down.
	[Export] public Vector3 LoweredOffset { get; set; } = new(0.05f, -0.5f, 0.15f);
	[Export] public float LoweredTiltDegrees { get; set; } = 60f;
	[Export] public float InspectTime { get; set; } = 3.0f;
	// Inspect pose relative to the hand: pulled toward the middle of the screen and a little closer.
	[Export] public Vector3 InspectOffset { get; set; } = new(-0.18f, 0.06f, 0.15f);
	[Export] public float InspectTiltDegrees { get; set; } = 25f;
	// The twirl: how many full spins, and the axis in camera space (mostly toward you, so it spins flat across the
	// screen around the hand).
	[Export] public int InspectSpins { get; set; } = 2;
	[Export] public Vector3 InspectSpinAxis { get; set; } = new(0.3f, 0f, 1f);
	// After the twirl the item is tipped toward you by this much and turned to show its side.
	[Export] public float InspectShowTiltDegrees { get; set; } = 20f;
	[Export] public float InspectShowTurnDegrees { get; set; } = 90f;
	// How much of a turn the item trails behind, how fast it catches up, and the most it can lag.
	[Export] public float LookSwayAmount { get; set; } = 0.8f;
	[Export] public float LookSwaySpeed { get; set; } = 8f;
	[Export] public float MaxLookSwayDegrees { get; set; } = 9f;
	[Export] public float WalkBob { get; set; } = 0.022f;
	[Export] public float StrideLength { get; set; } = 1.6f;
	// Speed at which the walk bob reaches full strength (Player.MoveSpeed).
	[Export] public float ReferenceSpeed { get; set; } = 4.5f;

	private const string GripName = "Grip";
	// Inspect phases, as fractions of InspectTime: raise and wind up, twirl, catch, show, put back.
	private const float RaiseEnd = 0.1f;
	private const float TwirlEnd = 0.38f;
	private const float CatchEnd = 0.48f;
	private const float ShowEnd = 0.84f;
	private const float WindUpRadians = 0.5f;
	// Turns an item's +X face (the glyph side of the wall blocks) toward the camera.
	private static readonly Basis BlockBasis = new(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0));

	private CharacterBody3D _body;
	private Camera3D _camera;
	private Inventory _inventory;
	private Pickup _shown;
	private Node3D _grip;
	// 0 = out of view, 1 = in the hand.
	private float _raise;
	// Seconds into the inspect; negative when not inspecting.
	private float _inspect = -1f;
	// Where the camera pointed a moment ago; the item leans toward it.
	private Quaternion _lagView;
	private bool _lagReady;
	private float _walkPhase;
	private float _walkAmount;

	public override void _Ready()
	{
		// After the camera has moved this frame (mouse look, camera sway), so the item doesn't trail behind it.
		ProcessPriority = 100;
	}

	public void Setup(CharacterBody3D body, Camera3D camera, Inventory inventory)
	{
		_body = body;
		_camera = camera;
		_inventory = inventory;
		_inventory.Changed += OnInventoryChanged;
	}

	public override void _ExitTree()
	{
		if (_inventory != null)
			_inventory.Changed -= OnInventoryChanged;
	}

	public void Inspect()
	{
		if (_shown != null && _raise >= 1f && _inspect < 0f)
			_inspect = 0f;
	}

	public override void _Notification(int what)
	{
		// A paused player (during the intro) brings their item up again when they get control back.
		if (what == NotificationEnabled)
		{
			_raise = 0f;
			_inspect = -1f;
			_lagReady = false;
		}
	}

	public override void _Process(double delta)
	{
		if (_inventory == null)
			return;
		float dt = (float)delta;
		Pickup selected = _inventory.SelectedItem;

		if (_shown != selected)
		{
			// Put the old item away first, then bring up the new one.
			_inspect = -1f;
			_raise = _shown == null ? 0f : Mathf.Max(0f, _raise - dt / UnequipTime);
			if (_raise <= 0f)
				Show(selected);
		}
		else if (_shown != null)
		{
			_raise = Mathf.Min(1f, _raise + dt / EquipTime);
			if (_inspect >= 0f)
			{
				_inspect += dt;
				if (_inspect >= InspectTime)
					_inspect = -1f;
			}
		}

		Transform3D sway = Sway(dt);
		if (_shown != null)
			_shown.GlobalTransform = _camera.GlobalTransform * sway * AnimatedHand() * ItemInHand() * InspectTurn();
	}

	private void Show(Pickup item)
	{
		if (_shown != null && IsInstanceValid(_shown))
			_shown.Visible = false;
		_shown = item;
		_grip = item?.GetNodeOrNull<Node3D>(GripName);
		if (item != null)
			item.Visible = true;
	}

	private void OnInventoryChanged()
	{
		// An item that left the hotbar (dropped or placed) is no longer ours to move or hide.
		if (_shown != null && !InInventory(_shown))
		{
			_shown = null;
			_grip = null;
			_inspect = -1f;
		}
		// Everything else stays stowed out of sight until it's selected.
		for (int i = 0; i < Inventory.SlotCount; i++)
		{
			Pickup item = _inventory.GetItem(i);
			if (item != null && item != _shown)
				item.Visible = false;
		}
	}

	private bool InInventory(Pickup item)
	{
		for (int i = 0; i < Inventory.SlotCount; i++)
		{
			if (_inventory.GetItem(i) == item)
				return true;
		}
		return false;
	}

	// Trails behind mouse look and bobs with each step, pivoting on the hand. Runs even with empty hands so the
	// next item doesn't start with a jolt.
	private Transform3D Sway(float dt)
	{
		Quaternion view = _camera.GlobalBasis.GetRotationQuaternion();
		if (!_lagReady)
		{
			_lagView = view;
			_lagReady = true;
		}
		_lagView = _lagView.Slerp(view, 1f - Mathf.Exp(-LookSwaySpeed * dt));
		// The lagging view as seen from the camera, scaled down and capped.
		Quaternion trail = Quaternion.Identity.Slerp(view.Inverse() * _lagView, LookSwayAmount);
		float angle = trail.GetAngle();
		float maxAngle = Mathf.DegToRad(MaxLookSwayDegrees);
		if (angle > maxAngle)
			trail = Quaternion.Identity.Slerp(trail, maxAngle / angle);

		Vector3 bob = Vector3.Zero;
		if (_body != null)
		{
			Vector3 velocity = _body.Velocity;
			float speed = new Vector2(velocity.X, velocity.Z).Length();
			float target = _body.IsOnFloor() ? Mathf.Clamp(speed / ReferenceSpeed, 0f, 1.3f) : 0f;
			_walkAmount = Mathf.Lerp(_walkAmount, target, 1f - Mathf.Exp(-8f * dt));
			_walkPhase = Mathf.Wrap(_walkPhase + speed * dt * Mathf.Pi / StrideLength, 0f, Mathf.Tau);
			float sin = Mathf.Sin(_walkPhase);
			bob = new Vector3(sin * WalkBob, -Mathf.Abs(sin) * WalkBob * 0.8f, 0f) * _walkAmount;
		}

		return new Transform3D(new Basis(trail), HoldOffset + bob) * new Transform3D(Basis.Identity, -HoldOffset);
	}

	// The hand's animated offset from its resting pose: a transform in camera space that pivots on the hand.
	private Transform3D AnimatedHand()
	{
		float lowered = 1f - EaseOut(_raise);
		Vector3 offset = LoweredOffset * lowered;
		var rotation = new Basis(Vector3.Right, -Mathf.DegToRad(LoweredTiltDegrees) * lowered);

		if (_inspect >= 0f)
		{
			float p = _inspect / InspectTime;
			float weight = Mathf.SmoothStep(0f, RaiseEnd, p) * (1f - Mathf.SmoothStep(ShowEnd, 1f, p));
			offset += InspectOffset * weight;
			rotation *= new Basis(InspectSpinAxis.Normalized(), TwirlAngle(p))
				* new Basis(Vector3.Back, Mathf.DegToRad(InspectTiltDegrees) * weight)
				* new Basis(Vector3.Right, Mathf.DegToRad(InspectShowTiltDegrees) * ShowAmount(p));
		}

		return new Transform3D(rotation, HoldOffset + offset) * new Transform3D(Basis.Identity, -HoldOffset);
	}

	private Transform3D ItemInHand()
	{
		if (_grip != null)
			return new Transform3D(Basis.Identity, HoldOffset) * _grip.Transform.AffineInverse();
		return new Transform3D(BlockBasis, HoldOffset);
	}

	// Karambit-style: a short wind-up the other way, the spins (fast in the middle), then a small overshoot as it's
	// caught. Whole turns, so it ends where it started.
	private float TwirlAngle(float p)
	{
		float spins = Mathf.Tau * InspectSpins;
		if (p < RaiseEnd)
			return -WindUpRadians * Mathf.SmoothStep(0f, 1f, p / RaiseEnd);
		if (p < TwirlEnd)
			return Mathf.Lerp(-WindUpRadians, spins, EaseInOut((p - RaiseEnd) / (TwirlEnd - RaiseEnd)));
		if (p < CatchEnd)
		{
			float k = (p - TwirlEnd) / (CatchEnd - TwirlEnd);
			return spins + Mathf.Sin(k * Mathf.Pi) * 0.25f * (1f - k);
		}
		return 0f;
	}

	// 0 → 1 → 0 over the show phase.
	private static float ShowAmount(float p) =>
		Mathf.Sin(Mathf.Clamp((p - CatchEnd) / (ShowEnd - CatchEnd), 0f, 1f) * Mathf.Pi);

	// While showing it off, the item turns around its own up axis to show its side.
	private Transform3D InspectTurn()
	{
		if (_inspect < 0f)
			return Transform3D.Identity;
		float turn = Mathf.DegToRad(InspectShowTurnDegrees) * ShowAmount(_inspect / InspectTime);
		return new Transform3D(new Basis(Vector3.Up, turn), Vector3.Zero);
	}

	private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

	private static float EaseOut(float t)
	{
		float inverse = 1f - t;
		return 1f - inverse * inverse * inverse;
	}
}
