using Gamejam.Core;
using Godot;

namespace Gamejam.Net;

// The lobby backdrop and the intro, played locally on each peer. In the lobby the camera sways gently over the
// intact tomb. Once both players are in, each sees a slow glide round their own era's room; then a time rupture
// ripples in and, at its height, gives a glimpse of the same spot in the other era, then settles back in your own room
// for a moment before a white flash that softens into the logo; then each player appears at their era's spawn, told who they are
// and what they must do.
// Visual only; the one thing it touches is pausing and placing the local player.
public partial class Intro : Node
{
	// Inside the intact (past) tomb, looking north across the room.
	[Export] public Vector3 CameraPosition { get; set; } = new(-3.73f, 2.2f, 13.36f);
	[Export] public Vector3 LookTarget { get; set; } = new(-3.73f, 1.0f, 5.86f);
	// The glide round your own room (each era's room centre; the rooms are laid out alike, 200 m apart), the rupture
	// building, and the glimpse of the other era.
	[Export] public Vector3 PastRoomCentre { get; set; } = new(-3.73f, 0f, 8.864f);
	[Export] public Vector3 PresentRoomCentre { get; set; } = new(196.27f, 0f, 8.864f);
	[Export] public float SwayDuration { get; set; } = 5.0f;
	[Export] public float RuptureDuration { get; set; } = 1.5f;
	[Export] public float PeekDuration { get; set; } = 2.4f;
	// Back in your own room after the glimpse, the rupture settling, before the flash and the logo.
	[Export] public float ReturnDuration { get; set; } = 2.5f;
	[Export] public float FlashDuration { get; set; } = 0.5f;
	[Export] public float LogoFadeIn { get; set; } = 1.0f;
	[Export] public float LogoHold { get; set; } = 1.8f;
	[Export] public float LogoFadeOut { get; set; } = 0.8f;
	[Export] public float LogoSize { get; set; } = 760f;
	[Export] public float RevealDuration { get; set; } = 0.9f;
	[Export] public float MessageHold { get; set; } = 3.5f;
	[Export] public float MessageFade { get; set; } = 1.5f;
	// Who each player is: the explorer (present) trapped in the tomb, the Egyptian (past) reaching them across time.
	[Export] public string PresentTitle { get; set; } = "You are trapped in an ancient Egyptian tomb";
	[Export] public string PresentLine { get; set; } = "You must escape.";
	[Export] public string PastTitle { get; set; } = "A disruption in space and time";
	[Export] public string PastLine { get; set; } = "has let you contact someone in the future who needs your help.";
	// Shown under the message to the present player, who starts with the torch, with extra time to read it.
	[Export] public string PresentTip { get; set; } = "Tip: with your torch out, press F on any wall to mount it there, and F again to take it back.";
	[Export] public float TipHold { get; set; } = 2.5f;
	[Export] public float DollySpeed { get; set; } = 0.12f;
	[Export] public float MaxShake { get; set; } = 0.08f;

	private const string LogoPath = "res://art/logo.png";
	// The logo's own paper colour, so the picture sits seamlessly on the backdrop.
	private static readonly Color Parchment = new(0.976f, 0.91f, 0.804f);
	// The white flash melts into the parchment over this long, while the logo starts to appear.
	private const float FlashToParchment = 0.5f;
	private const float LogoDelay = 0.2f;
	private const float MessageFadeIn = 0.4f;

	private static readonly StringName StrengthParam = "strength";
	private static readonly StringName FlashParam = "flash";

	private enum Phase { Lobby, Playing, Done }

	private Session _session;
	private CanvasLayer _hud;
	private Node3D _players;
	private Camera3D _camera;
	private ColorRect _rupture;
	private ShaderMaterial _ruptureMaterial;
	private ColorRect _backdrop;
	private Control _logo;
	private Control _message;
	private Label _heading;
	private Label _subtitle;
	private Label _tip;
	private float _hold;
	private Node3D _localPlayer;
	private Transform3D _baseView;
	private Phase _phase = Phase.Lobby;
	private Vector3 _ownRoom;
	private Vector3 _otherRoom;
	// The glide round the room, in the room's own space (start and end of the camera and of where it looks).
	private static readonly Vector3 TourFrom = new(-2.4f, 2.2f, 4.3f);
	private static readonly Vector3 TourTo = new(2.0f, 1.7f, 1.6f);
	private static readonly Vector3 TourLookFrom = new(0f, 0.9f, -2.0f);
	private static readonly Vector3 TourLookTo = new(-0.6f, 0.9f, -3.4f);
	// Seconds since the intro started.
	private float _time;
	// Drives the sway; never reset, so the lobby sway flows straight into the intro.
	private float _clock;
	private bool _swapped;
	// Testing: `--skip-intro` drops each player straight into their room.
	private bool _skip;

