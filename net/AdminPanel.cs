using Gamejam.Core;
using Gamejam.Gameplay;
using Godot;

namespace Gamejam.Net;

// A testing shortcut: Ctrl+Z (or Cmd+Z) opens a panel that jumps the game to a part of the escape room: the start,
// each puzzle, the trap room (obby) or the boulder ramp. It's the whole game that jumps, for both players: every puzzle
// before that stage is solved through the puzzles' own code (keys revealed and picked up, keys in the door, the tile
// on the altar, the collapse), instantly, and then the cutscene that leads into the chosen stage plays for real (the
// last key reveal, or the collapse sequence before the obby). Each player is moved to the stage: the puzzle stops are in
// their own era's room (the rooms are laid out alike, 200 m apart); the trap room and boulder ramp only exist in the
// present. Going back to an earlier stage only moves you; solved puzzles stay solved.
public partial class AdminPanel : CanvasLayer
{
	// Each stop: its button, where to stand and which way to look, in the present map's space.
	private static readonly (string Name, Vector3 At, Vector3 LookAt, bool PresentOnly)[] Stops =
	{
		("Start", new(0f, 0.2f, 4.0f), new(0f, 1.2f, -2f), false),
		("Puzzle 1: symbol blocks", new(-5.65f, 0.1f, 0.35f), new(-6.9f, 1.1f, -0.8f), false),
		("Puzzle 2: trinkets", new(5.3f, 0.1f, 0f), new(7.6f, 1.1f, 0f), false),
		("Puzzle 3: torches", new(0f, 0.1f, -3.4f), new(0f, 1.6f, -6f), false),
		("Obby: trap room", new(-6.4f, 1.65f, 16.3f), new(-12f, 1.2f, 16.3f), true),
		("Boulder ramp", new(-17.6f, 1.7f, 16.3f), new(-24f, 4.5f, 16.3f), true),
	};
	private const float PastOffsetX = -200f;

	// Stage numbers, the same order as Stops.
	private const int StagePuzzle2 = 2, StagePuzzle3 = 3, StageObby = 4, StageBoulder = 5;

	private Control _panel;
	private bool _applying;
	private Input.MouseModeEnum _mouseBefore;

