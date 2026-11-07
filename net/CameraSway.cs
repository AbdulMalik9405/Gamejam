using Gamejam.Core;
using Godot;

namespace Gamejam.Net;

// Subtle head-bob for the local player's camera: a bob per step and a slow side-to-side sway while walking,
// a faint breathing drift when standing still. Only offsets the camera's position and roll; Player.cs owns
// pitch (Rotation.X) and keeps the roll we set, so the two never fight.
public partial class CameraSway : Node
{
	[Export] public float StrideLength { get; set; } = 1.6f;
	[Export] public float BobHeight { get; set; } = 0.035f;
	[Export] public float SideSway { get; set; } = 0.02f;
	[Export] public float RollDegrees { get; set; } = 0.5f;
	[Export] public float BreathHeight { get; set; } = 0.006f;
	// Speed at which the bob reaches full strength (Player.MoveSpeed); sprinting goes a little past it.
	[Export] public float ReferenceSpeed { get; set; } = 4.5f;

	private CharacterBody3D _player;
	private Camera3D _camera;
	private Vector3 _restPosition;
	// One full cycle is two steps: left foot, right foot.
	private float _phase;
	private float _amount;
	private float _time;

	public override void _Ready()
	{
		GameState.Instance.GameStarted += OnGameStarted;
	}

	public override void _ExitTree()
	{
		GameState.Instance.GameStarted -= OnGameStarted;
	}

	public override void _Process(double delta)
	{
		if (_camera == null || !IsInstanceValid(_camera) || !IsInstanceValid(_player))
			return;
		float dt = (float)delta;
		_time += dt;

		Vector3 velocity = _player.Velocity;
		float speed = new Vector2(velocity.X, velocity.Z).Length();
		float target = _player.IsOnFloor() ? Mathf.Clamp(speed / ReferenceSpeed, 0f, 1.3f) : 0f;
		// Ease in and out so starting, stopping and landing don't snap the camera.
		_amount = Mathf.Lerp(_amount, target, 1f - Mathf.Exp(-8f * dt));
		_phase = Mathf.Wrap(_phase + speed * dt * Mathf.Pi / StrideLength, 0f, Mathf.Tau);

		float bob = Mathf.Sin(_phase * 2f) * BobHeight * _amount;
		float side = Mathf.Sin(_phase) * SideSway * _amount;
		float breath = Mathf.Sin(_time * 1.4f) * BreathHeight * (1f - Mathf.Min(_amount, 1f));
		_camera.Position = _restPosition + new Vector3(side, bob + breath, 0f);

		Vector3 rotation = _camera.Rotation;
		rotation.Z = Mathf.Sin(_phase) * Mathf.DegToRad(RollDegrees) * _amount;
		_camera.Rotation = rotation;
	}

	private void OnGameStarted()
	{
		_player = GameState.Instance.GetPlayerNode(Multiplayer.GetUniqueId()) as CharacterBody3D;
		_camera = _player?.GetNodeOrNull<Camera3D>("Camera");
		if (_camera != null)
			_restPosition = _camera.Position;
		_phase = 0f;
		_amount = 0f;
	}
}
