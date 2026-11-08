using System.Collections.Generic;
using Godot;

namespace Gamejam.Core;

// Who plays which era, the shared world flags, the timer and the escape.
// The host owns all of it; clients only ever receive copies.
public partial class GameState : Node
{
	public static GameState Instance { get; private set; }

	[Signal] public delegate void PlayerJoinedEventHandler(long id);
	[Signal] public delegate void PlayerLeftEventHandler(long id);
	[Signal] public delegate void EraAssignedEventHandler(long id, Era era);
	[Signal] public delegate void GameStartedEventHandler();
	// Fires on every peer.
	[Signal] public delegate void FlagChangedEventHandler(string key, int value);
	[Signal] public delegate void EscapedEventHandler(double elapsedSeconds);

	public const int PlayerCount = 2;

	public IReadOnlyDictionary<long, PlayerInfo> Players => _players;
	public Era LocalEra => _players.TryGetValue(Multiplayer.GetUniqueId(), out PlayerInfo info) ? info.Era : Era.Present;
	public bool IsRunning { get; private set; }
	private int _timerPauseCount;
	// Testing: the game starts with just the local player (net/Session.cs `--solo`).
	public bool Solo { get; set; }
	public double ElapsedSeconds { get; private set; }

	private readonly Dictionary<long, PlayerInfo> _players = new();
	private readonly Dictionary<long, Node3D> _playerNodes = new();
	private readonly Dictionary<string, int> _flags = new();

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		Multiplayer.PeerConnected += OnPeerConnected;
	}

	public override void _ExitTree()
	{
		Multiplayer.PeerConnected -= OnPeerConnected;
	}

	public override void _Process(double delta)
	{
		if (IsRunning && _timerPauseCount == 0)
			ElapsedSeconds += delta;
	}

	public void PauseTimer() => _timerPauseCount++;
	public void ResumeTimer() => _timerPauseCount = Mathf.Max(0, _timerPauseCount - 1);
	public int GetFlag(string key) => _flags.TryGetValue(key, out int value) ? value : 0;

	// Host only. Replicates to every peer, including the host itself.
	public void SetFlag(string key, int value)
	{
		if (!Multiplayer.IsServer())
		{
			GD.PushWarning($"SetFlag(\"{key}\") ignored: only the host sets flags.");
			return;
		}
		if (_flags.TryGetValue(key, out int current) && current == value)
			return;
		Rpc(MethodName.ReceiveFlag, key, value);
	}

	// Host only. Ends the game for both players.
	public void TriggerEscape()
	{
		if (!Multiplayer.IsServer() || !IsRunning)
			return;
		Rpc(MethodName.ReceiveEscape, ElapsedSeconds);
	}

	public Node3D GetPlayerNode(long id) =>
		_playerNodes.TryGetValue(id, out Node3D node) && IsInstanceValid(node) ? node : null;

	// Called by net/Session on every peer when a player node is ready. The spawn data carries the era,
	// so all peers build the same player list without extra RPCs.
	public void RegisterPlayer(long id, Era era, Node3D node)
	{
		_players[id] = new PlayerInfo { Name = $"Player {id}", Era = era };
		_playerNodes[id] = node;
		EmitSignal(SignalName.PlayerJoined, id);
		EmitSignal(SignalName.EraAssigned, id, Variant.From(era));

		if (!IsRunning && _players.Count == (Solo ? 1 : PlayerCount))
		{
			IsRunning = true;
			ElapsedSeconds = 0;
			EmitSignal(SignalName.GameStarted);
		}
	}

	public void UnregisterPlayer(long id)
	{
		if (!_players.Remove(id))
			return;
		_playerNodes.Remove(id);
		EmitSignal(SignalName.PlayerLeft, id);
	}

	// Back to an empty state after leaving a session.
	public void Reset()
	{
		_players.Clear();
		_playerNodes.Clear();
		_flags.Clear();
		IsRunning = false;
		ElapsedSeconds = 0;
		_timerPauseCount = 0;
	}

	// A player joining mid-game needs the flags that were already set.
	private void OnPeerConnected(long id)
	{
		if (!Multiplayer.IsServer())
			return;
		foreach (KeyValuePair<string, int> flag in _flags)
			RpcId(id, MethodName.ReceiveFlag, flag.Key, flag.Value);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveFlag(string key, int value)
	{
		_flags[key] = value;
		EmitSignal(SignalName.FlagChanged, key, value);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveEscape(double elapsedSeconds)
	{
		IsRunning = false;
		ElapsedSeconds = elapsedSeconds;
		EmitSignal(SignalName.Escaped, elapsedSeconds);
	}
}