	public override void _Ready()
	{
		Layer = 40;
		ProcessMode = ProcessModeEnum.Always;
		Build();
		_panel.Visible = false;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != Key.Z
			|| !(key.CtrlPressed || key.MetaPressed))
			return;
		GetViewport().SetInputAsHandled();
		Toggle(!_panel.Visible);
	}

	private void Toggle(bool open)
	{
		Player player = LocalPlayer();
		if (open && player == null)
			return;
		_panel.Visible = open;
		if (player != null)
			player.InputLocked = open;
		if (open)
		{
			_mouseBefore = Input.MouseMode;
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
		else
			Input.MouseMode = _mouseBefore == Input.MouseModeEnum.Visible ? Input.MouseModeEnum.Captured : _mouseBefore;
	}

	private Player LocalPlayer() =>
		GameState.Instance?.GetPlayerNode(Multiplayer.GetUniqueId()) as Player;

	private void GoTo(int index)
	{
		Toggle(false);
		Rpc(MethodName.ApplyStage, index);
	}

	// On every machine. Fast-forwards through the puzzles before the stage (each machine its own copy, as pickups are
	// local), moves this machine's player, then lets the explorer's machine play the lead-in for real; the other machine
	// gets its share of that cutscene the usual way, over the network.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private async void ApplyStage(int stage)
	{
		if (_applying || GameState.Instance == null)
			return;
		_applying = true;
		Node scene = GetTree().CurrentScene;
		var steps = new System.Collections.Generic.List<System.Action>();
		if (stage >= StagePuzzle2)
			steps.Add(() => (scene.FindChild("TombPuzzle", true, false) as SlotSequence)?.ForceSolve());
		if (stage >= StagePuzzle3)
			steps.Add(() => (scene.FindChild("PyramidPuzzle", true, false) as SlotSequence)?.ForceSolve());
		if (stage >= StageObby)
		{
			steps.Add(() => (scene.FindChild("TorchPuzzle", true, false) as TorchPuzzle)?.ForceSolve());
			steps.Add(() => KeysInDoor(scene));
			steps.Add(() => TileOnAltar(scene));
		}
		// The step whose cutscene leads into the stage: the last key reveal, or the tile (the collapse) before the obby.
		int lead = stage == StagePuzzle2 || stage == StagePuzzle3 || stage == StageObby ? steps.Count - 1 : -1;

		GameState.Instance.SkippingStages = true;
		for (int i = 0; i < steps.Count; i++)
		{
			if (i == lead)
				continue;
			steps[i]();
			// Rewards, locks and openers finish over a frame or two.
			for (int f = 0; f < 4; f++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		GameState.Instance.SkippingStages = false;

		bool present = GameState.Instance.LocalEra == Era.Present;
		if (present && (stage == StagePuzzle3))
			PickUp(scene, "Key");
		// Before the obby the explorer stands at the altar (where they'd place the tile); the cutscene moves both.
		if (stage == StageObby)
		{
			if (present)
				MoveLocalPlayer(new Vector3(0.33f, 1.7f, 15.25f), new Vector3(0f, 2.5f, 16.7f), true);
		}
		else
			MoveLocalPlayer(Stops[stage].At, Stops[stage].LookAt, Stops[stage].PresentOnly);

		if (lead >= 0 && IsDriver())
			steps[lead]();
		_applying = false;
		Events.Instance?.EmitSignal(Events.SignalName.ShowMessage, $"Admin: {Stops[stage].Name}", 2f);
	}

	// The explorer's machine plays the lead-in (as in a real game, where the explorer solves the puzzles); alone as the
	// past player, this one does.
	private bool IsDriver()
	{
		if (GameState.Instance.LocalEra == Era.Present)
			return true;
		foreach (PlayerInfo info in GameState.Instance.Players.Values)
		{
			if (info.Era == Era.Present)
				return false;
		}
		return true;
	}

	private static void KeysInDoor(Node scene)
	{
		if (scene.FindChild("KeyholeLock", true, false) is not KeyholeLock lockNode || lockNode.IsSolved)
			return;
		string[] keys = { "Key", "PyramidKey", "ExitKey" };
		for (int i = 0; i < keys.Length && i < lockNode.Keyholes.Count; i++)
		{
			if (scene.FindChild(keys[i], true, false) is not Pickup key || lockNode.Keyholes[i] == null || lockNode.Keyholes[i].Occupant != null)
				continue;
			TakeOutOfHotbar(key);
			key.Visible = true;
			key.Hold();
			key.PlaceIn(lockNode.Keyholes[i]);
		}
	}

	private static void TileOnAltar(Node scene)
	{
		if (scene.FindChild("Tile", true, false) is not Pickup tile || scene.FindChild("TileSlot", true, false) is not PickupSlot slot
			|| slot.Occupant != null)
			return;
		TakeOutOfHotbar(tile);
		tile.Hold();
		tile.PlaceIn(slot);
	}

	// A piece the explorer is carrying comes out of their hotbar before it's placed.
	private static void TakeOutOfHotbar(Pickup item)
	{
		if (!item.IsHeld || GameState.Instance == null)
			return;
		foreach (Node node in item.GetTree().GetNodesInGroup("players"))
		{
			if (node is not Player player || player.Inventory == null)
				continue;
			for (int i = 0; i < Inventory.SlotCount; i++)
			{
				if (player.Inventory.GetItem(i) != item)
					continue;
				player.Inventory.Select(i);
				if (player.Inventory.SelectedItem != item)
					player.Inventory.Select(i);
				player.Inventory.RemoveSelected();
				return;
			}
		}
	}

	// A key that was revealed earlier goes into the explorer's hotbar, as if they'd picked it up.
	private void PickUp(Node scene, string name)
	{
		if (scene.FindChild(name, true, false) is not Pickup key || key.IsHeld || key.Slot != null)
			return;
		LocalPlayer()?.GiveItem(key);
	}

	private void MoveLocalPlayer(Vector3 at, Vector3 lookAt, bool presentOnly)
	{
		Player player = LocalPlayer();
		Node3D present = null;
		foreach (Node node in GetTree().GetNodesInGroup("era_present"))
			present ??= node as Node3D;
		if (player == null || present == null)
			return;
		Vector3 shift = !presentOnly && GameState.Instance.LocalEra == Era.Past ? new Vector3(PastOffsetX, 0f, 0f) : Vector3.Zero;
		Vector3 from = present.ToGlobal(at) + shift;
		Vector3 target = present.ToGlobal(lookAt) + shift;

		player.GlobalPosition = from;
		player.Velocity = Vector3.Zero;
		Vector3 flat = target - from;
		flat.Y = 0f;
		if (flat.LengthSquared() > 0.0001f)
			player.Rotation = new Vector3(0f, Mathf.Atan2(-flat.X, -flat.Z), 0f);
		if (player.GetNodeOrNull<Camera3D>("Camera") is Camera3D camera)
			camera.Rotation = Vector3.Zero;
	}

	private void Build()
	{
		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(dim);
		_panel = dim;

		var frame = new PanelContainer();
		frame.SetAnchorsPreset(Control.LayoutPreset.Center);
		frame.GrowHorizontal = Control.GrowDirection.Both;
		frame.GrowVertical = Control.GrowDirection.Both;
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.1f, 0.075f, 0.05f, 0.95f),
			BorderColor = new Color(0.62f, 0.47f, 0.25f),
			ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 22, ContentMarginBottom = 22,
		};
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(10);
		frame.AddThemeStyleboxOverride("panel", style);
		dim.AddChild(frame);

		var list = new VBoxContainer();
		list.AddThemeConstantOverride("separation", 10);
		frame.AddChild(list);
		var font = new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } };
		var title = new Label { Text = "Admin: go to", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", 30);
		title.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.4f));
		list.AddChild(title);

		for (int i = 0; i < Stops.Length; i++)
		{
			int index = i;
			var button = new Button { Text = Stops[i].Name, CustomMinimumSize = new Vector2(360f, 44f) };
			button.AddThemeFontOverride("font", font);
			button.AddThemeFontSizeOverride("font_size", 20);
			button.Pressed += () => GoTo(index);
			list.AddChild(button);
		}
		var close = new Button { Text = "Close (Ctrl+Z)", CustomMinimumSize = new Vector2(360f, 36f) };
		close.AddThemeFontSizeOverride("font_size", 16);
		close.Pressed += () => Toggle(false);
		list.AddChild(close);
	}
}
