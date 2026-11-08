using System.Collections.Generic;
using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// The present's spike-pit room: the step blocks hang up by their rods and only come down while someone holds a lever
// (hold interact on it; letting go, or walking away, releases it). One player holds lever 1 while the other crosses and
// holds lever 2, then the first crosses. If no lever is held any more, the blocks jolt back up and whoever stands on one
// stumbles off into the spikes (death screen, then back at the start platform). Falling in any other way kills too.
// Each machine checks only its own player; lever holds are sent to both.
public partial class TrapBridge : Node
{
	[Export] public Node3D Level { get; set; }
	[Export] public int LeverCount { get; set; } = 2;
	[Export] public int BlockCount { get; set; } = 5;
	// How far above their walkable height the blocks hang while no lever is held.
	[Export] public float RaiseHeight { get; set; } = 2.6f;
	[Export] public float LowerTime { get; set; } = 1.4f;
	[Export] public float RaiseTime { get; set; } = 0.6f;
	// Dropping a lever makes the blocks shudder this long before they go up.
	[Export] public float ShudderTime { get; set; } = 0.5f;
	// A holder further than this from their lever lets go.
	[Export] public float HoldRange { get; set; } = 2.2f;
	// Where a dead player comes back, in Level's space (the start platform, facing the pit).
	[Export] public Vector3 RespawnPosition { get; set; } = new Vector3(-6.4f, 1.6f, 16.3f);
	[Export] public string DeathTitle { get; set; } = "IMPALED";
	[Export] public string DeathText { get; set; } = "The blocks rose without a hand on the lever.";
	[Export] public float DeathScreenTime { get; set; } = 4f;

	private readonly List<Node3D> _levers = new();
	private readonly List<Basis> _leverRest = new();
	private readonly List<float> _leverPull = new();
	private readonly List<Tween> _leverTweens = new();
	private long[] _holders;
	private readonly List<MeshInstance3D> _blocks = new();
	private readonly List<Vector3> _blockDown = new();
	private Tween _blockTween;
	private bool _down;
	private bool _dying;
	private int _localHeld = -1;
	private string _holdPrompt = "Hold F to pull the lever";
	private const string HoldingPrompt = "Holding the lever (let go of F to release)";

	public override void _Ready()
	{
		if (Level == null)
		{
			GD.PushWarning($"{Name}: set Level.");
			return;
		}
		_holders = new long[LeverCount];
		for (int i = 1; i <= LeverCount; i++)
			SetUpLever(i);
		for (int i = 1; i <= BlockCount; i++)
		{
			var block = Level.GetNodeOrNull<MeshInstance3D>($"Ruin_Trap_StepBlock_{i}");
			if (block == null)
				continue;
			_blocks.Add(block);
			_blockDown.Add(block.Position);
			block.Position += Vector3.Up * RaiseHeight;
		}
		SetUpKillZone();
	}

