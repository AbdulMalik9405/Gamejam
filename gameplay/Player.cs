using Godot;

namespace Gamejam.Gameplay;

/// <summary>
/// First-person player. Only the peer that owns this node (node name = peer id) reads input
/// and drives its camera; remote copies are moved by replication.
/// </summary>
public partial class Player : CharacterBody3D
{
	[Export] public float MoveSpeed { get; set; } = 4.5f;
	[Export] public float SprintSpeed { get; set; } = 7f;
	[Export] public float JumpVelocity { get; set; } = 4.5f;
	[Export] public float Acceleration { get; set; } = 12f;
	[Export] public float AirControl { get; set; } = 0.3f;
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float MaxPitchDegrees { get; set; } = 89f;
	[Export] public float InteractRange { get; set; } = 2.5f;
	// Where the selected item sits, in camera space (right, up, forward is -Z).
	[Export] public Vector3 HoldOffset { get; set; } = new Vector3(0.25f, -0.2f, -0.6f);
	[Export] public float DropSpeed { get; set; } = 1.5f;

	[Signal] public delegate void InteractTargetChangedEventHandler(string prompt);
	[Signal] public delegate void InteractedEventHandler(string interactableId);
	[Signal] public delegate void PickedUpEventHandler(string itemName);
	[Signal] public delegate void DroppedEventHandler(string itemName);

	public long PeerId { get; private set; } = 1;
	// TODO: add `public Era Era { get; }` once core/Era.cs is merged from net-core.
	public string HeldItem => _inventory.SelectedItem == null ? "" : _inventory.SelectedItem.ItemName;
	public Inventory Inventory => _inventory;
	// True while the crosshair is on something F would pick up. Read by the HUD on InteractTargetChanged.
	public bool IsLookingAtPickup { get; private set; }
	// No moving, looking or interacting, e.g. during a cutscene.
	public bool InputLocked { get; set; }

	private static readonly StringName[] SlotActions =
	{
		"hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5", "hotbar_6", "hotbar_7", "hotbar_8", "hotbar_9",
	};

	private Camera3D _camera;
	private MeshInstance3D _body;
	private RayCast3D _interactRay;
	private Inventory _inventory;
	private HeldItemView _heldView;
	private float _gravity;
	private bool _isLocal;

	private Pickup _targetPickup;
	private PickupSlot _targetSlot;
	private Interactable _targetInteractable;
	private Interactable _lastTargetInteractable;
	private Pickup _lastTargetPickup;
	private PickupSlot _lastTargetSlot;
	private Pickup _lastSelected;
	private bool _lastFull;
	private bool _promptDirty = true;
	private string _prompt = "";

	public override void _EnterTree()
	{
		// Spawned players are named after their peer id. A player dropped into a test scene
		// by hand keeps the default authority (1), which is the local peer when offline.
		if (long.TryParse(Name, out long id))
		{
			PeerId = id;
			SetMultiplayerAuthority((int)id);
		}
	}

	public override void _Ready()
	{
		AddToGroup("players");
		_camera = GetNode<Camera3D>("Camera");
		_body = GetNode<MeshInstance3D>("Body");
		_interactRay = GetNode<RayCast3D>("Camera/InteractRay");
		_inventory = GetNode<Inventory>("Inventory");
		_interactRay.TargetPosition = new Vector3(0, 0, -InteractRange);
		_interactRay.AddException(this);
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");

		_isLocal = IsMultiplayerAuthority();
		_camera.Current = _isLocal;
		_interactRay.Enabled = _isLocal;
		if (!_isLocal)
			return;

		// Hide our own body from our camera but keep its shadow; the other player still sees it.
		_body.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
		_inventory.Changed += OnInventoryChanged;
		_heldView = new HeldItemView { Name = "HeldItemView", HoldOffset = HoldOffset };
		AddChild(_heldView);
		_heldView.Setup(this, _camera, _inventory);
		EnsureDefaultInputActions();
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_isLocal || InputLocked)
			return;

