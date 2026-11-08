using Gamejam.Core;
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
	// How far off the crosshair a small pickup or slot (e.g. a pyramid) can be and still be aimed at.
	[Export] public float AimAssistRadius { get; set; } = 0.12f;
	// Ledges up to this high (stairs, slab edges, door sills, the edge of the sunken floor round the sarcophagus) are
	// stepped up onto without jumping.
	[Export] public float MaxStepHeight { get; set; } = 0.55f;
	// The body others see (and whose shadow you see): the explorer in the present, the Egyptian in the past.
	[Export] public PackedScene PresentCharacter { get; set; }
	[Export] public PackedScene PastCharacter { get; set; }

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
	// The past player after the ceiling collapse: they walk the present as the mummy. A mummy only moves and looks:
	// it can't pick up, place, drop or hold anything (pickups are kept per machine, so it would desync the explorer's).
	public bool IsMummy { get; private set; }
	// The rigged character standing in for the capsule once the era is known; null until then.
	public CharacterModel Character { get; private set; }

	private static readonly StringName[] SlotActions =
	{
		"hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5", "hotbar_6", "hotbar_7", "hotbar_8", "hotbar_9",
	};

	private Camera3D _camera;
	private MeshInstance3D _body;
	private RayCast3D _interactRay;
	private ShapeCast3D _aimAssist;
	private KinematicCollision3D _stepCollision;
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
	// With a torch out, looking at a wall: where it would be put.
	private bool _canMount;
	private bool _lastCanMount;
	private Vector3 _mountPoint;
	// What this player has out, as last told to the other machine ("" = empty hands), and when to repeat it.
	private string _shownItem = "";
	private float _resendShown;
	// On the other machine: the copy of what this player has out, carried by its character.
	private Node3D _heldCopy;
	private string _heldCopyOf = "";
	private Vector3 _mountNormal;
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
		_aimAssist = new ShapeCast3D
		{
			Name = "AimAssist",
			Shape = new SphereShape3D { Radius = AimAssistRadius },
			CollisionMask = Pickup.InteractablesLayerBit,
			CollideWithAreas = true,
			Enabled = false,
			MaxResults = 8,
		};
		_aimAssist.AddException(this);
		_camera.AddChild(_aimAssist);
		_stepCollision = new KinematicCollision3D();
		FloorSnapLength = Mathf.Max(FloorSnapLength, MaxStepHeight);
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

	public override void _Process(double delta)
	{
		// The era arrives from net/Session just after this node is ready, so the body is swapped in once it's known.
		if (Character == null)
			TryCreateCharacter();
		if (_isLocal)
			ShareHeldItem((float)delta);
	}

	// Turns this player into the mummy, on every machine: its body becomes the mummy (arms out, a running stride). On
	// the player's own machine it's also moved to `at` (only the owner moves a player; the rest follows by sync), and
	// whatever it held is put away.
	public void BecomeMummy(PackedScene mummy, Transform3D at, float scale)
	{
		if (IsMummy)
			return;
		IsMummy = true;
		if (Character != null)
		{
			RemoveChild(Character);
			Character.QueueFree();
		}
		Character = new CharacterModel
		{
			Name = "Character",
			Scene = mummy,
			Reach = 1f,
			WalkSpeed = 3f,
			StrideLength = 1.5f,
			LegSwingDegrees = 38f,
			ArmDownDegrees = 38f,
			ElbowBendDegrees = 12f,
			Scale = Vector3.One * scale,
			HeldItem = _heldCopy,
		};
		AddChild(Character);
		_body.Visible = false;
		if (!_isLocal)
			return;
		Character.SetCastShadow(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
		_inventory.PutAway();
		Vector3 forward = -at.Basis.Z;
		GlobalPosition = at.Origin;
		Rotation = new Vector3(0f, Mathf.Atan2(-forward.X, -forward.Z), 0f);
		_camera.Rotation = Vector3.Zero;
		Velocity = Vector3.Zero;
		_promptDirty = true;
	}

	private void TryCreateCharacter()
	{
		if (GameState.Instance == null || !GameState.Instance.Players.TryGetValue(PeerId, out PlayerInfo info))
			return;
		PackedScene scene = info.Era == Era.Present
			? PresentCharacter ?? GD.Load<PackedScene>("res://art/explorer.glb")
			: PastCharacter ?? GD.Load<PackedScene>("res://art/egyptian.glb");
		Character = new CharacterModel { Name = "Character", Scene = scene, HeldItem = _heldCopy };
		AddChild(Character);
		_body.Visible = false;
		// Our own body stays out of our camera, but its shadow shows.
		if (_isLocal)
			Character.SetCastShadow(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
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

		if (IsMummy)
		{
			if (@event is InputEventMouseMotion mummyLook)
				Look(mummyLook.Relative);
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
		Vector3 before = GlobalPosition;
		MoveAndSlide();
		StepUp(before, horizontal * dt);

		UpdateInteractTarget();
	}

	public override void _ExitTree()
	{
		if (_isLocal && _inventory != null)
			_inventory.Changed -= OnInventoryChanged;
	}

	// When walking into a low ledge stopped us, tries the move again from MaxStepHeight up and settles onto the
	// ledge: up, across, then down onto walkable floor.
	private void StepUp(Vector3 before, Vector3 wanted)
	{
		wanted.Y = 0f;
		if (!IsOnFloor() || !IsOnWall() || wanted.LengthSquared() < 1e-6f)
			return;
		Vector3 moved = GlobalPosition - before;
		moved.Y = 0f;
		Vector3 missing = wanted - wanted.Normalized() * Mathf.Min(moved.Dot(wanted.Normalized()), wanted.Length());
		if (missing.LengthSquared() < 1e-6f)
			return;
		// A frame's worth of walking is only a centimetre or two, which leaves the round bottom of the body still on
		// the ledge's edge, too steep to stand on. Stepping at least this far forward clears the edge.
		const float minStep = 0.15f;
		if (missing.Length() < minStep)
			missing = missing.Normalized() * minStep;

		Transform3D from = GlobalTransform;
		Vector3 up = Vector3.Up * MaxStepHeight;
		if (TestMove(from, up, _stepCollision))
			up = _stepCollision.GetTravel();
		Transform3D raised = from.Translated(up);
		if (TestMove(raised, missing, _stepCollision))
			return;
		Transform3D across = raised.Translated(missing);
		if (!TestMove(across, Vector3.Down * (up.Y + 0.05f), _stepCollision))
			return;
		if (_stepCollision.GetNormal().Dot(Vector3.Up) < Mathf.Cos(FloorMaxAngle))
			return;
		GlobalPosition = across.Origin + _stepCollision.GetTravel();
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
		if (IsMummy)
		{
			if (_prompt != "")
			{
				_prompt = "";
				IsLookingAtPickup = false;
				EmitSignal(SignalName.InteractTargetChanged, "");
			}
			return;
		}
		_targetPickup = null;
		_targetSlot = null;
		_targetInteractable = null;
		_canMount = false;

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

		if (_targetInteractable == null && _targetPickup == null && _targetSlot == null)
			AimAssist();

		Pickup selected = _inventory.SelectedItem;
		bool full = _inventory.IsFull;

		// A torch goes on any steep enough surface that isn't something to interact with.
		if (selected is Torch && _interactRay.IsColliding() && _targetInteractable == null && _targetPickup == null
			&& _targetSlot == null && Mathf.Abs(_interactRay.GetCollisionNormal().Y) < 0.35f)
		{
			_canMount = true;
			_mountPoint = _interactRay.GetCollisionPoint();
			_mountNormal = _interactRay.GetCollisionNormal();
		}

		// Only rebuild the prompt string when something changed, to avoid per-frame allocations.
		// An Interactable's prompt can change by itself (a torch gets lit), so it's re-read every frame.
		if (!_promptDirty && _targetInteractable == null && _lastTargetInteractable == null
			&& _targetPickup == _lastTargetPickup && _targetSlot == _lastTargetSlot
			&& selected == _lastSelected && full == _lastFull && _canMount == _lastCanMount)
			return;
		_promptDirty = false;
		_lastTargetInteractable = _targetInteractable;
		_lastTargetPickup = _targetPickup;
		_lastTargetSlot = _targetSlot;
		_lastSelected = selected;
		_lastFull = full;
		_lastCanMount = _canMount;

		string prompt = "";
		if (_targetInteractable != null)
			prompt = _targetInteractable.GetPrompt(this);
		else if (_targetPickup != null)
			prompt = full ? "Inventory full" : $"Pick up {_targetPickup.ItemName}";
		else if (_targetSlot != null && selected != null)
			prompt = _targetSlot.CanHold(selected) ? $"Place {selected.ItemName}" : $"Only a {_targetSlot.Accepts} fits here";
		else if (_canMount)
			prompt = "Mount torch";

		bool lookingAtPickup = _targetPickup != null && !full;
		if (prompt == _prompt && lookingAtPickup == IsLookingAtPickup)
			return;
		_prompt = prompt;
		IsLookingAtPickup = lookingAtPickup;
		EmitSignal(SignalName.InteractTargetChanged, prompt);
	}

	// A thin sphere cast along the view, stopped where the ray hit, picks the pickup, slot or interactable closest
	// to the crosshair line, so small things don't need pixel-perfect aim and nothing is reached through a wall.
	private void AimAssist()
	{
		Vector3 eye = _camera.GlobalPosition;
		Vector3 forward = -_camera.GlobalBasis.Z;
		float reach = _interactRay.IsColliding() ? eye.DistanceTo(_interactRay.GetCollisionPoint()) : InteractRange;
		_aimAssist.TargetPosition = new Vector3(0f, 0f, -reach);
		_aimAssist.ForceShapecastUpdate();

		float best = float.MaxValue;
		for (int i = 0; i < _aimAssist.GetCollisionCount(); i++)
		{
			GodotObject hit = _aimAssist.GetCollider(i);
			if (hit is not (Pickup or PickupSlot or Interactable) || hit is Pickup { IsHeld: true })
				continue;
			var node = (Node3D)hit;
			Vector3 offset = node.GlobalPosition - eye;
			float along = offset.Dot(forward);
			if (along <= 0f || along > InteractRange)
				continue;
			float off = (offset - forward * along).LengthSquared();
			if (off >= best)
				continue;
			best = off;
			_targetInteractable = hit as Interactable;
			_targetSlot = hit as PickupSlot;
			_targetPickup = hit as Pickup;
			if (_targetSlot != null)
				_targetPickup = _targetSlot.Occupant;
			else if (_targetPickup != null)
				_targetSlot = _targetPickup.Slot;
		}
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
		if (_canMount && selected is Torch torch)
		{
			_inventory.RemoveSelected();
			torch.MountOnWall(_mountPoint, _mountNormal);
			EmitSignal(SignalName.Dropped, torch.ItemName);
			_promptDirty = true;
			return;
		}

		if (_targetSlot == null || _targetSlot.Occupant != null || !_targetSlot.CanHold(selected))
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

	// Tells the other machine what's in our hand, when it changes and again every couple of seconds (so a partner
	// whose copy of us appeared later still finds out).
	private void ShareHeldItem(float dt)
	{
		string item = DescribeHeld(IsMummy ? null : _inventory.SelectedItem);
		_resendShown -= dt;
		if (item == _shownItem && _resendShown > 0f)
			return;
		_shownItem = item;
		_resendShown = 2f;
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.GetPeers().Length > 0)
			Rpc(MethodName.ShowHeld, item);
	}

	// A held item as the other machine can find it: its scene if it was made from one (the torch, keys), otherwise
	// its path in the level (pyramids, blocks, the seal), which is the same on both machines.
	private string DescribeHeld(Pickup item)
	{
		if (item == null)
			return "";
		if (item.SceneFilePath != "")
			return item.SceneFilePath;
		Node scene = GetTree().CurrentScene;
		return scene != null && scene.IsAncestorOf(item) ? "path:" + scene.GetPathTo(item) : "";
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ShowHeld(string item)
	{
		if (item == _heldCopyOf && (item == "" || IsInstanceValid(_heldCopy)))
			return;
		_heldCopyOf = item;
		if (IsInstanceValid(_heldCopy))
			_heldCopy.QueueFree();
		_heldCopy = MakeHeldCopy(item);
		if (_heldCopy != null)
			AddChild(_heldCopy);
		if (Character != null)
			Character.HeldItem = _heldCopy;
	}

	// Something to carry that looks like the item: a fresh one from its scene (inert: frozen, no collision), or a copy
	// of the level pickup's meshes (and its grip point).
	private Node3D MakeHeldCopy(string item)
	{
		if (item == "")
			return null;
		if (!item.StartsWith("path:"))
		{
			if (!ResourceLoader.Exists(item) || GD.Load<PackedScene>(item)?.Instantiate() is not Node3D fresh)
				return null;
			fresh.Name = "HeldCopy";
			if (fresh is RigidBody3D body)
			{
				body.Freeze = true;
				body.CollisionLayer = 0;
				body.CollisionMask = 0;
			}
			// A pickup's own setup would add it back to the interact layer and the pickups group.
			if (fresh is Pickup pickup)
			{
				pickup.CreateSlotAtStart = false;
				pickup.Ready += () => { pickup.CollisionLayer = 0; pickup.RemoveFromGroup("pickups"); };
			}
			return fresh;
		}
		if (GetTree().CurrentScene?.GetNodeOrNull<Node3D>(item.Substring(5)) is not Node3D source)
			return null;
		var copy = new Node3D { Name = "HeldCopy" };
		Transform3D toSource = source.GlobalTransform.AffineInverse();
		foreach (Node node in source.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mesh = (MeshInstance3D)node;
			var part = new MeshInstance3D { Mesh = mesh.Mesh, Transform = toSource * mesh.GlobalTransform };
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				part.SetSurfaceOverrideMaterial(i, mesh.GetActiveMaterial(i));
			copy.AddChild(part);
		}
		if (source.GetNodeOrNull<Node3D>("Grip") is Node3D grip)
			copy.AddChild(new Marker3D { Name = "Grip", Transform = grip.Transform });
		return copy;
	}

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
