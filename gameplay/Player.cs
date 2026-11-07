using Godot;

namespace Gamejam.Gameplay;

/// <summary>
/// First-person player. Only the peer that owns this node (node name = peer id) reads input
/// and drives its camera; remote copies are moved by replication.
/// </summary>
public partial class Player : CharacterBody3D
{
    [Signal] public delegate void InteractTargetChangedEventHandler(string prompt);
    [Signal] public delegate void InteractedEventHandler(string interactableId);
    [Signal] public delegate void PickedUpEventHandler(string itemName);
    [Signal] public delegate void DroppedEventHandler(string itemName);

    [Export] public float MoveSpeed { get; set; } = 4.5f;
    [Export] public float SprintSpeed { get; set; } = 7f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;
    [Export] public float Acceleration { get; set; } = 12f;
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;
    [Export] public float MaxPitchDegrees { get; set; } = 89f;

    public long PeerId { get; private set; } = 1;
    public string HeldItem { get; private set; } = "";
    // TODO: add `public Era Era { get; }` once core/Era.cs is merged from net-core.

    private Camera3D _camera;
    private MeshInstance3D _body;
    private float _gravity;
    private bool _isLocal;

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
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");

        _isLocal = IsMultiplayerAuthority();
        _camera.Current = _isLocal;

        if (_isLocal)
        {
            // Hide our own body from our camera but keep its shadow.
            _body.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            EnsureFallbackInputs();
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isLocal)
            return;

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            Vector3 rot = _camera.Rotation;
            float maxPitch = Mathf.DegToRad(MaxPitchDegrees);
            rot.X = Mathf.Clamp(rot.X - motion.Relative.Y * MouseSensitivity, -maxPitch, maxPitch);
            _camera.Rotation = rot;
        }
        else if (@event.IsActionPressed("pause"))
        {
            // Temporary until the pause menu (Agent C) owns mouse mode.
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
        else if (@event is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_isLocal)
            return;

        float dt = (float)delta;
        Vector3 velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y -= _gravity * dt;
        else if (Input.IsActionJustPressed("jump"))
            velocity.Y = JumpVelocity;

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 direction = (Transform.Basis * new Vector3(input.X, 0f, input.Y)).Normalized();
        float speed = Input.IsActionPressed("sprint") ? SprintSpeed : MoveSpeed;

        Vector3 target = direction * speed;
        float blend = Mathf.Min(1f, Acceleration * dt);
        velocity.X = Mathf.Lerp(velocity.X, target.X, blend);
        velocity.Z = Mathf.Lerp(velocity.Z, target.Z, blend);

        Velocity = velocity;
        MoveAndSlide();
    }

    /// <summary>
    /// The input map in project.godot (Agent A) isn't defined yet. Register the agreed default
    /// keys at runtime only for actions that are missing, so the real map always wins.
    /// </summary>
    private static void EnsureFallbackInputs()
    {
        EnsureKey("move_forward", Key.W);
        EnsureKey("move_back", Key.S);
        EnsureKey("move_left", Key.A);
        EnsureKey("move_right", Key.D);
        EnsureKey("jump", Key.Space);
        EnsureKey("sprint", Key.Shift);
        EnsureKey("pause", Key.Escape);
    }

    private static void EnsureKey(string action, Key key)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
