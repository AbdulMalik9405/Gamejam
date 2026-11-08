using System.Collections.Generic;
using Gamejam.Core;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

/// <summary>
/// The past character's death, which is how the present room's ceiling got its hole and rubble pile. When Trigger is
/// solved (the tile placed on the crypt's altar), both players watch the past room: the Egyptian walks in as the ground
/// rumbles and dust trickles down, looks up, the ceiling above the spot that's open in the present breaks, and the
/// rubble buries him (he goes limp, a ragdoll). Then it cuts to the same room, where he's laid in the royal coffin on
/// the floor and its lid swings shut on him. The past player stays dead (a black screen), the present player gets
/// their view back, and only then does this count as solved on their machine (for whatever comes next).
/// </summary>
public partial class CeilingCollapse : Puzzle
{
	[Export] public Puzzle Trigger { get; set; }
	// The past map's root (level1), its ceiling, and the present map's ceiling pieces, which leave the hole. Copies of
	// those pieces, in the past's materials, replace the past ceiling for good when the cutscene starts.
	[Export] public Node3D PastLevel { get; set; }
	[Export] public MeshInstance3D PastCeiling { get; set; }
	[Export] public Array<MeshInstance3D> BrokenCeiling { get; set; } = new();
	// The hole, the Egyptian's feet and the camera, in PastLevel's space.
	[Export] public Vector3 HoleMin { get; set; } = new(2.7f, 4.0f, -0.8f);
	[Export] public Vector3 HoleMax { get; set; } = new(4.3f, 4.4f, 0.8f);
	[Export] public Vector3 VictimPosition { get; set; } = new(3.5f, 0f, 0.25f);
	// Where he walks in from (PastLevel's space), arriving under the hole just before it gives.
	[Export] public Vector3 WalkFrom { get; set; } = new(3.8f, 0f, -1.7f);
	[Export] public Vector3 CameraPosition { get; set; } = new(0.8f, 1.5f, 0.9f);
	[Export] public PackedScene VictimScene { get; set; }
	// The coffin he's laid in: the crypt's royal coffin (its mesh becomes the lid), lying on the floor at
	// CoffinPosition (PastLevel's space, turned by CoffinYawDegrees, head end towards +Z), and where the camera stands
	// in the coffin's own space.
	[Export] public MeshInstance3D RoyalCoffin { get; set; }
	[Export] public Vector3 CoffinPosition { get; set; } = new(-2.75f, 0f, 0f);
	[Export] public float CoffinYawDegrees { get; set; }
	[Export] public Vector3 CoffinCameraPosition { get; set; } = new(-1.1f, 1.45f, 0.55f);
	[Export] public string DeathTitle { get; set; } = "CRUSHED";
	[Export] public string DeathText { get; set; } = "The ceiling gave way and buried you.\nYour partner must escape alone.";

	// Seconds from the cut to the past room.
	private const float FadeTime = 0.5f;
	// He walks in (slowing as he hears the rumble), stops, and looks up as it breaks; the camera follows him and
	// tilts partway up to the ceiling with him.
	private const float WalkEnd = 1.85f;
	private const float LookUpStart = 1.3f;
	private const float LookUpEnd = 2.1f;
	private const float BreakTime = 2.3f;
	// He goes limp a moment before the slab reaches him: starting the ragdoll with a chunk already inside him would
	// fling him across the room.
	private const float FallTime = 2.7f;
	private const float RubbleEnd = 3.9f;
	private const float ShotEnd = 7.0f;
	// The coffin shot, from the cut: the lid starts to swing shut, takes this long, then the shot holds.
	private const float CoffinStart = ShotEnd + FadeTime;
	private const float LidDelay = 1.5f;
	private const float LidTime = 1.3f;
	private const float LidOpenDegrees = 105f;
	private const float CoffinEnd = CoffinStart + LidDelay + LidTime + 1.5f;
	// The trough is the royal coffin's outline measured at this many points along its length.
	private const int CoffinStations = 24;
	private const float CoffinWall = 0.035f;
	private const float CoffinWallHeight = 0.36f;
	// The lid is the royal coffin flattened to this much of its depth.
	private const float LidDepthScale = 0.45f;
	private const int PlugCells = 3;
	private const int RubbleCount = 26;
	private const int PebbleCount = 14;