	public override void _Ready()
	{
		_session = GetParent<Session>();
		_hud = GetNodeOrNull<CanvasLayer>("../Hud");
		_players = GetNodeOrNull<Node3D>("../Players");
		Build();
		GameState.Instance.GameStarted += OnGameStarted;
		_skip = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--skip-intro") >= 0;

		_baseView = new Transform3D(Basis.Identity, CameraPosition).LookingAt(LookTarget, Vector3.Up);
		_camera.GlobalTransform = _baseView;
		_camera.MakeCurrent();
		if (_hud != null)
			_hud.Visible = false;
	}

	public override void _ExitTree()
	{
		GameState.Instance.GameStarted -= OnGameStarted;
	}

	public override void _Process(double delta)
	{
		if (_phase == Phase.Done)
			return;
		float dt = (float)delta;
		_clock += dt;
		if (_phase == Phase.Lobby)
		{
			UpdateCamera(0f, 0f);
			return;
		}

		_time += dt;
		float t = _time;
		float peekStart = SwayDuration + RuptureDuration;
		float returnStart = peekStart + PeekDuration;
		float flashStart = returnStart + ReturnDuration;
		float logoStart = flashStart + FlashDuration;

		if (t < logoStart)
		{
			// The rupture builds to full, eases a little while the other era shows (rippling the whole time), swells
			// again as it pulls back, and the flash takes over.
			float rupture = Mathf.Clamp((t - SwayDuration) / RuptureDuration, 0f, 1f);
			if (t >= peekStart && t < returnStart)
				rupture = 1f - 0.4f * Mathf.Sin((t - peekStart) / PeekDuration * Mathf.Pi);
			// Back home, the ripples die down to a shimmer, then swell again into the flash.
			if (t >= returnStart)
			{
				float r = (t - returnStart) / ReturnDuration;
				rupture = Mathf.Lerp(1f, 0.2f, Smooth(r / 0.45f)) + 0.8f * Smooth((r - 0.75f) / 0.25f);
			}
			float flash = Mathf.Clamp((t - flashStart) / FlashDuration, 0f, 1f);
			bool peeking = t >= peekStart && t < returnStart;
			UpdateTourCamera(t, rupture, peeking ? _otherRoom : _ownRoom);
			_rupture.Visible = rupture > 0f;
			_ruptureMaterial.SetShaderParameter(StrengthParam, rupture);
			_ruptureMaterial.SetShaderParameter(FlashParam, flash);
			return;
		}

		// From here the screen is covered: first the white flash, then the parchment behind the logo.
		_rupture.Visible = false;
		float sinceLogo = t - logoStart;
		float logoEnd = LogoDelay + LogoFadeIn + LogoHold + LogoFadeOut;
		if (sinceLogo < logoEnd)
		{
			_backdrop.Color = Colors.White.Lerp(Parchment, Smooth(sinceLogo / FlashToParchment));
			UpdateLogo(sinceLogo - LogoDelay);
			return;
		}

		if (!_swapped)
			SwapToPlayer();

		float sinceReveal = sinceLogo - logoEnd;
		_backdrop.Color = Parchment with { A = 1f - Smooth(sinceReveal / RevealDuration) };

		float sinceMessage = sinceReveal - RevealDuration * 0.5f;
		float fadeIn = Mathf.Clamp(sinceMessage / MessageFadeIn, 0f, 1f);
		float fadeOut = 1f - Mathf.Clamp((sinceMessage - MessageFadeIn - _hold) / MessageFade, 0f, 1f);
		_message.Modulate = new Color(1f, 1f, 1f, Mathf.Min(fadeIn, fadeOut));
		if (sinceMessage >= MessageFadeIn + _hold + MessageFade)
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
		// The past player's body stands right in front of the intro camera.
		if (_players != null)
			_players.Visible = false;

		_camera.MakeCurrent();
		bool present = GameState.Instance.LocalEra == Era.Present;
		_ownRoom = present ? PresentRoomCentre : PastRoomCentre;
		_otherRoom = present ? PastRoomCentre : PresentRoomCentre;
		_heading.Text = present ? PresentTitle : PastTitle;
		_subtitle.Text = present ? PresentLine : PastLine;
		// The blank first line spaces it off from the subtitle.
		_tip.Text = "\n" + PresentTip;
		_tip.Visible = present && PresentTip != "";
		_hold = MessageHold + (_tip.Visible ? TipHold : 0f);
		_ruptureMaterial.SetShaderParameter(StrengthParam, 0f);
		_ruptureMaterial.SetShaderParameter(FlashParam, 0f);
		_backdrop.Color = Colors.Transparent;
		_backdrop.Visible = true;
		_logo.Modulate = Colors.Transparent;
		_logo.Visible = true;
		_message.Visible = false;
		_swapped = false;
		_time = 0f;
		_phase = Phase.Playing;
		// The game clock waits until both players are in their rooms.
		GameState.Instance.PauseTimer();
		if (_skip)
		{
			SwapToPlayer();
			Finish();
		}
	}

