using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// How the game ends, for both players at once. Lost (the timer runs out, or a player dies in the spike trap) or won
// (a player gets halfway up the exit stair): the screen fades to black, both players appear on the two floor circles of
// art/spawn_room.glb (a room of its own, away from both maps), red and hanging their heads in shame when lost, blue and
// dancing when won, seen from in front. After ScreenTime it fades out and both go back to the menu.
// The music for it is played by audio/Music.cs on EndingStarted. Anything may call RequestEnd; it's sent to both.
public partial class GameEnding : Node
{
	[Export] public PackedScene RoomScene { get; set; }
	// Far from both maps (past at the origin, present at +200 m X), so neither sees it or its lights.
	[Export] public Vector3 RoomPosition { get; set; } = new Vector3(-200f, 0f, 0f);
	[Export] public float ScreenTime { get; set; } = 10f;
	[Export] public float FadeTime { get; set; } = 0.6f;
	// The win line: a box across the exit stair's middle step, in the present map's (level1old) space.
	[Export] public Vector3 WinZoneCenter { get; set; } = new Vector3(-23.05f, 5.9f, 16.3f);
	[Export] public Vector3 WinZoneSize { get; set; } = new Vector3(0.5f, 2.4f, 1.4f);
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
		// Each machine only reports its own player.
		zone.BodyEntered += body =>
		{
			if (body is Player player && player.IsMultiplayerAuthority())
				RequestEnd(true);
		};
	}

	public override void _Process(double delta)
	{
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
		AddStandIn(present, false, "Spawn_P1", won);
		AddStandIn(past, true, "Spawn_P2", won);

		var camera = new Camera3D { Name = "EndingCamera", Fov = 55f };
		_room.AddChild(camera);
		camera.Position = CameraPosition;
		camera.LookAt(_room.ToGlobal(CameraTarget), Vector3.Up);
		camera.MakeCurrent();
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

	private void AddStandIn(Player player, bool isPast, string circle, bool won)
	{
		var spot = _room.GetNodeOrNull<Node3D>(circle);
		if (spot == null)
			return;
		CharacterModel source = player?.Character;
		PackedScene scene = source?.Scene ?? GD.Load<PackedScene>(isPast ? "res://art/egyptian.glb" : "res://art/explorer.glb");
		if (player == null && GameState.Instance != null && GameState.Instance.Players.Count > 0)
			return;
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
