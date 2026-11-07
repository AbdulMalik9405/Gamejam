using Godot;

namespace Gamejam.Gameplay;

// Holds the selected hotbar item in front of the local player's camera and animates it: it rises into view when
// equipped, drops out of view when put away, and `inspect` (Y) turns it around in front of you, CS-style.
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
	[Export] public float InspectTime { get; set; } = 2.6f;
	// Inspect pose relative to the hand: pulled toward the middle of the screen and a little closer.
	[Export] public Vector3 InspectOffset { get; set; } = new(-0.18f, 0.06f, 0.15f);
	[Export] public float InspectTiltDegrees { get; set; } = 25f;

	private const string GripName = "Grip";
	// Turns an item's +X face (the glyph side of the wall blocks) toward the camera.
	private static readonly Basis BlockBasis = new(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0));

	private Camera3D _camera;
	private Inventory _inventory;
	private Pickup _shown;
	private Node3D _grip;
	// 0 = out of view, 1 = in the hand.
	private float _raise;
	// Seconds into the inspect; negative when not inspecting.
	private float _inspect = -1f;

	public override void _Ready()
	{
		// After the camera has moved this frame (mouse look, camera sway), so the item doesn't trail behind it.
		ProcessPriority = 100;
	}

	public void Setup(Camera3D camera, Inventory inventory)
	{
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

		if (_shown != null)
			_shown.GlobalTransform = _camera.GlobalTransform * AnimatedHand() * ItemInHand() * InspectSpin();
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

	// The hand's animated offset from its resting pose: a transform in camera space that pivots on the hand.
	private Transform3D AnimatedHand()
	{
		float lowered = 1f - EaseOut(_raise);
		Vector3 offset = LoweredOffset * lowered;
		var rotation = new Basis(Vector3.Right, -Mathf.DegToRad(LoweredTiltDegrees) * lowered);

		if (_inspect >= 0f)
		{
			float p = _inspect / InspectTime;
			// Moves in and out over the first and last 15 %, one slow turn in between.
			float weight = Mathf.SmoothStep(0f, 0.15f, p) * (1f - Mathf.SmoothStep(0.85f, 1f, p));
			offset += InspectOffset * weight;
			rotation *= new Basis(Vector3.Back, Mathf.DegToRad(InspectTiltDegrees) * weight);
		}

		return new Transform3D(rotation, HoldOffset + offset) * new Transform3D(Basis.Identity, -HoldOffset);
	}

	private Transform3D ItemInHand()
	{
		if (_grip != null)
			return new Transform3D(Basis.Identity, HoldOffset) * _grip.Transform.AffineInverse();
		return new Transform3D(BlockBasis, HoldOffset);
	}

	// One slow turn around the item's own up axis during the inspect.
	private Transform3D InspectSpin()
	{
		if (_inspect < 0f)
			return Transform3D.Identity;
		float spin = Mathf.SmoothStep(0.1f, 0.9f, _inspect / InspectTime) * Mathf.Tau;
		return new Transform3D(new Basis(Vector3.Up, spin), Vector3.Zero);
	}

	private static float EaseOut(float t)
	{
		float inverse = 1f - t;
		return 1f - inverse * inverse * inverse;
	}
}
