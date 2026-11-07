using Gamejam.Core;
using Godot;

namespace Gamejam.Net;

// Hosts or joins a 2-player game and spawns each player into their era's map.
// Root of net/Game.tscn. Test shortcut: run with user args `--host`, `--host-past` or `--join=<ip>`.
public partial class Session : Node
{
	public const int Port = 7777;

	[Export] public PackedScene PlayerScene { get; set; }

	// Players move on their own machine and push their transform to the other peer.
	private static readonly string[] SyncedProperties = { ".:position", ".:rotation" };

	private Node3D _players;
	private MultiplayerSpawner _spawner;
	private Lobby _lobby;
	private Era _hostEra = Era.Present;

	public override void _Ready()
	{
		PlayerScene ??= GD.Load<PackedScene>("res://gameplay/Player.tscn");
		_players = GetNode<Node3D>("Players");
		_spawner = GetNode<MultiplayerSpawner>("PlayerSpawner");
		_lobby = GetNode<Lobby>("Lobby");

		_spawner.SpawnFunction = Callable.From<Variant, Node>(SpawnPlayer);
		_lobby.HostPressed += Host;
		_lobby.JoinPressed += Join;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;

		foreach (Node node in GetTree().GetNodesInGroup("exit"))
		{
			if (node is Area3D exit)
				exit.BodyEntered += OnExitBodyEntered;
		}

		Callable.From(HandleCommandLine).CallDeferred();
	}

	public override void _ExitTree()
	{
		// The MultiplayerAPI outlives this scene (it's reloaded after a disconnect).
		Multiplayer.PeerConnected -= OnPeerConnected;
		Multiplayer.PeerDisconnected -= OnPeerDisconnected;
		Multiplayer.ConnectedToServer -= OnConnectedToServer;
		Multiplayer.ConnectionFailed -= OnConnectionFailed;
		Multiplayer.ServerDisconnected -= OnServerDisconnected;
	}

	private void Host(Era era)
	{
		var peer = new ENetMultiplayerPeer();
		// One client: the game is exactly 2 players.
		Error err = peer.CreateServer(Port, GameState.PlayerCount - 1);
		if (err != Error.Ok)
		{
			_lobby.ShowMenu($"Could not host on port {Port} ({err}).");
			return;
		}
		Multiplayer.MultiplayerPeer = peer;
		_hostEra = era;
		_lobby.ShowWaiting($"Hosting on port {Port}. Waiting for your partner to join...");
		Spawn(Multiplayer.GetUniqueId(), era);
	}

	private void Join(string address)
	{
		var peer = new ENetMultiplayerPeer();
		Error err = peer.CreateClient(address, Port);
		if (err != Error.Ok)
		{
			_lobby.ShowMenu($"Could not connect to {address} ({err}).");
			return;
		}
		Multiplayer.MultiplayerPeer = peer;
		_lobby.ShowMenu($"Connecting to {address}...", interactive: false);
	}

	private void Spawn(long id, Era era) =>
		_spawner.Spawn(new Godot.Collections.Array { id, (int)era });

	// Runs on every peer (the spawner replays it on clients), so it must only depend on the spawn data.
	private Node SpawnPlayer(Variant data)
	{
		Godot.Collections.Array args = data.AsGodotArray();
		long id = args[0].AsInt64();
		var era = (Era)args[1].AsInt32();

		var player = PlayerScene.Instantiate<Node3D>();
		// Player.cs takes its peer id and input authority from the node name.
		player.Name = id.ToString();
		Marker3D spawn = FindSpawnPoint(era);
		if (spawn != null)
			player.Transform = spawn.GlobalTransform;
		else
			GD.PushWarning($"No spawn point for era {era}. Add a Marker3D in group \"spawn_points\" with meta era.");
		player.AddChild(CreateSynchronizer());

		player.Ready += () => GameState.Instance.RegisterPlayer(id, era, player);
		player.TreeExiting += () => GameState.Instance.UnregisterPlayer(id);
		return player;
	}

	private static MultiplayerSynchronizer CreateSynchronizer()
	{
		var config = new SceneReplicationConfig();
		foreach (string property in SyncedProperties)
		{
			var path = new NodePath(property);
			config.AddProperty(path);
			// The spawn function already places the player, so only stream updates.
			config.PropertySetSpawn(path, false);
			config.PropertySetReplicationMode(path, SceneReplicationConfig.ReplicationMode.Always);
		}
		return new MultiplayerSynchronizer { Name = "Sync", ReplicationConfig = config };
	}

	private Marker3D FindSpawnPoint(Era era)
	{
		string wanted = era == Era.Past ? "past" : "present";
		foreach (Node node in GetTree().GetNodesInGroup("spawn_points"))
		{
			if (node is Marker3D marker && marker.HasMeta("era") && marker.GetMeta("era").AsString() == wanted)
				return marker;
		}
		return null;
	}

	private void OnPeerConnected(long id)
	{
		if (!Multiplayer.IsServer())
			return;
		Spawn(id, _hostEra == Era.Past ? Era.Present : Era.Past);
	}

	private void OnPeerDisconnected(long id)
	{
		if (!Multiplayer.IsServer())
			return;
		// Freeing it on the host makes the spawner despawn it everywhere.
		_players.GetNodeOrNull(id.ToString())?.QueueFree();
		_lobby.ShowWaiting("Your partner left. Waiting for someone to join...");
	}

	private void OnConnectedToServer() =>
		_lobby.ShowWaiting("Connected. Waiting for the game to start...");

	private void OnConnectionFailed()
	{
		Multiplayer.MultiplayerPeer = null;
		_lobby.ShowMenu("Could not connect. Check the IP and that the host is running.");
	}

	private void OnServerDisconnected()
	{
		Multiplayer.MultiplayerPeer = null;
		GameState.Instance.Reset();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		// Deferred: we're inside a multiplayer callback, and reloading frees this node.
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	private void OnExitBodyEntered(Node3D body)
	{
		if (!Multiplayer.IsServer() || !long.TryParse(body.Name, out long id))
			return;
		if (GameState.Instance.Players.TryGetValue(id, out PlayerInfo info) && info.Era == Era.Present)
			GameState.Instance.TriggerEscape();
	}

	private void HandleCommandLine()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg == "--host")
				Host(Era.Present);
			else if (arg == "--host-past")
				Host(Era.Past);
			else if (arg.StartsWith("--join"))
				Join(arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "127.0.0.1");
		}
	}
}
