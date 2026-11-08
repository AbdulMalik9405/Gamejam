using System;
using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// The local cutscene for a puzzle reward, started by RiseOnSolved: the room rumbles, the view cuts to the reward as
// it rises, it glows gold, then the view returns to the player. Only the local player's screen and input change.
public partial class RevealCinematic : Node
{
	private const float RumbleTime = 0.8f;
	private const float FadeTime = 0.2f;
	private const float GlowIn = 0.4f;
	private const float GlowHold = 1.0f;
	private const float GlowOut = 0.6f;
	private const float RumbleShake = 0.025f;
	private const float RiseShake = 0.012f;
	private const float GlowLightEnergy = 0.18f;
	private const float GlowStrength = 0.3f;
	private const float GlowLightRange = 1.2f;

	private Player _player;
	private Camera3D _playerCamera;
	private CanvasLayer _hud;
	private Node3D _target;
	private Vector3 _focus;
	private float _riseTime;
	private Action _startRise;
	private Color _glowColor;

	private Camera3D _shot;
	private Vector3 _shotFrom;
	private Vector3 _shotTo;
	private ColorRect _fade;
	private StandardMaterial3D _glowMaterial;
	private OmniLight3D _light;
	private Godot.Collections.Array<Node> _meshes;
	private float _time;
	private bool _cutToShot;
	private bool _cutBack;

	// Plays the cutscene for target, which rises to focus (global) over riseTime once startRise is called.
	// Returns false when there's no local player to show it to; the caller then just starts the rise.
	public static bool Play(Node host, Node3D target, Vector3 focus, float riseTime, Color glowColor, Action startRise)
	{
		var player = GameState.Instance?.GetPlayerNode(host.Multiplayer.GetUniqueId()) as Player;
		Camera3D camera = player?.GetNodeOrNull<Camera3D>("Camera");
		if (camera == null)
			return false;

		var cinematic = new RevealCinematic
		{
			Name = "RevealCinematic",
			_player = player,
			_playerCamera = camera,
			_hud = host.GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud"),
			_target = target,
			_focus = focus,
			_riseTime = riseTime,
			_glowColor = glowColor,
			_startRise = startRise,
		};
		host.AddChild(cinematic);
		return true;
	}

	public override void _Ready()
	{
		_player.InputLocked = true;
		Build();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		float t = _time;
		float shotStart = RumbleTime;
		float glowStart = shotStart + _riseTime;
		float glowEnd = glowStart + GlowIn + GlowHold + GlowOut;
		float back = glowEnd + 0.2f;

		if (!_cutToShot && t >= shotStart)
			CutToShot();
		if (!_cutBack && t >= back)
			CutBack();

		// Fades to black just before each cut and back in just after it.
		float fade = Mathf.Max(FadeAround(t, shotStart), FadeAround(t, back));
		_fade.Color = new Color(0f, 0f, 0f, fade);

		Camera3D camera = _cutToShot && !_cutBack ? _shot : _playerCamera;
		float shake = 0f;
		if (t < shotStart)
			shake = RumbleShake * Mathf.SmoothStep(0f, 1f, t / shotStart);
		else if (t < glowStart)
			shake = RiseShake * (1f - (t - shotStart) / _riseTime);
		camera.HOffset = (GD.Randf() - 0.5f) * 2f * shake;
		camera.VOffset = (GD.Randf() - 0.5f) * 2f * shake;

		if (_cutToShot && !_cutBack)
		{
			// A slow push-in for the whole shot.
			float k = Mathf.SmoothStep(0f, 1f, (t - shotStart) / (back - shotStart));
			_shot.GlobalPosition = _shotFrom.Lerp(_shotTo, k);
			_shot.LookAt(_focus, Vector3.Up);
		}

		float glow = Mathf.Clamp((t - glowStart) / GlowIn, 0f, 1f)
			* (1f - Mathf.Clamp((t - glowStart - GlowIn - GlowHold) / GlowOut, 0f, 1f));
		glow = Mathf.SmoothStep(0f, 1f, glow);
		_glowMaterial.AlbedoColor = _glowColor with { A = GlowStrength * glow };
		// The light builds slowly from the moment the reward starts coming out (e.g. as a wall opening opens),
		// so it seems to spill out with it, then fades with the glow.
		float lightIn = Mathf.Clamp((t - shotStart) / (glowStart + GlowIn - shotStart), 0f, 1f);
		float lightOut = 1f - Mathf.Clamp((t - glowStart - GlowIn - GlowHold) / GlowOut, 0f, 1f);
		_light.LightEnergy = GlowLightEnergy * Mathf.SmoothStep(0f, 1f, lightIn) * lightOut;

		if (t >= back + FadeTime)
			Finish();
	}

