using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// How the game ends, for both players at once. Lost (the timer runs out, or a player dies in the spike trap) or won
// (a player gets halfway up the exit stair): the screen fades to black, both players appear on the two floor circles of
// art/spawn_room.glb (a room of its own, away from both maps), red and hanging their heads in shame when lost, blue and
// dancing when won, seen from in front. After ScreenTime (20 s) it fades out and both go back to the menu.
// The music for it is played by audio/Music.cs on EndingStarted. Anything may call RequestEnd; it's sent to both.
public partial class GameEnding : Node
{
	[Export] public PackedScene RoomScene { get; set; }
	// Far from both maps (past at the origin, present at +200 m X), so neither sees it or its lights.
	[Export] public Vector3 RoomPosition { get; set; } = new Vector3(-200f, 0f, 0f);
	[Export] public float ScreenTime { get; set; } = 20f;
	[Export] public float FadeTime { get; set; } = 0.6f;
	// The win line: a box across the exit stair's middle step, in the present map's (level1old) space.
	// Past the boulder at the top of the exit ramp (gameplay/BoulderRun.cs), out on the landing (in the present map's
	// space): the game is won once every player is in it at the same time.
	[Export] public Vector3 WinZoneCenter { get; set; } = new Vector3(-30.05f, 9.2f, 16.3f);
	[Export] public Vector3 WinZoneSize { get; set; } = new Vector3(1.3f, 3.6f, 4.2f);
	[Export] public string WaitForPartnerMessage { get; set; } = "Wait for your partner at the exit.";
	// Invisible walls round the landing, so nobody walks off into the desert (in the present map's space).
	[Export] public Vector3 FenceMin { get; set; } = new Vector3(-30.7f, 6.4f, 14.1f);
	[Export] public Vector3 FenceMax { get; set; } = new Vector3(-27.9f, 13f, 18.5f);
	[Export] public Color LoseColor { get; set; } = new Color(1f, 0.12f, 0.08f);
	[Export] public Color WinColor { get; set; } = new Color(0.15f, 0.45f, 1f);
	// The ending camera, in the room's space: in front of the circles, looking back at both players.
	[Export] public Vector3 CameraPosition { get; set; } = new Vector3(0f, 1.5f, -3.1f);
	[Export] public Vector3 CameraTarget { get; set; } = new Vector3(0f, 1.0f, 0.2f);
	// Shown in big letters above the players.
	[Export] public string WinTitle { get; set; } = "Escaped";
	[Export] public string LoseTitle { get; set; } = "Died";

	// won, and how long the screen lasts (fade in to the end of the fade out), on every machine.
	[Signal] public delegate void EndingStartedEventHandler(bool won, float duration);

	public bool HasEnded { get; private set; }

	private Node3D _room;
	// Host: the players standing in the win zone right now.
	private readonly System.Collections.Generic.HashSet<long> _atExit = new();
	// The lose screen's little scene: both drop to their knees, then the past character gets up and lays into the
	// explorer, who cowers.
	private CharacterModel _loser;
	private CharacterModel _attacker;
	private Vector3 _attackerStart;
	private Vector3 _attackFrom;
	private float _loseTime = -1f;
	private const float KneelAt = 5f;
	private const float KneelTime = 0.6f;
	private const float RiseAt = 6.6f;
	private const float RiseTime = 0.7f;
	private const float StompTime = 1.3f;
	private const float BeatAt = RiseAt + RiseTime + StompTime + 0.2f;

	public override void _Ready()
	{
		AddToGroup("game_ending");
		RoomScene ??= GD.Load<PackedScene>("res://art/spawn_room.glb");
		_room = RoomScene.Instantiate<Node3D>();
		_room.Name = "EndingRoom";
		AddChild(_room);
		_room.Position = RoomPosition;
		// Hidden (lights and all) until it's needed.
		_room.Visible = false;
		TuneLights();
		Callable.From(AddWinZone).CallDeferred();
	}

