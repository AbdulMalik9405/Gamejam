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
	// Cutscenes pause the timer for everyone: this machine's own pauses, and (on the host) which peers have one going.
	// The host's clock is the only real one; it's sent out a few times a second so both players see the same time.
	private int _timerPauseCount;
	private readonly HashSet<long> _pausedPeers = new();
	private bool _hostPaused;
	private float _timeSyncIn;
	private const float TimeSyncInterval = 0.25f;
	// Testing: the game starts with just the local player (net/Session.cs `--solo`).
	public bool Solo { get; set; }
	public double ElapsedSeconds { get; private set; }
	// The game is lost when ElapsedSeconds reaches this (gameplay/GameEnding.cs); the HUD counts down to it.
	public double TimeLimitSeconds { get; set; } = 300d;

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
		if (!IsRunning)
			return;
		bool online = Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
		if (!online || Multiplayer.IsServer())
		{
			if (_pausedPeers.Count == 0 && _timerPauseCount == 0)
				ElapsedSeconds += delta;
			_timeSyncIn -= (float)delta;
			if (online && _timeSyncIn <= 0f)
				SendTime();
			return;
		}
		// Between the host's updates the client's copy runs on by itself, stopping straight away for its own cutscenes.
		if (!_hostPaused && _timerPauseCount == 0)
			ElapsedSeconds += delta;
	}

	public bool IsTimerPaused => _timerPauseCount > 0 || _pausedPeers.Count > 0 || _hostPaused;

	// Called by a cutscene on whichever machine plays it; the timer stops for both players until it's resumed.
	public void PauseTimer()
	{
		_timerPauseCount++;
		if (_timerPauseCount == 1)
			TellHostPaused(true);
	}

	public void ResumeTimer()
	{
		if (_timerPauseCount == 0)
			return;
		_timerPauseCount--;
		if (_timerPauseCount == 0)
			TellHostPaused(false);
	}

	private void TellHostPaused(bool paused)
	{
		long me = Multiplayer.GetUniqueId();
		if (Multiplayer.IsServer() || !Multiplayer.HasMultiplayerPeer() || Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer)
			SetPeerPaused(me, paused);
		else
			RpcId(1, MethodName.RequestPause, paused);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestPause(bool paused)
	{
		if (Multiplayer.IsServer())
			SetPeerPaused(Multiplayer.GetRemoteSenderId(), paused);
	}

	// Host only.
	private void SetPeerPaused(long peer, bool paused)
	{
		if (paused)
			_pausedPeers.Add(peer);
		else
			_pausedPeers.Remove(peer);
		SendTime();
	}

	private void SendTime()
	{
		_timeSyncIn = TimeSyncInterval;
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer && Multiplayer.IsServer())
			Rpc(MethodName.ReceiveTime, ElapsedSeconds, _pausedPeers.Count > 0 || _timerPauseCount > 0);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void ReceiveTime(double elapsedSeconds, bool paused)
	{
		ElapsedSeconds = elapsedSeconds;
		_hostPaused = paused;
	}
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
		// A player who leaves mid-cutscene doesn't hold the timer for ever.
		if (_pausedPeers.Remove(id))
			SendTime();
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
		_pausedPeers.Clear();
		_hostPaused = false;
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