	// Gliding across the room (centred on room) with a gentle handheld sway, and the rupture's tremor. The glide
	// keeps going through the glimpse, so the other era is seen from the same spot.
	private void UpdateTourCamera(float t, float rupture, Vector3 room)
	{
		float total = SwayDuration + RuptureDuration + PeekDuration + ReturnDuration + FlashDuration;
		float k = Smooth(t / total);
		Vector3 position = room + TourFrom.Lerp(TourTo, k);
		Vector3 target = room + TourLookFrom.Lerp(TourLookTo, k);
		float yaw = Mathf.Sin(_clock * 0.6f) * 0.03f;
		float pitch = Mathf.Sin(_clock * 0.85f + 1f) * 0.018f;
		float roll = Mathf.Sin(_clock * 0.45f) * 0.012f;

		float shake = rupture * rupture * MaxShake;
		position += new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * shake;
		roll += (GD.Randf() - 0.5f) * shake * 0.6f;

		Basis look = Basis.LookingAt(target - position, Vector3.Up);
		_camera.GlobalTransform = new Transform3D(look * Basis.FromEuler(new Vector3(pitch, yaw, roll)), position);
	}

	private void UpdateCamera(float t, float rupture)
	{
		// Slow drift forward with a gentle handheld sway, then a tremor that builds with the rupture.
		Vector3 position = _baseView.Origin - _baseView.Basis.Z * DollySpeed * t;
		float yaw = Mathf.Sin(_clock * 0.6f) * 0.035f;
		float pitch = Mathf.Sin(_clock * 0.85f + 1f) * 0.02f;
		float roll = Mathf.Sin(_clock * 0.45f) * 0.015f;

		float shake = rupture * rupture * MaxShake;
		position += new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * shake;
		roll += (GD.Randf() - 0.5f) * shake * 0.6f;

		Basis basis = _baseView.Basis * Basis.FromEuler(new Vector3(pitch, yaw, roll));
		_camera.GlobalTransform = new Transform3D(basis, position);
	}

	// Fades in while settling from slightly small, holds, then fades out while growing a touch.
	private void UpdateLogo(float t)
	{
		float alpha;
		float scale;
		if (t < LogoFadeIn)
		{
			float k = Mathf.Clamp(t / LogoFadeIn, 0f, 1f);
			alpha = Smooth(k);
			scale = Mathf.Lerp(0.92f, 1f, 1f - Mathf.Pow(1f - k, 3f));
		}
		else if (t < LogoFadeIn + LogoHold)
		{
			alpha = 1f;
			scale = 1f + 0.01f * (t - LogoFadeIn) / LogoHold;
		}
		else
		{
			float k = Smooth((t - LogoFadeIn - LogoHold) / LogoFadeOut);
			alpha = 1f - k;
			scale = 1.01f + 0.03f * k;
		}
		_logo.Modulate = new Color(1f, 1f, 1f, alpha);
		_logo.Scale = new Vector2(scale, scale);
	}

	// Happens while the parchment covers the screen, so the cut is hidden.
	private void SwapToPlayer()
	{
		_swapped = true;
		_logo.Visible = false;

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
		if (_players != null)
			_players.Visible = true;
		_message.Modulate = Colors.Transparent;
		_message.Visible = true;
	}

	private void Finish()
	{
		if (_phase == Phase.Playing)
			GameState.Instance.ResumeTimer();
		_phase = Phase.Done;
		_message.Visible = false;
		_backdrop.Visible = false;
	}

	private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t, 0f, 1f));

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

		_backdrop = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(_backdrop);

		_logo = BuildLogo();
		_logo.Visible = false;
		overlay.AddChild(_logo);

		var box = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center,
			Visible = false,
		};
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_heading = MakeLabel("", 46);
		box.AddChild(_heading);
		_subtitle = MakeLabel("", 28);
		box.AddChild(_subtitle);
		_tip = MakeLabel("", 20);
		box.AddChild(_tip);
		overlay.AddChild(box);
		_message = box;
	}

	private Control BuildLogo()
	{
		var size = new Vector2(LogoSize, LogoSize);
		Control logo;
		if (ResourceLoader.Exists(LogoPath))
		{
			logo = new TextureRect
			{
				Texture = GD.Load<Texture2D>(LogoPath),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://net/logo_edge.gdshader") },
			};
		}
		else
		{
			// The art isn't on this computer yet: show the name instead.
			var label = MakeLabel("TOMB OF TIME", 96);
			label.VerticalAlignment = VerticalAlignment.Center;
			label.AddThemeColorOverride("font_color", new Color(0.24f, 0.16f, 0.08f));
			label.AddThemeConstantOverride("outline_size", 0);
			logo = label;
		}
		logo.MouseFilter = Control.MouseFilterEnum.Ignore;
		// Centred by hand (not with a container) so it can scale around its middle.
		logo.SetAnchorsPreset(Control.LayoutPreset.Center);
		logo.OffsetLeft = -size.X / 2f;
		logo.OffsetTop = -size.Y / 2f;
		logo.OffsetRight = size.X / 2f;
		logo.OffsetBottom = size.Y / 2f;
		logo.PivotOffset = size / 2f;
		return logo;
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