	// Lights from Blender come in thousands of times too bright with a 4096 m range.
	private void TuneLights()
	{
		foreach (Node node in _room.FindChildren("*", "OmniLight3D", true, false))
		{
			var light = (OmniLight3D)node;
			string name = light.Name;
			if (name.Contains("Brazier"))
				(light.LightEnergy, light.OmniRange) = (1.3f, 7f);
			else if (name.Contains("Fill"))
				(light.LightEnergy, light.OmniRange) = (0.5f, 9f);
			else
				(light.LightEnergy, light.OmniRange) = (1.2f, 3f);
			light.OmniAttenuation = 1.3f;
		}
	}

	private void AddWinZone()
	{
		Node3D present = null;
		foreach (Node node in GetTree().GetNodesInGroup("era_present"))
			present ??= node as Node3D;
		if (present == null)
		{
			GD.PushWarning($"{Name}: no node in group era_present, so there's no win zone.");
			return;
		}
		var zone = new Area3D { Name = "WinZone", CollisionLayer = 0, CollisionMask = Layers.Players, Monitorable = false };
		zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = WinZoneSize } });
		present.AddChild(zone);
		zone.Position = WinZoneCenter;
		// Each machine only reports its own player; the host ends the game once everyone's there.
		zone.BodyEntered += body =>
		{
			if (body is not Player player || !player.IsMultiplayerAuthority())
				return;
			ReportAtExit(true);
			if (GameState.Instance != null && GameState.Instance.Players.Count > 1 && WaitForPartnerMessage != "")
				Events.Instance?.EmitSignal(Events.SignalName.ShowMessage, WaitForPartnerMessage, 4f);
		};
		zone.BodyExited += body =>
		{
			if (body is Player player && player.IsMultiplayerAuthority())
				ReportAtExit(false);
		};
		AddFence(present);
	}

	private void ReportAtExit(bool there)
	{
		// Leaving the zone as the game shuts down (after it's won) doesn't count.
		if (HasEnded || !IsInsideTree() || Multiplayer?.MultiplayerPeer == null)
			return;
		if (Multiplayer.IsServer())
			SetAtExit(Multiplayer.GetUniqueId(), there);
		else
			RpcId(1, MethodName.SendAtExit, there);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SendAtExit(bool there)
	{
		if (Multiplayer.IsServer())
			SetAtExit(Multiplayer.GetRemoteSenderId(), there);
	}

	// Host only.
	private void SetAtExit(long peer, bool there)
	{
		if (there)
			_atExit.Add(peer);
		else
			_atExit.Remove(peer);
		int needed = GameState.Instance?.Players.Count ?? 1;
		if (_atExit.Count >= Mathf.Max(needed, 1))
			RequestEnd(true);
	}

	// Four walls nobody can see or get past, round the landing at the top of the exit stair (the stair side open).
	private void AddFence(Node3D present)
	{
		var fence = new StaticBody3D { Name = "ExitFence", CollisionLayer = Layers.World, CollisionMask = 0 };
		present.AddChild(fence);
		Vector3 min = FenceMin, max = FenceMax;
		Vector3 size = max - min;
		Vector3 mid = (min + max) * 0.5f;
		const float thick = 0.2f;
		void Wall(Vector3 centre, Vector3 extent) =>
			fence.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = extent }, Position = centre });
		Wall(new Vector3(min.X, mid.Y, mid.Z), new Vector3(thick, size.Y, size.Z));
		Wall(new Vector3(mid.X, mid.Y, min.Z), new Vector3(size.X, size.Y, thick));
		Wall(new Vector3(mid.X, mid.Y, max.Z), new Vector3(size.X, size.Y, thick));
		Wall(new Vector3(mid.X, max.Y, mid.Z), new Vector3(size.X, thick, size.Z));
	}

	public override void _Process(double delta)
	{
		if (_loseTime >= 0f && IsInstanceValid(_loser) && IsInstanceValid(_attacker))
			AnimateLoss((float)delta);
		// The host keeps the time.
		GameState state = GameState.Instance;
		if (!HasEnded && state != null && state.IsRunning && Multiplayer.IsServer() && state.ElapsedSeconds >= state.TimeLimitSeconds)
			RequestEnd(false);
	}

	public void RequestEnd(bool won)
	{
		if (HasEnded)
			return;
		Rpc(MethodName.Play, won);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Play(bool won)
	{
		if (HasEnded)
			return;
		HasEnded = true;
		GameState state = GameState.Instance;
		if (state != null)
		{
			// The escape stops the timer and hides it; a loss just stops it.
			if (won && Multiplayer.IsServer())
				state.TriggerEscape();
			else
				state.PauseTimer();
		}
		EmitSignal(SignalName.EndingStarted, won, ScreenTime);
		Run(won);
	}

	private async void Run(bool won)
	{
		var overlay = new CanvasLayer { Name = "EndingFade", Layer = 30 };
		AddChild(overlay);
		var black = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(black);
		Tween fadeOut = CreateTween();
		fadeOut.TweenProperty(black, "color:a", 1f, FadeTime);
		await ToSignal(fadeOut, Tween.SignalName.Finished);

		BuildScene(won);

		Tween fadeIn = CreateTween();
		fadeIn.TweenProperty(black, "color:a", 0f, FadeTime);
		await ToSignal(GetTree().CreateTimer(Mathf.Max(0f, ScreenTime - FadeTime * 2f)), SceneTreeTimer.SignalName.Timeout);
		Tween end = CreateTween();
		end.TweenProperty(black, "color:a", 1f, FadeTime);
		await ToSignal(end, Tween.SignalName.Finished);
		ReturnToMenu();
	}

	private void BuildScene(bool won)
	{
		var hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		if (hud != null)
			hud.Visible = false;

		_room.Visible = true;
		Color color = won ? WinColor : LoseColor;
		for (int i = 1; i <= 2; i++)
			ColorCircle(i, color);
		AddTitle(won);

		// The real players stay where they were, hidden and frozen; stand-ins take their place on the circles.
		Player present = null, past = null;
		foreach (Node node in GetTree().GetNodesInGroup("players"))
		{
			if (node is not Player player)
				continue;
			player.Visible = false;
			if (player.IsMultiplayerAuthority())
			{
				player.InputLocked = true;
				player.Velocity = Vector3.Zero;
			}
			bool isPast = GameState.Instance != null && GameState.Instance.Players.TryGetValue(player.PeerId, out PlayerInfo info)
				&& info.Era == Era.Past;
			if (isPast)
				past = player;
			else
				present = player;
		}
		CharacterModel presentStandIn = AddStandIn(present, false, "Spawn_P1", won);
		CharacterModel pastStandIn = AddStandIn(past, true, "Spawn_P2", won);
		if (!won && presentStandIn != null && pastStandIn != null)
		{
			_loser = presentStandIn;
			_attacker = pastStandIn;
			_attackerStart = pastStandIn.Position;
			// Beside the explorer, a little behind, close enough to reach them.
			_attackFrom = presentStandIn.Position + new Vector3(0.55f, 0f, 0.12f);
			_loseTime = 0f;
		}

		var camera = new Camera3D { Name = "EndingCamera", Fov = 55f };
		_room.AddChild(camera);
		camera.Position = CameraPosition;
		camera.LookAt(_room.ToGlobal(CameraTarget), Vector3.Up);
		camera.MakeCurrent();
		if (!won)
			MakeGloomy(camera);
	}

	// The lose screen: the room has fallen in. The braziers burn low, a brown haze hangs in the air with dust drifting
	// through it and sand trickling from the ceiling, and rubble lies heaped along the walls and strewn over the floor.
	private void MakeGloomy(Camera3D camera)
	{
		var rng = new RandomNumberGenerator { Seed = 77 };
		foreach (Node node in _room.FindChildren("*", "OmniLight3D", true, false))
		{
			var light = (OmniLight3D)node;
			if (light.Name.ToString().Contains("Brazier"))
				light.LightEnergy = 0.55f;
			else if (light.Name.ToString().Contains("Fill"))
				light.LightEnergy = 0.12f;
		}

		// The camera's own copy of the world's look, darker, drained and hazy.
		if (_room.GetWorld3D()?.Environment?.Duplicate() is Godot.Environment gloom)
		{
			gloom.AmbientLightEnergy *= 0.55f;
			gloom.FogEnabled = true;
			gloom.FogLightColor = new Color(0.3f, 0.23f, 0.16f);
			gloom.FogLightEnergy = 0.6f;
			gloom.FogDensity = 0.09f;
			gloom.FogSkyAffect = 0f;
			gloom.AdjustmentEnabled = true;
			gloom.AdjustmentBrightness = 0.82f;
			gloom.AdjustmentContrast = 1.08f;
			gloom.AdjustmentSaturation = 0.55f;
			camera.Environment = gloom;
		}

		Material stone = _room.GetNodeOrNull<MeshInstance3D>("Crypt_Pillar_1")?.GetActiveMaterial(0);
		Material[] shades = Rubble.Shades(stone, rng, 5);
		var debris = new Node3D { Name = "Collapse" };
		_room.AddChild(debris);
		// Heaps in view between the pillars and the dais, on the dais and in the corners (clear of the two circles at
		// x ±0.75, z 0 and of the camera).
		var heaps = new (Vector3 At, float Size, int Stones)[]
		{
			(new Vector3(-1.25f, 0f, 1.95f), 1.1f, 26), (new Vector3(1.35f, 0f, 1.75f), 1.0f, 24), (new Vector3(0.45f, 0.34f, 2.6f), 0.6f, 12),
			(new Vector3(-2.75f, 0f, 2.9f), 1.0f, 18), (new Vector3(2.65f, 0f, 2.7f), 1.1f, 18), (new Vector3(-1.75f, 0f, -0.55f), 0.55f, 12),
			(new Vector3(1.75f, 0f, -0.75f), 0.5f, 10),
		};
		foreach ((Vector3 at, float size, int stones) in heaps)
		{
			for (int i = 0; i < stones; i++)
			{
				// Bigger stones at the bottom and middle of the heap, smaller ones higher and further out.
				float k = i / (float)stones;
				float s = size * Mathf.Lerp(0.55f, 0.15f, k) * rng.RandfRange(0.7f, 1.2f);
				float r = size * Mathf.Lerp(0.15f, 0.75f, k) * rng.RandfRange(0.5f, 1f);
				float a = rng.RandfRange(0f, Mathf.Tau);
				MeshInstance3D rock = Rubble.StillRock(new Vector3(s, s * rng.RandfRange(0.5f, 0.8f), s * rng.RandfRange(0.7f, 1.1f)), shades[rng.RandiRange(0, shades.Length - 1)], rng);
				debris.AddChild(rock);
				float height = Mathf.Max(0f, size * 0.55f * (1f - r / size) - s * 0.2f);
				rock.Position = at + new Vector3(Mathf.Cos(a) * r, height + s * 0.25f, Mathf.Sin(a) * r);
			}
		}
		// A big block fallen from the ceiling, lying cracked in the foreground.
		MeshInstance3D block = Rubble.StillRock(new Vector3(0.75f, 0.42f, 0.55f), shades[0], rng);
		debris.AddChild(block);
		block.Position = new Vector3(-1.45f, 0.17f, -1.15f);
		// Gravel and odd stones scattered over the floor, keeping the circles clear.
		for (int i = 0; i < 70; i++)
		{
			var at = new Vector3(rng.RandfRange(-3.3f, 3.3f), 0f, rng.RandfRange(-3.2f, 3.2f));
			if (Mathf.Abs(Mathf.Abs(at.X) - 0.75f) < 0.6f && Mathf.Abs(at.Z) < 0.6f)
				continue;
			float s = rng.Randf() < 0.75f ? rng.RandfRange(0.04f, 0.1f) : rng.RandfRange(0.14f, 0.28f);
			MeshInstance3D rock = Rubble.StillRock(new Vector3(s, s * 0.6f, s), shades[rng.RandiRange(0, shades.Length - 1)], rng);
			debris.AddChild(rock);
			rock.Position = at + Vector3.Up * s * 0.25f;
		}

		StandardMaterial3D dust = Rubble.DustMaterial();
		var dustColor = new Color(0.6f, 0.5f, 0.38f);
		// Motes drifting in the haze.
		debris.AddChild(new CpuParticles3D
		{
			Name = "Motes",
			// Kept back from the camera (at z -3.1), where a mote would be a big blur.
			Position = new Vector3(0f, 1.8f, 1.2f),
			Amount = 260,
			Lifetime = 9f,
			Preprocess = 9f,
			Mesh = new QuadMesh { Size = new Vector2(0.014f, 0.014f), Material = dust },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(3.2f, 1.8f, 2.2f),
			Direction = Vector3.Down,
			Spread = 180f,
			InitialVelocityMin = 0.02f,
			InitialVelocityMax = 0.08f,
			Gravity = new Vector3(0f, -0.015f, 0f),
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.6f,
			Color = new Color(0.42f, 0.35f, 0.26f, 0.4f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		// Thin streams of sand from cracks in the ceiling.
		foreach (Vector3 crack in new[] { new Vector3(-2.2f, 3.95f, 2.2f), new Vector3(2.0f, 3.95f, 0.6f), new Vector3(-0.3f, 3.95f, 2.6f) })
		{
			debris.AddChild(new CpuParticles3D
			{
				Name = "SandTrickle",
				Position = crack,
				Amount = 140,
				Lifetime = 1.4f,
				Preprocess = 2f,
				Mesh = new QuadMesh { Size = new Vector2(0.022f, 0.022f), Material = dust },
				EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
				EmissionSphereRadius = 0.04f,
				Direction = Vector3.Down,
				Spread = 4f,
				InitialVelocityMin = 0.1f,
				InitialVelocityMax = 0.3f,
				Gravity = new Vector3(0f, -4f, 0f),
				Color = dustColor with { A = 0.45f },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
	}

	// Under the fade (layer 30), so it fades in and out with the room.
	private void AddTitle(bool won)
	{
		var layer = new CanvasLayer { Name = "EndingTitle", Layer = 29 };
		AddChild(layer);
		var title = new Label
		{
			Text = won ? WinTitle : LoseTitle,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		title.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		title.OffsetTop = 70f;
		title.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } });
		title.AddThemeFontSizeOverride("font_size", 130);
		title.AddThemeColorOverride("font_color", won ? new Color(1f, 0.84f, 0.45f) : new Color(0.9f, 0.12f, 0.08f));
		title.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.04f, 0.02f));
		title.AddThemeConstantOverride("outline_size", 18);
		title.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
		title.AddThemeConstantOverride("shadow_offset_x", 4);
		title.AddThemeConstantOverride("shadow_offset_y", 6);
		layer.AddChild(title);
		// A slow swell while it's up.
		title.PivotOffset = new Vector2(960f, 80f);
		title.Scale = Vector2.One * 0.92f;
		CreateTween().TweenProperty(title, "scale", Vector2.One, ScreenTime).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	private void ColorCircle(int index, Color color)
	{
		if (_room.GetNodeOrNull<MeshInstance3D>($"Spawn_P{index}_Glow") is MeshInstance3D glow)
		{
			var material = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = color,
				EmissionEnabled = true,
				Emission = color,
				EmissionEnergyMultiplier = 2f,
			};
			glow.MaterialOverride = material;
		}
		if (_room.GetNodeOrNull<OmniLight3D>($"Spawn_P{index}_Light") is OmniLight3D light)
		{
			light.LightColor = color;
			light.LightEnergy = 1.5f;
		}
	}

	private CharacterModel AddStandIn(Player player, bool isPast, string circle, bool won)
	{
		var spot = _room.GetNodeOrNull<Node3D>(circle);
		if (spot == null)
			return null;
		CharacterModel source = player?.Character;
		PackedScene scene = source?.Scene ?? GD.Load<PackedScene>(isPast ? "res://art/egyptian.glb" : "res://art/explorer.glb");
		if (player == null && GameState.Instance != null && GameState.Instance.Players.Count > 0)
			return null;
		var standIn = new CharacterModel
		{
			Name = isPast ? "PastStandIn" : "PresentStandIn",
			Scene = scene,
			Scale = source?.Scale ?? Vector3.One,
			ArmDownDegrees = source?.ArmDownDegrees ?? 30f,
			ElbowBendDegrees = source?.ElbowBendDegrees ?? 10f,
			Dance = won ? 1f : 0f,
			Shame = won ? 0f : 1f,
		};
		_room.AddChild(standIn);
		standIn.Position = spot.Position;
		return standIn;
	}

	private void AnimateLoss(float dt)
	{
		_loseTime += dt;
		float t = _loseTime;
		float kneel = Mathf.Clamp((t - KneelAt) / KneelTime, 0f, 1f);
		_loser.Kneel = kneel;

		// The past character goes down too, then snaps back up in a rage, drops the shame, and stomps over.
		float rise = Mathf.Clamp((t - RiseAt) / RiseTime, 0f, 1f);
		_attacker.Kneel = kneel * (1f - rise);
		_attacker.Shame = 1f - rise;
		if (t >= RiseAt + RiseTime)
		{
			_attacker.Animate = true;
			float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - RiseAt - RiseTime) / StompTime, 0f, 1f));
			_attacker.Position = _attackerStart.Lerp(_attackFrom, k);
			// Facing the explorer (-X from where it ends up).
			float yaw = Mathf.Lerp(0f, Mathf.Pi / 2f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - RiseAt - RiseTime) / 0.5f, 0f, 1f)));
			_attacker.Rotation = new Vector3(0f, yaw, 0f);
		}
		else if (rise > 0f)
			// Fists clench and the head comes up as it rises.
			_attacker.HeadPitchDegrees = 12f * rise;

		if (t < BeatAt)
			return;
		// The beating: fists rain down; the explorer cowers on their knees, arms over the head, flinching at each blow.
		float beat = t - BeatAt;
		_attacker.Pummel = Mathf.Clamp(beat / 0.3f, 0f, 1f);
		_attacker.PummelTime = beat;
		_loser.Shame = 1f - Mathf.Clamp(beat / 0.3f, 0f, 1f);
		_loser.Shield = Mathf.Clamp(beat / 0.3f, 0f, 1f);
		// Time since the last fist landed (each fist lands at PummelHit of its swing; the other is half a swing behind).
		float cycle = beat / CharacterModel.PummelPeriod;
		float sinceHit = float.MaxValue;
		foreach (float offset in new[] { 0f, 0.5f })
		{
			float since = (cycle + offset - CharacterModel.PummelHit) - Mathf.Floor(cycle + offset - CharacterModel.PummelHit);
			if (cycle + offset >= CharacterModel.PummelHit)
				sinceHit = Mathf.Min(sinceHit, since * CharacterModel.PummelPeriod);
		}
		float flinch = sinceHit < 1f ? Mathf.Exp(-sinceHit * 12f) : 0f;
		// Knocked away from the blows (towards -X, away from the attacker) and down.
		_loser.Rotation = new Vector3(0f, -0.35f, Mathf.DegToRad(9f) * flinch);
		_loser.HeadPitchDegrees = -14f - 10f * flinch;
	}

	// Ends the session on this machine and reloads the game, which opens the menu.
	private void ReturnToMenu()
	{
		if (Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer)
			Multiplayer.MultiplayerPeer?.Close();
		Multiplayer.MultiplayerPeer = null;
		GameState.Instance?.Reset();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ReloadCurrentScene();
	}
}
