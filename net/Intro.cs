using Gamejam.Core;
using Godot;

namespace Gamejam.Net;

// Plays locally on each peer once both players are in: a swaying look at the intact tomb, a growing tremor and
// time rupture, a white flash, then each player appears at their era's spawn with "Solve the puzzle".
// Visual only; the one thing it touches is pausing and placing the local player.
public partial class Intro : Node
{
	// Inside the intact (past) tomb, looking north across the room.
	[Export] public Vector3 CameraPosition { get; set; } = new(-3.73f, 2.2f, 13.36f);
	[Export] public Vector3 LookTarget { get; set; } = new(-3.73f, 1.0f, 5.86f);
	[Export] public float SwayDuration { get; set; } = 3.5f;
	[Export] public float RuptureDuration { get; set; } = 2.0f;
	[Export] public float FlashDuration { get; set; } = 0.5f;
	[Export] public float MessageHold { get; set; } = 2.0f;
	[Export] public float MessageFade { get; set; } = 1.5f;
	[Export] public float DollySpeed { get; set; } = 0.12f;
	[Export] public float MaxShake { get; set; } = 0.08f;

	private static readonly StringName StrengthParam = "strength";
	private static readonly StringName FlashParam = "flash";

	private Session _session;
	private CanvasLayer _hud;
	private Camera3D _camera;
	private ColorRect _rupture;
	private ShaderMaterial _ruptureMaterial;
	private Control _message;
	private Label _subtitle;
	private Node3D _localPlayer;
	private Transform3D _baseView;
	// Seconds since the intro started; negative while it isn't playing.
	private double _time = -1;
	private bool _swapped;

	public override void _Ready()
	{
		_session = GetParent<Session>();
		_hud = GetNodeOrNull<CanvasLayer>("../Hud");
		Build();
		GameState.Instance.GameStarted += OnGameStarted;
	}

	public override void _ExitTree()
	{
		GameState.Instance.GameStarted -= OnGameStarted;
	}

	public override void _Process(double delta)
	{
		if (_time < 0)
			return;
		_time += delta;
		float t = (float)_time;
		float flashStart = SwayDuration + RuptureDuration;
		float swapAt = flashStart + FlashDuration;

		if (!_swapped)
		{
			float rupture = Mathf.Clamp((t - SwayDuration) / RuptureDuration, 0f, 1f);
			float flash = Mathf.Clamp((t - flashStart) / FlashDuration, 0f, 1f);
			UpdateCamera(t, rupture);
			_rupture.Visible = rupture > 0f;
			_ruptureMaterial.SetShaderParameter(StrengthParam, rupture);
			_ruptureMaterial.SetShaderParameter(FlashParam, flash);
			if (t >= swapAt)
				SwapToPlayer();
			return;
		}

		float after = t - swapAt;
		float flashLeft = 1f - Mathf.Clamp(after / FlashDuration, 0f, 1f);
		_ruptureMaterial.SetShaderParameter(FlashParam, flashLeft);
		_rupture.Visible = flashLeft > 0f;

		float fadeIn = Mathf.Clamp(after / 0.4f, 0f, 1f);
		float fadeOut = 1f - Mathf.Clamp((after - FlashDuration - MessageHold) / MessageFade, 0f, 1f);
		_message.Modulate = new Color(1f, 1f, 1f, Mathf.Min(fadeIn, fadeOut));
		if (after >= FlashDuration + MessageHold + MessageFade)
			Finish();
	}

	private void OnGameStarted()
	{
		// Paused (input, movement and position sync) until it reappears at its spawn.
		_localPlayer = GameState.Instance.GetPlayerNode(Multiplayer.GetUniqueId());
		if (_localPlayer != null)
			_localPlayer.ProcessMode = ProcessModeEnum.Disabled;
		if (_hud != null)
			_hud.Visible = false;

		_baseView = new Transform3D(Basis.Identity, CameraPosition).LookingAt(LookTarget, Vector3.Up);
		_camera.GlobalTransform = _baseView;
		_camera.MakeCurrent();
		_subtitle.Text = GameState.Instance.LocalEra == Era.Past ? "You are in the past" : "You are in the present";
		_ruptureMaterial.SetShaderParameter(StrengthParam, 0f);
		_ruptureMaterial.SetShaderParameter(FlashParam, 0f);
		_message.Visible = false;
		_swapped = false;
		_time = 0;
	}

	private void UpdateCamera(float t, float rupture)
	{
		// Slow drift forward with a gentle handheld sway, then a tremor that builds with the rupture.
		Vector3 position = _baseView.Origin - _baseView.Basis.Z * DollySpeed * t;
		float yaw = Mathf.Sin(t * 0.6f) * 0.035f;
		float pitch = Mathf.Sin(t * 0.85f + 1f) * 0.02f;
		float roll = Mathf.Sin(t * 0.45f) * 0.015f;

		float shake = rupture * rupture * MaxShake;
		position += new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * shake;
		roll += (GD.Randf() - 0.5f) * shake * 0.6f;

		Basis basis = _baseView.Basis * Basis.FromEuler(new Vector3(pitch, yaw, roll));
		_camera.GlobalTransform = new Transform3D(basis, position);
	}

	// Happens under the full white flash, so the cut is hidden.
	private void SwapToPlayer()
	{
		_swapped = true;
		_ruptureMaterial.SetShaderParameter(StrengthParam, 0f);

		if (_localPlayer != null && IsInstanceValid(_localPlayer))
		{
			Marker3D spawn = _session.FindSpawnPoint(GameState.Instance.LocalEra);
			if (spawn != null)
				_localPlayer.GlobalTransform = spawn.GlobalTransform;
			if (_localPlayer is CharacterBody3D body)
				body.Velocity = Vector3.Zero;
			_localPlayer.ProcessMode = ProcessModeEnum.Inherit;

			Camera3D playerCamera = _localPlayer.GetNodeOrNull<Camera3D>("Camera");
			if (playerCamera != null)
			{
				playerCamera.Rotation = Vector3.Zero;
				playerCamera.MakeCurrent();
			}
		}

		if (_hud != null)
			_hud.Visible = true;
		_message.Modulate = new Color(1f, 1f, 1f, 0f);
		_message.Visible = true;
	}

	private void Finish()
	{
		_time = -1;
		_message.Visible = false;
		_rupture.Visible = false;
	}

	private void Build()
	{
		_camera = new Camera3D { Name = "IntroCamera", Fov = 70f };
		AddChild(_camera);

		// Above the HUD (default layer) and the lobby (10).
		var overlay = new CanvasLayer { Name = "IntroOverlay", Layer = 20 };
		AddChild(overlay);

		_ruptureMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://net/time_rupture.gdshader") };
		_rupture = new ColorRect
		{
			Material = _ruptureMaterial,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_rupture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(_rupture);

		var box = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center,
			Visible = false,
		};
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		box.AddChild(MakeLabel("Solve the puzzle", 56));
		_subtitle = MakeLabel("", 24);
		box.AddChild(_subtitle);
		overlay.AddChild(box);
		_message = box;
	}

	private static Label MakeLabel(string text, int size)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_outline_color", Colors.Black);
		label.AddThemeConstantOverride("outline_size", size / 6);
		return label;
	}
}
