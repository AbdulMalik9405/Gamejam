using Godot;

namespace Gamejam.Gameplay;

// Shows the explorer's arm (art/explorer_arm.glb) holding the selected hotbar item in front of the local player's
// camera, and animates both: they rise into view when an item is equipped, drop out of view when it's put away, and
// `inspect` (Y) twirls the item like the CS karambit inspect (a torch is tossed up and caught instead). They also sway: trail behind when you turn, bob as you
// walk. An item with a child Node3D named "Grip" is held so that its grip sits in the arm's GripPoint, at the grip's
// angle (move or turn the grip to change how the item sits in the hand); others are held in front of the palm.
public partial class HeldItemView : Node
{
	// Its origin is the first-person camera; it holds RightArm_FP, whose child GripPoint is where items go.
	[Export] public string ArmScene { get; set; } = "res://art/explorer_arm.glb";
	// Where the hand is, in camera space (right, up, forward is -Z), when the arm model is missing.
	[Export] public Vector3 HoldOffset { get; set; } = new(0.25f, -0.2f, -0.6f);
	// Nudges the whole arm from where the model puts it, in camera space.
	[Export] public Vector3 ArmOffset { get; set; } = new(0.02f, -0.09f, -0.07f);
	// Where an item without a Grip sits, relative to the arm's GripPoint (in front of the palm).
	[Export] public Vector3 PalmOffset { get; set; } = new(0f, 0.02f, -0.18f);
	[Export] public float EquipTime { get; set; } = 0.3f;
	[Export] public float UnequipTime { get; set; } = 0.2f;
	// Where a put-away arm sits relative to its resting pose, and how far it swings down at the shoulder.
	[Export] public Vector3 LoweredOffset { get; set; } = new(0.05f, -0.45f, 0.1f);
	[Export] public float LoweredTiltDegrees { get; set; } = 25f;
	[Export] public float InspectTime { get; set; } = 3.0f;
	// Inspect pose relative to the hand: pulled toward the middle of the screen and a little closer.
	[Export] public Vector3 InspectOffset { get; set; } = new(-0.14f, 0.05f, 0.08f);
	[Export] public float InspectTiltDegrees { get; set; } = 20f;
	// The twirl: how many full spins of the item in the fist, and the axis in camera space (mostly toward you, so it
	// spins flat across the screen).
	[Export] public int InspectSpins { get; set; } = 2;
	[Export] public Vector3 InspectSpinAxis { get; set; } = new(0.3f, 0f, 1f);
	// After the twirl the hand tips toward you by this much and the item turns to show its side.
	[Export] public float InspectShowTiltDegrees { get; set; } = 20f;
	[Export] public float InspectShowTurnDegrees { get; set; } = 90f;
	// A torch's inspect: thrown up out of the hand, spun end over end, and caught.
	[Export] public float TossTime { get; set; } = 1.6f;
	[Export] public float TossHeight { get; set; } = 0.28f;
	[Export] public int TossSpins { get; set; } = 1;
	[Export] public Vector3 TossSpinAxis { get; set; } = new(0f, 0f, 1f);
	// How much of a turn the arm trails behind, how fast it catches up, and the most it can lag.
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
	// Toss phases, as fractions of TossTime: dip, flick up and let go, in the air, caught.
	private const float TossDipEnd = 0.15f;
	private const float TossRelease = 0.24f;
	private const float TossCatch = 0.72f;
	private const float TossDip = 0.04f;
	private const float TossFlick = 0.03f;
	private const float CatchDip = 0.05f;
	// Turns an item's +X face (the glyph side of the wall blocks) toward the camera.
	private static readonly Basis BlockBasis = new(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0));

	private CharacterBody3D _body;
	private Camera3D _camera;
	private Inventory _inventory;
	private Pickup _shown;
	private Node3D _grip;
	private bool _toss;
	private Node3D _arm;
	// The arm's and the hand's resting poses in camera space, read from the model.
	private Transform3D _armRest;
	private Transform3D _gripRest;
	// 0 = out of view, 1 = in the hand.
	private float _raise;
	// Seconds into the inspect; negative when not inspecting.
	private float _inspect = -1f;
	// Where the camera pointed a moment ago; the arm leans toward it.
	private Quaternion _lagView;
	private bool _lagReady;
	private float _walkPhase;
	private float _walkAmount;

	public override void _Ready()
	{
		// After the camera has moved this frame (mouse look, camera sway), so the arm doesn't trail behind it.
		ProcessPriority = 100;
		BuildArm();
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
				if (_inspect >= (_toss ? TossTime : InspectTime))
					_inspect = -1f;
			}
		}

		// Everything below is in camera space and pivots on the hand.
		Transform3D hand = Sway(dt) * AnimatedHand();
		Transform3D view = _camera.GlobalTransform;
		if (_arm != null)
		{
			_arm.Visible = _shown != null;
			if (_shown != null)
				_arm.GlobalTransform = view * hand * _armRest;
		}
		if (_shown != null)
			_shown.GlobalTransform = _toss
				? view * hand * Toss() * ItemInHand()
				: view * hand * Twirl() * ItemInHand() * InspectTurn();
	}

	private void BuildArm()
	{
		_gripRest = new Transform3D(Basis.Identity, HoldOffset);
		if (!ResourceLoader.Exists(ArmScene))
		{
			GD.PushWarning($"HeldItemView: {ArmScene} not found, holding items without an arm.");
			return;
		}

		Node root = GD.Load<PackedScene>(ArmScene).Instantiate();
		var arm = root.FindChild("RightArm_FP", true, false) as Node3D;
		var grip = arm?.FindChild("GripPoint", true, false) as Node3D;
		if (arm == null || grip == null)
		{
			GD.PushWarning($"HeldItemView: {ArmScene} has no RightArm_FP with a GripPoint, holding items without an arm.");
			root.Free();
			return;
		}

		_armRest = new Transform3D(Basis.Identity, ArmOffset) * arm.Transform;
		Transform3D gripInArm = Transform3D.Identity;
		for (Node node = grip; node != arm; node = node.GetParent())
			gripInArm = ((Node3D)node).Transform * gripInArm;
		_gripRest = _armRest * gripInArm;

		arm.GetParent().RemoveChild(arm);
		root.Free();
		// The preview handle only shows the artist how items sit in the hand.
		foreach (Node preview in arm.FindChildren("Preview*", "", true, false))
			preview.Free();
		// Right in front of the camera, so a shadow would cover half the screen.
		foreach (Node mesh in arm.FindChildren("*", "MeshInstance3D", true, false))
			((MeshInstance3D)mesh).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

		arm.Name = "Arm";
		arm.Visible = false;
		AddChild(arm);
		_arm = arm;
	}

	private void Show(Pickup item)
	{
		if (_shown != null && IsInstanceValid(_shown))
			_shown.Visible = false;
		_shown = item;
		_grip = item?.GetNodeOrNull<Node3D>(GripName);
		_toss = item is Torch;
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

	// A rotation and offset in camera space, applied around the hand.
	private Transform3D AroundHand(Basis rotation, Vector3 offset)
	{
		Vector3 pivot = _gripRest.Origin;
		return new Transform3D(rotation, pivot + offset) * new Transform3D(Basis.Identity, -pivot);
	}

	// Trails behind mouse look and bobs with each step. Runs even with empty hands so the next item doesn't start
	// with a jolt.
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

		return AroundHand(new Basis(trail), bob);
	}

	// Equip / put-away and the inspect's hand movement. Moves the arm and the item together.
	private Transform3D AnimatedHand()
	{
		float lowered = 1f - EaseOut(_raise);
		Vector3 offset = LoweredOffset * lowered;
		// Positive tips the item's top toward you, so the forearm behind the hand swings down out of view.
		var rotation = new Basis(Vector3.Right, Mathf.DegToRad(LoweredTiltDegrees) * lowered);

		if (_inspect >= 0f && _toss)
			offset += Vector3.Up * TossHandHeight(_inspect / TossTime);
		else if (_inspect >= 0f)
		{
			float p = _inspect / InspectTime;
			float weight = Mathf.SmoothStep(0f, RaiseEnd, p) * (1f - Mathf.SmoothStep(ShowEnd, 1f, p));
			offset += InspectOffset * weight;
			rotation *= new Basis(Vector3.Back, Mathf.DegToRad(InspectTiltDegrees) * weight)
				* new Basis(Vector3.Right, Mathf.DegToRad(InspectShowTiltDegrees) * ShowAmount(p));
		}

		return AroundHand(rotation, offset);
	}

	// The inspect's twirl: only the item spins, in the fist.
	private Transform3D Twirl()
	{
		if (_inspect < 0f)
			return Transform3D.Identity;
		return AroundHand(new Basis(InspectSpinAxis.Normalized(), TwirlAngle(_inspect / InspectTime)), Vector3.Zero);
	}

	// The hand dips, flicks up to let go, waits, then gives a little as it catches.
	private static float TossHandHeight(float p)
	{
		if (p < TossDipEnd)
			return -TossDip * Mathf.SmoothStep(0f, 1f, p / TossDipEnd);
		if (p < TossRelease)
			return Mathf.Lerp(-TossDip, TossFlick, EaseOut((p - TossDipEnd) / (TossRelease - TossDipEnd)));
		if (p < TossCatch)
			return TossFlick * (1f - Mathf.SmoothStep(0f, 1f, (p - TossRelease) / (TossCatch - TossRelease)));
		float k = (p - TossCatch) / (1f - TossCatch);
		return -CatchDip * Mathf.Sin(Mathf.Min(k * 1.6f, 1f) * Mathf.Pi) * (1f - k);
	}

	// The item in the air, relative to the hand: up and back down, spinning steadily, whole turns so it lands as it left.
	// It straightens out of the hand's tilt while in the air so it spins flat across the screen, instead of swinging
	// its far end at the camera.
	private Transform3D Toss()
	{
		if (_inspect < 0f)
			return Transform3D.Identity;
		float k = Mathf.Clamp((_inspect / TossTime - TossRelease) / (TossCatch - TossRelease), 0f, 1f);
		if (k <= 0f || k >= 1f)
			return Transform3D.Identity;
		float height = 4f * TossHeight * k * (1f - k);
		float angle = Mathf.Tau * TossSpins * k;

		Vector3 up = ItemInHand().Basis.Y.Normalized();
		var flat = new Vector3(up.X, up.Y, 0f);
		Quaternion straighten = Quaternion.Identity;
		if (flat.LengthSquared() > 0.01f)
			straighten = Quaternion.Identity.Slerp(new Quaternion(up, flat.Normalized()), Mathf.SmoothStep(0f, 1f, Mathf.Sin(k * Mathf.Pi) * 1.5f));
		var spin = new Quaternion(TossSpinAxis.Normalized(), angle);
		return AroundHand(new Basis(spin * straighten), Vector3.Up * height);
	}

	private Transform3D ItemInHand()
	{
		if (_grip != null)
			return _gripRest * _grip.Transform.AffineInverse();
		return new Transform3D(BlockBasis, _gripRest * PalmOffset);
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