		if (@event.IsActionPressed("pause"))
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			return;
		}

		if (Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		if (@event.IsActionPressed("interact"))
			Interact();
		else if (@event.IsActionPressed("drop"))
			DropSelected();
		else if (@event.IsActionPressed("hotbar_next"))
			_inventory.Cycle(1);
		else if (@event.IsActionPressed("hotbar_prev"))
			_inventory.Cycle(-1);
		else if (@event.IsActionPressed("inspect"))
			_heldView.Inspect();
		else if (@event is InputEventMouseMotion motion)
			Look(motion.Relative);
		else
			SelectSlotFromKey(@event);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_isLocal)
			return;

		float dt = (float)delta;
		Vector3 velocity = Velocity;

		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;
		else if (Input.IsActionJustPressed("jump") && Input.MouseMode == Input.MouseModeEnum.Captured && !InputLocked)
			velocity.Y = JumpVelocity;

		Vector2 input = Input.MouseMode == Input.MouseModeEnum.Captured && !InputLocked
			? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
			: Vector2.Zero;
		Vector3 direction = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
		float speed = Input.IsActionPressed("sprint") ? SprintSpeed : MoveSpeed;

		float accel = IsOnFloor() ? Acceleration : Acceleration * AirControl;
		Vector3 horizontal = new Vector3(velocity.X, 0, velocity.Z).Lerp(direction * speed, Mathf.Clamp(accel * dt, 0f, 1f));
		velocity.X = horizontal.X;
		velocity.Z = horizontal.Z;

		Velocity = velocity;
		MoveAndSlide();

		UpdateInteractTarget();
	}

	public override void _ExitTree()
	{
		if (_isLocal && _inventory != null)
			_inventory.Changed -= OnInventoryChanged;
	}

	private void Look(Vector2 relative)
	{
		// Yaw turns the body, pitch only tilts the camera, so movement stays level.
		RotateY(-relative.X * MouseSensitivity);
		Vector3 rot = _camera.Rotation;
		float maxPitch = Mathf.DegToRad(MaxPitchDegrees);
		rot.X = Mathf.Clamp(rot.X - relative.Y * MouseSensitivity, -maxPitch, maxPitch);
		_camera.Rotation = rot;
	}

	// Number keys pick a hotbar slot; the selected slot's number puts its item away or takes it out.
	private void SelectSlotFromKey(InputEvent @event)
	{
		for (int i = 0; i < SlotActions.Length; i++)
		{
			if (@event.IsActionPressed(SlotActions[i]))
			{
				_inventory.Select(i);
				return;
			}
		}
	}

	private void UpdateInteractTarget()
	{
		_targetPickup = null;
		_targetSlot = null;
		_targetInteractable = null;

		if (_interactRay.IsColliding())
		{
			GodotObject hit = _interactRay.GetCollider();
			if (hit is Interactable interactable)
				_targetInteractable = interactable;
			else if (hit is PickupSlot slot)
			{
				_targetSlot = slot;
				_targetPickup = slot.Occupant;
			}
			else if (hit is Pickup pickup && !pickup.IsHeld)
			{
				_targetPickup = pickup;
				_targetSlot = pickup.Slot;
			}
		}

		Pickup selected = _inventory.SelectedItem;
		bool full = _inventory.IsFull;

		// Only rebuild the prompt string when something changed, to avoid per-frame allocations.
		// An Interactable's prompt can change by itself (a torch gets lit), so it's re-read every frame.
		if (!_promptDirty && _targetInteractable == null && _lastTargetInteractable == null
			&& _targetPickup == _lastTargetPickup && _targetSlot == _lastTargetSlot
			&& selected == _lastSelected && full == _lastFull)
			return;
		_promptDirty = false;
		_lastTargetInteractable = _targetInteractable;
		_lastTargetPickup = _targetPickup;
		_lastTargetSlot = _targetSlot;
		_lastSelected = selected;
		_lastFull = full;

		string prompt = "";
		if (_targetInteractable != null)
			prompt = _targetInteractable.GetPrompt(this);
		else if (_targetPickup != null)
			prompt = full ? "Inventory full" : $"Pick up {_targetPickup.ItemName}";
		else if (_targetSlot != null && selected != null)
			prompt = $"Place {selected.ItemName}";

		bool lookingAtPickup = _targetPickup != null && !full;
		if (prompt == _prompt && lookingAtPickup == IsLookingAtPickup)
			return;
		_prompt = prompt;
		IsLookingAtPickup = lookingAtPickup;
		EmitSignal(SignalName.InteractTargetChanged, prompt);
	}

	private void Interact()
	{
		if (_targetInteractable != null)
		{
			_targetInteractable.Interact(this);
			EmitSignal(SignalName.Interacted, _targetInteractable.Id);
			_promptDirty = true;
			return;
		}

		if (_targetPickup != null)
		{
			if (!_inventory.TryAdd(_targetPickup))
				return;
			_targetPickup.Hold();
			EmitSignal(SignalName.PickedUp, _targetPickup.ItemName);
			_promptDirty = true;
			return;
		}

		Pickup selected = _inventory.SelectedItem;
		if (_targetSlot == null || _targetSlot.Occupant != null || selected == null)
			return;

		_inventory.RemoveSelected();
		selected.PlaceIn(_targetSlot);
		EmitSignal(SignalName.Dropped, selected.ItemName);
		_promptDirty = true;
	}

	// Puts an item straight into the hotbar, e.g. a starting item. The item must already be in the tree.
	public bool GiveItem(Pickup pickup)
	{
		if (!_inventory.TryAdd(pickup))
			return false;
		pickup.Hold();
		EmitSignal(SignalName.PickedUp, pickup.ItemName);
		_promptDirty = true;
		return true;
	}

	private void DropSelected()
	{
		Pickup dropped = _inventory.RemoveSelected();
		if (dropped == null)
			return;
		dropped.Drop(Velocity - _camera.GlobalBasis.Z * DropSpeed);
		EmitSignal(SignalName.Dropped, dropped.ItemName);
		_promptDirty = true;
	}

	// HeldItemView shows, hides and animates the items; here only the prompt can change.
	private void OnInventoryChanged() => _promptDirty = true;

	// project.godot (Agent A) has no input map yet. Fill in only missing actions so A's map wins once it exists.
	private static void EnsureDefaultInputActions()
	{
		AddKeyAction("move_forward", Key.W);
		AddKeyAction("move_back", Key.S);
		AddKeyAction("move_left", Key.A);
		AddKeyAction("move_right", Key.D);
		AddKeyAction("jump", Key.Space);
		AddKeyAction("sprint", Key.Shift);
		AddKeyAction("interact", Key.F);
		AddKeyAction("drop", Key.Q);
		AddKeyAction("pause", Key.Escape);
		AddKeyAction("inspect", Key.Y);
		for (int i = 0; i < SlotActions.Length; i++)
			AddKeyAction(SlotActions[i], Key.Key1 + i);
		AddMouseAction("hotbar_next", MouseButton.WheelDown);
		AddMouseAction("hotbar_prev", MouseButton.WheelUp);
	}

	private static void AddKeyAction(string action, Key key)
	{
		if (InputMap.HasAction(action))
			return;
		InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}

	private static void AddMouseAction(string action, MouseButton button)
	{
		if (InputMap.HasAction(action))
			return;
		InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
	}
}