	private static float FadeAround(float t, float cut) =>
		t < cut ? Mathf.Clamp((t - (cut - FadeTime)) / FadeTime, 0f, 1f) : 1f - Mathf.Clamp((t - cut) / FadeTime, 0f, 1f);

	private void Build()
	{
		var overlay = new CanvasLayer { Name = "Fade", Layer = 15 };
		AddChild(overlay);
		_fade = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
		_fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(_fade);

		// Faces the reward's flat side (its local Z, e.g. a key's face) from the side the player is on, so the
		// whole shape shows and the camera stays inside the room.
		Vector3 toPlayer = _player.GlobalPosition - _focus;
		toPlayer.Y = 0f;
		toPlayer = toPlayer.LengthSquared() > 0.01f ? toPlayer.Normalized() : Vector3.Back;
		Vector3 face = _target.GlobalBasis.Z;
		face.Y = 0f;
		Vector3 side = face.LengthSquared() > 0.1f ? face.Normalized() : toPlayer;
		if (side.Dot(toPlayer) < 0f)
			side = -side;
		// A reward lying flat (e.g. on a ledge) is seen from higher up, so its whole top shows. One that slides out
		// of a wall (it starts away from focus, sideways) is filmed from in front of the opening, leaning toward
		// the player, so whatever stands between the player and the wall stays out of the shot.
		bool flat = Mathf.Abs(_target.GlobalBasis.Z.Normalized().Y) > 0.7f;
		Vector3 slide = _focus - _target.GlobalPosition;
		slide.Y = 0f;
		if (flat && slide.LengthSquared() > 0.01f)
			side = (slide.Normalized() + toPlayer * 0.5f).Normalized();
		_shotFrom = _focus + side * 0.75f + Vector3.Up * (flat ? 0.45f : 0.22f);
		_shotTo = _focus + side * 0.5f + Vector3.Up * (flat ? 0.3f : 0.1f);
		_shot = new Camera3D { Name = "Shot", Fov = 60f };
		AddChild(_shot);

		// A gold sheen laid over the reward's own materials, and a warm light so it lights up its corner.
		_glowMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			AlbedoColor = Colors.Transparent,
		};
		_meshes = _target.FindChildren("*", "MeshInstance3D", true, false);
		foreach (Node mesh in _meshes)
			((MeshInstance3D)mesh).MaterialOverlay = _glowMaterial;
		_light = new OmniLight3D { LightColor = _glowColor, LightEnergy = 0f, OmniRange = GlowLightRange, ShadowEnabled = false };
		_target.AddChild(_light);
	}

	private void CutToShot()
	{
		_cutToShot = true;
		_playerCamera.HOffset = 0f;
		_playerCamera.VOffset = 0f;
		_shot.GlobalPosition = _shotFrom;
		_shot.LookAt(_focus, Vector3.Up);
		_shot.MakeCurrent();
		if (_hud != null)
			_hud.Visible = false;
		_startRise?.Invoke();
	}

	private void CutBack()
	{
		_cutBack = true;
		_shot.HOffset = 0f;
		_shot.VOffset = 0f;
		if (IsInstanceValid(_playerCamera))
			_playerCamera.MakeCurrent();
		if (_hud != null && IsInstanceValid(_hud))
			_hud.Visible = true;
		if (IsInstanceValid(_player))
			_player.InputLocked = false;
	}

	private void Finish()
	{
		foreach (Node mesh in _meshes)
		{
			if (IsInstanceValid(mesh))
				((MeshInstance3D)mesh).MaterialOverlay = null;
		}
		if (IsInstanceValid(_light))
			_light.QueueFree();
		QueueFree();
	}

	public override void _ExitTree()
	{
		// Never leave the player stuck if this goes away early (scene reload, partner left).
		if (!_cutBack && IsInstanceValid(_player))
			_player.InputLocked = false;
	}
}
