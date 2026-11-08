using Gamejam.Core;
using Gamejam.Gameplay;
using Godot;

namespace Gamejam.Net;

// A testing shortcut: Ctrl+Z (or Cmd+Z) opens a panel that moves the local player straight to a part of the escape
// room: the start, each puzzle, the trap room (obby) or the boulder ramp. Local only, like moving yourself: each player
// opens their own. The puzzle stops are in the player's own era's room (the rooms are laid out alike, 200 m apart);
// the trap room and the boulder ramp only exist in the present.
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

	private Control _panel;
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
		Player player = LocalPlayer();
		Node3D present = null;
		foreach (Node node in GetTree().GetNodesInGroup("era_present"))
			present ??= node as Node3D;
		if (player == null || present == null)
			return;
		(string _, Vector3 at, Vector3 lookAt, bool presentOnly) = Stops[index];
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
		Toggle(false);
		Events.Instance?.EmitSignal(Events.SignalName.ShowMessage, $"Admin: {Stops[index].Name}", 2f);
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