	private void SetUpLever(int number)
	{
		var pivot = Level.GetNodeOrNull<Node3D>($"Ruin_Trap_Lever_{number}_Pivot");
		var grip = pivot?.GetNodeOrNull<MeshInstance3D>($"Ruin_Trap_Lever_{number}_Grip");
		if (pivot == null)
			return;
		int index = _levers.Count;
		_levers.Add(pivot);
		_leverRest.Add(pivot.Basis);
		_leverTweens.Add(null);
		// Pulled = the handle swung over to the mirror side of the pivot.
		float pull = Mathf.Pi * 0.6f;
		if (grip != null)
		{
			// The grip is a direct child of the pivot; measured in the pivot's parent space, where the swing happens.
			Vector3 g = pivot.Basis * (grip.Transform * grip.GetAabb().GetCenter());
			pull = Mathf.Pi - 2f * Mathf.Atan2(g.Y, g.X);
			if (pull > Mathf.Pi)
				pull -= Mathf.Tau;
		}
		_leverPull.Add(pull);

		var area = new Interactable { Name = $"Lever{number}", Id = $"trap_lever_{number}", MummyCanUse = true };
		area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.9f, 0.6f) } });
		pivot.GetParent().AddChild(area);
		area.Position = pivot.Position + Vector3.Up * 0.2f;
		area.PromptFor = player =>
		{
			if (_localHeld == index)
				return HoldingPrompt;
			return _holders[index] != 0 ? "" : _holdPrompt;
		};
		area.Interacted += player => OnLeverInteracted(index, player);
	}

	private void SetUpKillZone()
	{
		var zone = Level.GetNodeOrNull<MeshInstance3D>("Ruin_Trap_SpikePit_KillZone");
		if (zone == null)
			return;
		// It's only a marker of the kill volume; seen in game, it covers the spikes.
		zone.Visible = false;
		Aabb box = zone.Transform * zone.GetAabb();
		var area = new Area3D { Name = "SpikePitKill", CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
		area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size + new Vector3(0f, 0.4f, 0f) } });
		Level.AddChild(area);
		area.Position = box.GetCenter();
		area.BodyEntered += body =>
		{
			if (body is Player player && player.IsMultiplayerAuthority() && !_dying)
				Die(player);
		};
	}

	private void OnLeverInteracted(int index, Player player)
	{
		if (!player.IsMultiplayerAuthority() || _holders[index] != 0 || _localHeld >= 0 || _dying)
			return;
		_localHeld = index;
		_holdPrompt = "Hold F to pull the lever";
		SendHold(index, player.PeerId);
	}

	public override void _Process(double delta)
	{
		if (_localHeld < 0)
			return;
		Player me = LocalPlayer();
		bool keep = me != null && Input.IsActionPressed("interact") && !me.InputLocked
			&& me.GlobalPosition.DistanceTo(_levers[_localHeld].GlobalPosition) <= HoldRange;
		if (keep)
			return;
		int index = _localHeld;
		_localHeld = -1;
		SendHold(index, 0);
	}

	private void SendHold(int index, long peer)
	{
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
			Rpc(MethodName.SetHolder, index, peer);
		else
			SetHolder(index, peer);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SetHolder(int index, long peer)
	{
		if (index < 0 || index >= _holders.Length)
			return;
		_holders[index] = peer;
		AnimateLever(index, peer != 0);

		bool down = false;
		foreach (long holder in _holders)
			down |= holder != 0;
		if (down == _down)
			return;
		_down = down;
		if (down)
			LowerBlocks();
		else
			DropBlocks();
	}

	private void AnimateLever(int index, bool pulled)
	{
		_leverTweens[index]?.Kill();
		Node3D pivot = _levers[index];
		Basis from = pivot.Basis;
		Basis to = pulled ? new Basis(Vector3.Back, _leverPull[index]) * _leverRest[index] : _leverRest[index];
		Tween tween = CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.TweenMethod(Callable.From<float>(t => pivot.Basis = from.Slerp(to, t)), 0f, 1f, pulled ? 0.45f : 0.25f);
		_leverTweens[index] = tween;
	}

	private void LowerBlocks()
	{
		_blockTween?.Kill();
		_blockTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		for (int i = 0; i < _blocks.Count; i++)
			_blockTween.TweenProperty(_blocks[i], "position", _blockDown[i], LowerTime).SetDelay(i * 0.08f);
	}

	private void DropBlocks()
	{
		Player me = LocalPlayer();
		if (me != null && !_dying)
		{
			MeshInstance3D under = BlockUnder(me);
			if (under != null)
				StumbleOff(me, under);
		}
		_blockTween?.Kill();
		_blockTween = CreateTween().SetParallel();
		for (int i = 0; i < _blocks.Count; i++)
		{
			MeshInstance3D block = _blocks[i];
			Vector3 start = block.Position;
			Vector3 up = _blockDown[i] + Vector3.Up * RaiseHeight;
			// A shudder (the chains giving), then a snap back up.
			_blockTween.TweenMethod(Callable.From<float>(t =>
				block.Position = start + new Vector3(Mathf.Sin(t * 70f + i) * 0.03f, -0.06f * t, Mathf.Cos(t * 55f + i) * 0.03f)),
				0f, 1f, ShudderTime);
			_blockTween.TweenProperty(block, "position", up, RaiseTime).SetDelay(ShudderTime)
				.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
		}
	}

	private MeshInstance3D BlockUnder(Player player)
	{
		Vector3 feet = player.GlobalPosition;
		foreach (MeshInstance3D block in _blocks)
		{
			Aabb box = block.GlobalTransform * block.GetAabb();
			float top = box.End.Y;
			if (feet.X > box.Position.X - 0.35f && feet.X < box.End.X + 0.35f
				&& feet.Z > box.Position.Z - 0.35f && feet.Z < box.End.Z + 0.35f
				&& feet.Y > top - 0.4f && feet.Y < top + 0.8f)
				return block;
		}
		return null;
	}

	// The local player loses their footing: a lurch, then over the edge and down onto the spikes.
	private async void StumbleOff(Player player, MeshInstance3D block)
	{
		_dying = true;
		player.InputLocked = true;
		player.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;

		Vector3 center = block.GlobalTransform * block.GetAabb().GetCenter();
		Vector3 dir = player.GlobalPosition - center;
		dir.Y = 0f;
		// Off the side of the bridge, not along it onto the next block.
		dir.X *= 0.3f;
		if (dir.LengthSquared() < 0.0004f)
			dir = new Vector3(0f, 0f, GD.Randf() < 0.5f ? 1f : -1f);
		dir = dir.Normalized();
		SendStumble(player.PeerId, dir);

		var camera = player.GetNodeOrNull<Camera3D>("Camera");
		Vector3 camStart = camera?.Rotation ?? Vector3.Zero;
		Vector3 start = player.GlobalPosition;
		Vector3 edge = start + dir * 0.55f;
		Vector3 bottom = new Vector3(start.X, -1.8f, start.Z) + dir * 1.4f;
		if (Level != null)
			bottom.Y = Level.GlobalPosition.Y - 1.8f;

		Tween tween = CreateTween();
		// Stumble: a step and a sway as the block drops under them.
		tween.TweenMethod(Callable.From<float>(t =>
		{
			if (!IsInstanceValid(player))
				return;
			player.GlobalPosition = start.Lerp(edge, t) + Vector3.Down * 0.08f * Mathf.Sin(t * Mathf.Pi);
			if (camera != null)
				camera.Rotation = camStart + new Vector3(-0.25f * t, 0f, Mathf.Sin(t * 9f) * 0.12f * (1f - t * 0.5f));
		}), 0f, 1f, 0.55f);
		// Fall: tipping over the edge and down, looking at the spikes.
		tween.TweenMethod(Callable.From<float>(t =>
		{
			if (!IsInstanceValid(player))
				return;
			Vector3 p = edge.Lerp(bottom, t);
			p.Y = Mathf.Lerp(edge.Y, bottom.Y, t * t) + Mathf.Sin(t * Mathf.Pi) * 0.25f;
			player.GlobalPosition = p;
			if (camera != null)
				camera.Rotation = new Vector3(Mathf.Lerp(camStart.X - 0.25f, -1.35f, t), camStart.Y, Mathf.Lerp(0.06f, 0.5f, t));
		}), 0f, 1f, 0.75f);
		await ToSignal(tween, Tween.SignalName.Finished);
		if (IsInstanceValid(player))
			ShowDeath(player);
	}

	// Fell in some other way (a missed jump, raised blocks): straight to the death screen.
	private void Die(Player player)
	{
		_dying = true;
		player.InputLocked = true;
		player.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;
		SendStumble(player.PeerId, -player.GlobalBasis.Z);
		ShowDeath(player);
	}

	private async void ShowDeath(Player player)
	{
		if (Events.Instance != null)
			Events.Instance.EmitSignal(Events.SignalName.CameraShake, 0.6f);
		var hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		if (hud != null)
			hud.Visible = false;

		var overlay = new CanvasLayer { Name = "TrapDeath", Layer = 20 };
		AddChild(overlay);
		var fade = new ColorRect { Color = new Color(0.35f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(fade);
		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Modulate = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(box);
		var font = new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } };
		var title = new Label { Text = DeathTitle, HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", 64);
		title.AddThemeColorOverride("font_color", new Color(0.85f, 0.15f, 0.1f));
		box.AddChild(title);
		var text = new Label { Text = DeathText, HorizontalAlignment = HorizontalAlignment.Center };
		text.AddThemeFontOverride("font", font);
		text.AddThemeFontSizeOverride("font_size", 26);
		text.AddThemeColorOverride("font_color", new Color(0.93f, 0.86f, 0.7f));
		box.AddChild(text);

		Tween show = CreateTween();
		show.TweenProperty(fade, "color", new Color(0.12f, 0f, 0f, 1f), 0.35f);
		show.TweenProperty(box, "modulate", Colors.White, 0.6f);
		await ToSignal(GetTree().CreateTimer(DeathScreenTime), SceneTreeTimer.SignalName.Timeout);

		// Back at the start platform.
		if (IsInstanceValid(player) && Level != null)
		{
			player.GlobalPosition = Level.ToGlobal(RespawnPosition);
			player.Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0f);
			var camera = player.GetNodeOrNull<Camera3D>("Camera");
			if (camera != null)
				camera.Rotation = Vector3.Zero;
			player.Velocity = Vector3.Zero;
			player.SetPhysicsProcess(true);
			player.InputLocked = false;
			SendStumble(player.PeerId, Vector3.Zero);
		}
		Tween hide = CreateTween();
		hide.TweenProperty(box, "modulate", Colors.Transparent, 0.4f);
		hide.TweenProperty(fade, "color", new Color(0.12f, 0f, 0f, 0f), 0.6f);
		await ToSignal(hide, Tween.SignalName.Finished);
		overlay.QueueFree();
		if (hud != null && IsInstanceValid(hud))
			hud.Visible = true;
		_dying = false;
	}

	private void SendStumble(long peer, Vector3 dir)
	{
		if (Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
			Rpc(MethodName.TipCharacter, peer, dir);
		else
			TipCharacter(peer, dir);
	}

	// Tips the player's body over towards dir (what the other player sees); Vector3.Zero stands it back up.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void TipCharacter(long peer, Vector3 dir)
	{
		Player player = null;
		foreach (Node node in GetTree().GetNodesInGroup("players"))
			if (node is Player p && p.PeerId == peer)
				player = p;
		CharacterModel body = player?.Character;
		if (body == null)
			return;
		if (!body.HasMeta("trap_rest"))
			body.SetMeta("trap_rest", body.Basis);
		Basis rest = body.GetMeta("trap_rest").AsBasis();
		if (dir == Vector3.Zero)
		{
			body.Basis = rest;
			return;
		}
		Vector3 local = player.GlobalBasis.Inverse() * dir;
		local.Y = 0f;
		Vector3 axis = Vector3.Up.Cross(local.Normalized());
		if (axis.LengthSquared() < 0.001f)
			axis = Vector3.Right;
		axis = axis.Normalized();
		Tween tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(a => { if (IsInstanceValid(body)) body.Basis = new Basis(axis, a) * rest; }), 0f, 0.35f, 0.5f);
		tween.TweenMethod(Callable.From<float>(a => { if (IsInstanceValid(body)) body.Basis = new Basis(axis, a) * rest; }), 0.35f, 1.6f, 0.6f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
	}

	private Player LocalPlayer()
	{
		foreach (Node node in GetTree().GetNodesInGroup("players"))
			if (node is Player p && p.IsMultiplayerAuthority())
				return p;
		return null;
	}
}