	private bool _playing;
	// Trigger was solved on this machine (the present player's), so the passage reward waits on this one.
	private bool _solvedHere;
	private float _time = -1f;
	private Player _localPlayer;
	private bool _localIsPast;
	private CanvasLayer _hud;
	private Camera3D _camera;
	private ColorRect _fade;
	private Label _deathTitle;
	private Label _deathText;
	private Node3D _victimPivot;
	private CharacterModel _victim;
	private Node3D _debris;
	private readonly List<RigidBody3D> _plug = new();
	private readonly List<(float Time, RigidBody3D Body)> _pending = new();
	private CpuParticles3D _trickle;
	private CpuParticles3D _burst;
	private Material _starsMaterial;
	private Material _stoneMaterial;
	private bool _broke, _fell, _cutBack, _finished, _inCoffin, _slammed;
	private Node3D _coffin;
	private Node3D _lidHinge;
	private CpuParticles3D _slamDust;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		if (Trigger != null)
			Trigger.Solved += OnTriggerSolved;
		VictimScene ??= GD.Load<PackedScene>("res://art/egyptian.glb");
	}

	public override void _ExitTree()
	{
		if (Trigger != null && IsInstanceValid(Trigger))
			Trigger.Solved -= OnTriggerSolved;
		// Never leave the present player stuck if this goes away early.
		if (_playing && !_cutBack && !_localIsPast && IsInstanceValid(_localPlayer))
			_localPlayer.InputLocked = false;
	}

	private void OnTriggerSolved()
	{
		_solvedHere = true;
		// Pickups are local, so only the present player's machine knows the keys are in. Tell everyone.
		Rpc(MethodName.Play);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Play()
	{
		if (_playing || PastLevel == null)
			return;
		_playing = true;
		_rng.Seed = 1234;
		_localPlayer = GameState.Instance?.GetPlayerNode(Multiplayer.GetUniqueId()) as Player;
		_localIsPast = GameState.Instance != null && GameState.Instance.LocalEra == Era.Past
			&& GameState.Instance.Players.ContainsKey(Multiplayer.GetUniqueId());
		if (_localPlayer != null)
			_localPlayer.InputLocked = true;
		_hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		BuildOverlay();
		// Fade to black, then cut to the past room.
		_time = -FadeTime;
	}

	public override void _Process(double delta)
	{
		if (!_playing || _finished)
			return;
		float dt = (float)delta;
		float before = _time;
		_time += dt;
		float t = _time;

		if (before < 0f && t >= 0f)
			CutToPast();
		if (t < 0f)
		{
			_fade.Color = new Color(0f, 0f, 0f, Mathf.Clamp(1f + t / FadeTime, 0f, 1f));
			return;
		}

		// Each shot fades in after its cut and out at its end.
		float fade = t < CoffinStart
			? Mathf.Max(1f - Mathf.Clamp(t / FadeTime, 0f, 1f), Mathf.Clamp((t - ShotEnd) / FadeTime, 0f, 1f))
			: Mathf.Max(1f - Mathf.Clamp((t - CoffinStart) / FadeTime, 0f, 1f), Mathf.Clamp((t - CoffinEnd) / FadeTime, 0f, 1f));
		_fade.Color = new Color(0f, 0f, 0f, fade);

		if (!_inCoffin)
		{
			AnimateVictim(t);
			AnimateCamera(t);
		}
		ReleaseRubble(t);
		if (!_inCoffin && t >= CoffinStart)
			CutToCoffin();
		if (_inCoffin && !_cutBack)
			AnimateCoffin(t - CoffinStart);

		if (!_broke && t >= BreakTime)
			Break();
		if (!_fell && t >= FallTime)
			Fall();

		if (t >= CoffinEnd + FadeTime && !_cutBack)
			CutBack();
		if (_localIsPast && _cutBack)
		{
			// The past player is dead: their screen stays black with a few words.
			float k = Mathf.Clamp((t - CoffinEnd - FadeTime - 0.3f) / 1.2f, 0f, 1f);
			_deathTitle.Modulate = new Color(1f, 1f, 1f, k);
			_deathText.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(k * 1.5f - 0.5f, 0f, 1f));
		}
		else if (_cutBack && t >= CoffinEnd + FadeTime * 2f)
			Finish();
	}

	private Vector3 Local(Vector3 p) => PastLevel.ToGlobal(p);
	private Vector3 HoleCenter => (HoleMin + HoleMax) * 0.5f;

	private void BuildOverlay()
	{
		var overlay = new CanvasLayer { Name = "CollapseOverlay", Layer = 20 };
		AddChild(overlay);
		_fade = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
		_fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(_fade);

		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		box.AddThemeConstantOverride("separation", 18);
		overlay.AddChild(box);
		_deathTitle = new Label { Text = DeathTitle, HorizontalAlignment = HorizontalAlignment.Center, Modulate = Colors.Transparent };
		_deathTitle.AddThemeFontSizeOverride("font_size", 64);
		_deathTitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.12f, 0.08f));
		box.AddChild(_deathTitle);
		_deathText = new Label { Text = DeathText, HorizontalAlignment = HorizontalAlignment.Center, Modulate = Colors.Transparent };
		_deathText.AddThemeFontSizeOverride("font_size", 24);
		_deathText.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.7f));
		box.AddChild(_deathText);
	}

	private void CutToPast()
	{
		if (_hud != null)
			_hud.Visible = false;
		HidePastPlayers();
		BreakCeilingForGood();

		_camera = new Camera3D { Name = "CollapseCamera", Fov = 62f };
		AddChild(_camera);
		_camera.GlobalPosition = Local(CameraPosition);
		_camera.MakeCurrent();

		_debris = new Node3D { Name = "CollapseDebris" };
		PastLevel.AddChild(_debris);
		BuildVictim();
		BuildPlug();
		BuildRubble();
		BuildDust();
		AnimateCamera(0f);
	}

	// The real past player's body is replaced by the cutscene's Egyptian, here and on the other machine.
	private void HidePastPlayers()
	{
		if (GameState.Instance == null)
			return;
		foreach (KeyValuePair<long, PlayerInfo> entry in GameState.Instance.Players)
		{
			if (entry.Value.Era != Era.Past || GameState.Instance.GetPlayerNode(entry.Key) is not Player player)
				continue;
			player.Visible = false;
			player.Inventory?.SelectedItem?.SetDeferred(Node3D.PropertyName.Visible, false);
			// Dead: no more moving, picking up or showing items.
			if (player.IsMultiplayerAuthority())
				player.ProcessMode = ProcessModeEnum.Disabled;
		}
	}

	// Swaps the whole past ceiling for copies of the present's pieces, which leave the hole.
	private void BreakCeilingForGood()
	{
		if (PastCeiling == null)
			return;
		for (int i = 0; i < PastCeiling.Mesh.GetSurfaceCount(); i++)
		{
			Material material = PastCeiling.GetActiveMaterial(i);
			string name = material?.ResourceName ?? "";
			if (name.Contains("Ceiling"))
				_starsMaterial = material;
			else if (name.Contains("Stone"))
				_stoneMaterial = material;
		}
		foreach (MeshInstance3D piece in BrokenCeiling)
		{
			if (piece?.Mesh == null)
				continue;
			var copy = new MeshInstance3D { Name = "Past" + piece.Name, Mesh = piece.Mesh, Transform = piece.Transform };
			for (int i = 0; i < piece.Mesh.GetSurfaceCount(); i++)
			{
				string name = piece.GetActiveMaterial(i)?.ResourceName ?? "";
				copy.SetSurfaceOverrideMaterial(i, name.Contains("Ceiling") ? _starsMaterial : _stoneMaterial);
			}
			PastLevel.AddChild(copy);
		}
		PastCeiling.Visible = false;
	}

	private void BuildVictim()
	{
		_victimPivot = new Node3D { Name = "Victim" };
		_debris.AddChild(_victimPivot);
		_victimPivot.GlobalPosition = Local(WalkFrom);
		Vector3 heading = Local(VictimPosition) - Local(WalkFrom);
		heading.Y = 0f;
		_victimPivot.LookAt(_victimPivot.GlobalPosition + heading, Vector3.Up);
		// Animate on: the walk cycle follows his movement, and he idles (breathing, shifting his weight) once he stops.
		// A slow, short-stepped walk.
		_victim = new CharacterModel { Name = "Egyptian", Scene = VictimScene, Animate = true, WalkSpeed = 1.3f, StrideLength = 0.9f };
		_victimPivot.AddChild(_victim);
	}

	// The slab that fills the hole until it breaks: a grid of chunks, starry underneath like the rest of the ceiling.
	private void BuildPlug()
	{
		Vector3 size = HoleMax - HoleMin;
		float cellX = size.X / PlugCells;
		float cellZ = size.Z / PlugCells;
		for (int x = 0; x < PlugCells; x++)
		{
			for (int z = 0; z < PlugCells; z++)
			{
				var center = new Vector3(HoleMin.X + cellX * (x + 0.5f), (HoleMin.Y + HoleMax.Y) * 0.5f, HoleMin.Z + cellZ * (z + 0.5f));
				var chunkSize = new Vector3(cellX - 0.006f, size.Y, cellZ - 0.006f);
				RigidBody3D chunk = MakeStone(chunkSize, center);
				chunk.Freeze = true;
				chunk.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
				// The underside: the ceiling's stars, mapped the way the ceiling maps them (u = x / 2, v = z / 2 + 1 in
				// the level's space), so the slab looks like part of it until it falls.
				var stars = (_starsMaterial as BaseMaterial3D)?.Duplicate() as BaseMaterial3D;
				if (stars != null)
				{
					stars.Uv1Triplanar = true;
					stars.Uv1Scale = new Vector3(0.5f, 0.5f, -0.5f);
					stars.Uv1Offset = new Vector3(center.X * 0.5f, 0f, center.Z * 0.5f + 1f);
				}
				chunk.AddChild(new MeshInstance3D
				{
					Mesh = new PlaneMesh { Size = new Vector2(chunkSize.X, chunkSize.Z), Material = stars },
					Position = new Vector3(0f, -size.Y * 0.5f - 0.002f, 0f),
					RotationDegrees = new Vector3(180f, 0f, 0f),
				});
				_plug.Add(chunk);
			}
		}
	}

	// Stones that fall after the slab, and pebbles that trickle down before it goes.
	private void BuildRubble()
	{
		for (int i = 0; i < PebbleCount; i++)
		{
			float s = _rng.RandfRange(0.03f, 0.07f);
			Vector3 at = new(_rng.RandfRange(HoleMin.X + 0.2f, HoleMax.X - 0.2f), HoleMin.Y - 0.05f, _rng.RandfRange(HoleMin.Z + 0.2f, HoleMax.Z - 0.2f));
			_pending.Add((_rng.RandfRange(0.9f, BreakTime), MakeStone(new Vector3(s, s * 0.8f, s * 1.1f), at, hidden: true)));
		}
		for (int i = 0; i < RubbleCount; i++)
		{
			float s = _rng.RandfRange(0.12f, 0.38f);
			Vector3 at = new(_rng.RandfRange(HoleMin.X, HoleMax.X), HoleMax.Y + _rng.RandfRange(0.1f, 1.2f), _rng.RandfRange(HoleMin.Z, HoleMax.Z));
			_pending.Add((_rng.RandfRange(BreakTime + 0.05f, RubbleEnd), MakeStone(new Vector3(s, s * _rng.RandfRange(0.5f, 0.9f), s * _rng.RandfRange(0.7f, 1.2f)), at, hidden: true)));
		}
	}

	// A block of the ceiling's stone at a point in PastLevel's space. Hidden ones wait, frozen, until released, and
	// start at a random angle so the rubble doesn't land in neat rows.
	private RigidBody3D MakeStone(Vector3 size, Vector3 localCenter, bool hidden = false)
	{
		var body = new RigidBody3D
		{
			CollisionLayer = Layers.World,
			CollisionMask = Layers.World,
			Mass = Mathf.Max(1f, size.X * size.Y * size.Z * 2400f),
			Freeze = hidden,
			FreezeMode = RigidBody3D.FreezeModeEnum.Static,
			Visible = !hidden,
		};
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = _stoneMaterial } });
		_debris.AddChild(body);
		body.GlobalPosition = Local(localCenter);
		if (hidden)
			body.Rotation = new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-3f, 3f), _rng.RandfRange(-0.4f, 0.4f));
		return body;
	}

	private void BuildDust()
	{
		// A soft round puff, faded at the edges.
		var puffShape = new Gradient
		{
			Offsets = new[] { 0f, 1f },
			Colors = new[] { Colors.White, new Color(1f, 1f, 1f, 0f) },
		};
		var puff = new GradientTexture2D
		{
			Gradient = puffShape,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(0.5f, 0f),
			Width = 64,
			Height = 64,
		};
		var dust = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			// Plain billboards that keep the particle's scale: in Particles mode, particles waiting to be emitted (scale 0)
			// still showed as a solid black puff at the emitter.
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			VertexColorUseAsAlbedo = true,
			AlbedoColor = Colors.White,
			AlbedoTexture = puff,
		};
		// Fades in, then out over each puff's life.
		var dustColor = new Color(0.42f, 0.37f, 0.3f);
		var fade = new Gradient
		{
			Offsets = new[] { 0f, 0.2f, 1f },
			Colors = new[] { dustColor with { A = 0f }, dustColor with { A = 0.28f }, dustColor with { A = 0f } },
		};

		Vector3 size = HoleMax - HoleMin;
		_trickle = new CpuParticles3D
		{
			Name = "DustTrickle",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Amount = 70,
			Lifetime = 2.6f,
			Mesh = new QuadMesh { Size = new Vector2(0.25f, 0.25f), Material = dust },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(size.X * 0.5f, 0.02f, size.Z * 0.5f),
			Direction = Vector3.Down,
			Spread = 12f,
			InitialVelocityMin = 0.2f,
			InitialVelocityMax = 0.6f,
			Gravity = new Vector3(0f, -1.6f, 0f),
			ScaleAmountMin = 0.4f,
			ScaleAmountMax = 1.6f,
			ColorRamp = fade,
		};

		_burst = new CpuParticles3D
		{
			Name = "DustBurst",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			OneShot = true,
			Explosiveness = 0.85f,
			Amount = 50,
			Lifetime = 3.0f,
			Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f), Material = dust },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
			EmissionRingAxis = Vector3.Up,
			EmissionRingRadius = 0.9f,
			EmissionRingInnerRadius = 0.5f,
			EmissionRingHeight = 0.1f,
			Direction = Vector3.Up,
			Spread = 70f,
			InitialVelocityMin = 0.5f,
			InitialVelocityMax = 1.6f,
			Gravity = new Vector3(0f, -0.25f, 0f),
			DampingMin = 1.2f,
			DampingMax = 2.0f,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.8f,
			ColorRamp = fade,
		};
	}

	// Particles are only added when they start, where they should be.
	private void StartDust(CpuParticles3D dust, Vector3 localPosition)
	{
		_debris.AddChild(dust);
		dust.GlobalPosition = Local(localPosition);
	}

	private void AnimateVictim(float t)
	{
		if (_victim == null)
			return;
		// He walks in, slowing to a stop as the rumble grows, looks up, then throws his arms up as it gives.
		if (!_fell)
		{
			float k = Mathf.Clamp(t / WalkEnd, 0f, 1f);
			_victimPivot.GlobalPosition = Local(WalkFrom.Lerp(VictimPosition, 1f - (1f - k) * (1f - k)));
		}
		float look = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - LookUpStart) / (LookUpEnd - LookUpStart), 0f, 1f));
		_victim.HeadPitchDegrees = 40f * look * (_fell ? 0.3f : 1f);
		float shield = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - (BreakTime - 0.4f)) / 0.45f, 0f, 1f));
		_victim.Shield = _fell ? Mathf.Max(0f, shield * (1f - (t - FallTime) * 2f)) : shield;
	}

	private void AnimateCamera(float t)
	{
		// His chest as he walks in; once he's down, where he stood.
		Vector3 chest = (_victimPivot != null && !_fell ? _victimPivot.GlobalPosition : Local(VictimPosition)) + PastLevel.GlobalBasis * new Vector3(0f, 1.3f, 0f);
		Vector3 ceiling = Local(new Vector3(HoleCenter.X, HoleMin.Y, HoleCenter.Z));
		Vector3 ground = Local(VictimPosition + new Vector3(0f, 0.35f, -0.8f));
		// Up to the cracking ceiling, then follows the slab down onto him.
		float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - LookUpStart) / (LookUpEnd - LookUpStart), 0f, 1f));
		float down = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - BreakTime - 0.1f) / 0.7f, 0f, 1f));
		Vector3 target = chest.Lerp(ceiling, up * 0.42f).Lerp(ground, down);
		// A slow push-in.
		Vector3 from = Local(CameraPosition);
		Vector3 to = Local(CameraPosition.Lerp(VictimPosition + new Vector3(0f, 1.45f, 0f), 0.22f));
		_camera.GlobalPosition = from.Lerp(to, Mathf.SmoothStep(0f, 1f, t / ShotEnd));
		_camera.LookAt(target, Vector3.Up);

		float shake = t < BreakTime
			? Mathf.Lerp(0.004f, 0.02f, Mathf.Clamp(t / BreakTime, 0f, 1f))
			: Mathf.Lerp(0.07f, 0.0f, Mathf.Clamp((t - BreakTime) / 3.2f, 0f, 1f));
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		// Starting a little below the ceiling keeps the puffs from cutting into it.
		if (_trickle != null && !_trickle.IsInsideTree() && t >= 0.4f)
			StartDust(_trickle, new Vector3(HoleCenter.X, HoleMin.Y - 0.25f, HoleCenter.Z));
	}

	private void ReleaseRubble(float t)
	{
		for (int i = _pending.Count - 1; i >= 0; i--)
		{
			if (t < _pending[i].Time)
				continue;
			RigidBody3D stone = _pending[i].Body;
			stone.Visible = true;
			stone.Freeze = false;
			stone.AngularVelocity = new Vector3(_rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f));
			_pending.RemoveAt(i);
		}
	}

	private void Break()
	{
		_broke = true;
		foreach (RigidBody3D chunk in _plug)
		{
			chunk.Freeze = false;
			chunk.LinearVelocity = new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-1.5f, -0.5f), _rng.RandfRange(-0.4f, 0.4f));
			chunk.AngularVelocity = new Vector3(_rng.RandfRange(-2.5f, 2.5f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-2.5f, 2.5f));
		}
		Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "ceiling_collapse", Local(HoleCenter));
	}

	// Struck down: he goes limp and is knocked down towards the middle of the hole, under where the rubble pile will be,
	// and it lands on him.
	private void Fall()
	{
		_fell = true;
		StartDust(_burst, VictimPosition + new Vector3(0f, 0.15f, -0.8f));
		Vector3 toHole = Local(new Vector3(HoleCenter.X, 0f, HoleCenter.Z)) - _victimPivot.GlobalPosition;
		toHole.Y = 0f;
		_victim.StartRagdoll(toHole.Normalized() * 12f + Vector3.Down * 10f);
	}

	// The same room, later: the royal coffin lies open on the floor with him inside, its lid standing up on one side.
	private void CutToCoffin()
	{
		_inCoffin = true;
		if (_trickle.IsInsideTree())
			_trickle.Emitting = false;
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;

		_coffin = new Node3D { Name = "Coffin" };
		_debris.AddChild(_coffin);
		_coffin.GlobalTransform = new Transform3D(PastLevel.GlobalBasis * new Basis(Vector3.Up, Mathf.DegToRad(CoffinYawDegrees)), Local(CoffinPosition));

		Material wood = RoyalCoffin?.GetActiveMaterial(0) ?? _stoneMaterial;
		Material gold = RoyalCoffin != null && RoyalCoffin.Mesh.GetSurfaceCount() > 1 ? RoyalCoffin.GetActiveMaterial(1) : wood;
		Aabb box = RoyalCoffin?.GetAabb() ?? new Aabb(new Vector3(-0.33f, 0f, -0.2f), new Vector3(0.66f, 2.1f, 0.4f));
		float[] halfWidths = CoffinProfile(RoyalCoffin?.Mesh, box);
		float halfLength = box.Size.Y * 0.5f;
		float widest = 0f;
		foreach (float w in halfWidths)
			widest = Mathf.Max(widest, w);

		// The open coffin: a body-shaped trough following the royal coffin's outline, gold outside, wood inside.
		_coffin.AddChild(new MeshInstance3D { Name = "Base", Mesh = CoffinShell(halfWidths, halfLength, gold, wood) });

		// Him, on his back, feet towards -Z (the coffin's foot end), face up, arms at his sides.
		var body = new CharacterModel { Name = "Entombed", Scene = VictimScene, Animate = false, ArmDownDegrees = 46f, ElbowBendDegrees = 6f };
		_coffin.AddChild(body);
		body.Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(90f)), new Vector3(0f, 0.16f, -halfLength + 0.16f));

		// The lid is the royal coffin itself, flattened, so its outline is the trough's. It hinges on the trough's
		// widest +X edge and lies face up, head end towards +Z, its back on the walls when shut.
		_lidHinge = new Node3D { Name = "LidHinge", Position = new Vector3(widest, CoffinWallHeight, 0f) };
		_coffin.AddChild(_lidHinge);
		if (RoyalCoffin?.Mesh != null)
		{
			var lid = new MeshInstance3D { Name = "Lid", Mesh = RoyalCoffin.Mesh };
			for (int i = 0; i < RoyalCoffin.Mesh.GetSurfaceCount(); i++)
				lid.SetSurfaceOverrideMaterial(i, RoyalCoffin.GetActiveMaterial(i));
			var flat = new Basis(Vector3.Right, Mathf.DegToRad(90f)) * Basis.FromScale(new Vector3(1f, 1f, LidDepthScale));
			lid.Transform = new Transform3D(flat, new Vector3(-widest - box.GetCenter().X, box.End.Z * LidDepthScale, -box.GetCenter().Y));
			_lidHinge.AddChild(lid);
		}
		_lidHinge.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(LidOpenDegrees));

		_slamDust = (CpuParticles3D)_burst.Duplicate();
		_slamDust.EmissionRingRadius = 0.7f;
		_slamDust.EmissionRingInnerRadius = 0.3f;
		_slamDust.Amount = 30;
		AnimateCoffin(0f);
	}

	// How wide the coffin is (half, from its centre line) at evenly spaced points from its foot (mesh Y min) to its head.
	private static float[] CoffinProfile(Mesh mesh, Aabb box)
	{
		var half = new float[CoffinStations];
		if (mesh == null)
		{
			System.Array.Fill(half, box.Size.X * 0.5f);
			return half;
		}
		float step = box.Size.Y / (CoffinStations - 1);
		float centre = box.GetCenter().X;
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			foreach (Vector3 v in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				int i = Mathf.Clamp(Mathf.RoundToInt((v.Y - box.Position.Y) / step), 0, CoffinStations - 1);
				half[i] = Mathf.Max(half[i], Mathf.Abs(v.X - centre));
			}
		}
		// Stations no vertex fell near take their neighbours' width.
		for (int i = 1; i < CoffinStations; i++)
			half[i] = half[i] > 0f ? half[i] : half[i - 1];
		for (int i = CoffinStations - 2; i >= 0; i--)
			half[i] = half[i] > 0f ? half[i] : half[i + 1];
		for (int i = 0; i < CoffinStations; i++)
			half[i] = Mathf.Max(half[i], CoffinWall * 3f);
		return half;
	}

	// An open trough following the outline (feet at -Z, head at +Z): outer wall and rim in gold, inner wall and floor
	// in wood.
	private static ArrayMesh CoffinShell(float[] halfWidths, float halfLength, Material outside, Material inside)
	{
		int n = halfWidths.Length;
		float h = CoffinWallHeight;
		float floor = 0.03f;
		// The outline loop: up the +X side from the foot, back down the -X side. Inner loop inset by the wall.
		var outer = new Vector3[n * 2];
		var inner = new Vector3[n * 2];
		for (int i = 0; i < n; i++)
		{
			float z = -halfLength + 2f * halfLength * i / (n - 1);
			float zIn = i == 0 ? z + CoffinWall : i == n - 1 ? z - CoffinWall : z;
			float w = halfWidths[i];
			outer[i] = new Vector3(w, 0f, z);
			outer[2 * n - 1 - i] = new Vector3(-w, 0f, z);
			inner[i] = new Vector3(w - CoffinWall, 0f, zIn);
			inner[2 * n - 1 - i] = new Vector3(-(w - CoffinWall), 0f, zIn);
		}

		var gold = new SurfaceTool();
		gold.Begin(Mesh.PrimitiveType.Triangles);
		var wood = new SurfaceTool();
		wood.Begin(Mesh.PrimitiveType.Triangles);
		Vector3 up = Vector3.Up * h;
		for (int i = 0; i < outer.Length; i++)
		{
			int j = (i + 1) % outer.Length;
			Vector3 along = outer[j] - outer[i];
			Vector3 outward = new Vector3(along.Z, 0f, -along.X).Normalized();
			if (outward.Dot(outer[i] + outer[j]) < 0f)
				outward = -outward;
			Quad(gold, outer[i], outer[j], outer[j] + up, outer[i] + up, outward);
			Quad(gold, outer[i] + up, outer[j] + up, inner[j] + up, inner[i] + up, Vector3.Up);
			Quad(wood, inner[i] + Vector3.Up * floor, inner[j] + Vector3.Up * floor, inner[j] + up, inner[i] + up, -outward);
		}
		// The floor, strip by strip across the inner outline.
		for (int i = 0; i < n - 1; i++)
		{
			Vector3 lift = Vector3.Up * floor;
			Quad(wood, inner[i] + lift, inner[i + 1] + lift, inner[2 * n - 2 - i] + lift, inner[2 * n - 1 - i] + lift, Vector3.Up);
		}

		var mesh = new ArrayMesh();
		gold.SetMaterial(outside);
		gold.Commit(mesh);
		wood.SetMaterial(inside);
		wood.Commit(mesh);
		return mesh;
	}

	// Two triangles facing `normal` (Godot's front faces wind clockwise, seen from the front).
	private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
	{
		Tri(tool, a, b, c, normal);
		Tri(tool, a, c, d, normal);
	}

	private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
	{
		if ((b - a).Cross(c - a).Dot(normal) > 0f)
			(b, c) = (c, b);
		foreach (Vector3 v in new[] { a, b, c })
		{
			tool.SetNormal(normal);
			tool.SetUV(new Vector2(v.X + v.Y, v.Z));
			tool.AddVertex(v);
		}
	}

	private void AnimateCoffin(float c)
	{
		// The lid falls shut faster and faster, lands with a thump and settles with a small bounce.
		float k = Mathf.Clamp((c - LidDelay) / LidTime, 0f, 1f);
		float angle = LidOpenDegrees * (1f - k * k);
		float after = c - LidDelay - LidTime;
		if (after > 0f)
			angle = 2.5f * Mathf.Abs(Mathf.Sin(after * 14f)) * Mathf.Exp(-after * 9f);
		_lidHinge.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(angle));
		if (!_slammed && k >= 1f)
		{
			_slammed = true;
			_debris.AddChild(_slamDust);
			_slamDust.GlobalPosition = _coffin.GlobalPosition + Vector3.Up * 0.1f;
			_slamDust.Emitting = true;
			Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "coffin_close", _coffin.GlobalPosition);
		}

		// Looking down into the coffin from beside it, drifting in slowly.
		Vector3 from = _coffin.ToGlobal(CoffinCameraPosition);
		Vector3 to = _coffin.ToGlobal(CoffinCameraPosition * 0.85f + new Vector3(0f, 0f, 0.05f));
		_camera.GlobalPosition = from.Lerp(to, Mathf.SmoothStep(0f, 1f, c / (CoffinEnd - CoffinStart)));
		_camera.LookAt(_coffin.ToGlobal(new Vector3(0f, 0.2f, 0.25f)), Vector3.Up);
		float shake = after > 0f ? Mathf.Lerp(0.035f, 0f, Mathf.Clamp(after / 0.8f, 0f, 1f)) : 0f;
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
	}

	private void CutBack()
	{
		_cutBack = true;
		if (_localIsPast)
		{
			// Dead. The black screen and the words stay.
			_fade.Color = Colors.Black;
			return;
		}
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
		Camera3D playerCamera = _localPlayer?.GetNodeOrNull<Camera3D>("Camera");
		playerCamera?.MakeCurrent();
		if (_hud != null && IsInstanceValid(_hud))
			_hud.Visible = true;
		if (_trickle.IsInsideTree())
			_trickle.Emitting = false;
	}

	private void Finish()
	{
		_finished = true;
		_fade.Color = Colors.Transparent;
		if (IsInstanceValid(_localPlayer))
			_localPlayer.InputLocked = false;
		// The stones stay where they fell, but stop being simulated.
		foreach (Node node in _debris.FindChildren("*", "RigidBody3D", true, false))
			((RigidBody3D)node).Freeze = true;
		if (_solvedHere)
		{
			IsSolved = true;
			EmitSignal(SignalName.Solved);
		}
	}
}
