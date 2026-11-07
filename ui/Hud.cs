using Gamejam.Gameplay;
using Godot;

namespace Gamejam.UI;

// Crosshair, interact prompt and the 9-slot hotbar for the local player. Only reads Player and Inventory.
public partial class Hud : CanvasLayer
{
	[Export] public Texture2D GrabIcon { get; set; }
	[Export] public float SlotSize { get; set; } = 64f;

	private static readonly Color SlotBg = new Color(0.08f, 0.07f, 0.06f, 0.6f);
	private static readonly Color SlotBorder = new Color(0.55f, 0.5f, 0.42f, 0.8f);
	private static readonly Color SelectedBorder = new Color(1f, 0.85f, 0.45f, 1f);

	private Player _player;
	private Inventory _inventory;

	private Control _crosshair;
	private TextureRect _grabHand;
	private Label _prompt;
	private Label _selectedName;
	private readonly PanelContainer[] _slots = new PanelContainer[Inventory.SlotCount];
	private readonly TextureRect[] _icons = new TextureRect[Inventory.SlotCount];
	private StyleBoxFlat _slotStyle;
	private StyleBoxFlat _selectedStyle;

	public override void _Ready()
	{
		GrabIcon ??= GD.Load<Texture2D>("res://placeholders/ui_hand_grab.svg");
		BuildCrosshair();
		BuildHotbar();
	}

	public override void _Process(double delta)
	{
		// Players may be spawned after the HUD, so keep looking until the local one shows up.
		if (_player == null || !IsInstanceValid(_player))
			FindLocalPlayer();
	}

	public override void _ExitTree() => Unbind();

	private void FindLocalPlayer()
	{
		foreach (Node node in GetTree().GetNodesInGroup("players"))
		{
			if (node is Player player && player.IsMultiplayerAuthority() && player.Inventory != null)
			{
				Bind(player);
				return;
			}
		}
	}

	private void Bind(Player player)
	{
		Unbind();
		_player = player;
		_inventory = player.Inventory;
		_player.InteractTargetChanged += OnInteractTargetChanged;
		_inventory.Changed += RefreshHotbar;
		OnInteractTargetChanged("");
		RefreshHotbar();
	}

	private void Unbind()
	{
		if (_player != null && IsInstanceValid(_player))
			_player.InteractTargetChanged -= OnInteractTargetChanged;
		if (_inventory != null && IsInstanceValid(_inventory))
			_inventory.Changed -= RefreshHotbar;
		_player = null;
		_inventory = null;
	}

	private void OnInteractTargetChanged(string prompt)
	{
		bool grab = _player != null && _player.IsLookingAtPickup;
		_grabHand.Visible = grab;
		_crosshair.Visible = !grab;
		_prompt.Text = prompt == "" ? "" : $"[F] {prompt}";
	}

	private void RefreshHotbar()
	{
		for (int i = 0; i < Inventory.SlotCount; i++)
		{
			Pickup item = _inventory.GetItem(i);
			_icons[i].Texture = item?.Icon;
			_slots[i].AddThemeStyleboxOverride("panel", i == _inventory.SelectedIndex ? _selectedStyle : _slotStyle);
		}
		Pickup selected = _inventory.SelectedItem;
		_selectedName.Text = selected == null ? "" : selected.ItemName;
	}

	private void BuildCrosshair()
	{
		var center = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(Control.LayoutPreset.Center);
		AddChild(center);

		var dotStyle = new StyleBoxFlat
		{
			BgColor = new Color(1f, 1f, 1f, 0.9f),
			BorderColor = new Color(0f, 0f, 0f, 0.6f),
		};
		dotStyle.SetBorderWidthAll(1);
		dotStyle.SetCornerRadiusAll(4);
		var dot = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(8, 8), Position = new Vector2(-4, -4) };
		dot.AddThemeStyleboxOverride("panel", dotStyle);
		center.AddChild(dot);
		_crosshair = dot;

		_grabHand = new TextureRect
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Texture = GrabIcon,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Size = new Vector2(40, 40),
			Position = new Vector2(-20, -20),
			Visible = false,
		};
		center.AddChild(_grabHand);

		_prompt = new Label
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			Size = new Vector2(400, 30),
			Position = new Vector2(-200, 28),
		};
		AddOutline(_prompt, 18);
		center.AddChild(_prompt);
	}

	private void BuildHotbar()
	{
		_slotStyle = MakeSlotStyle(SlotBorder, 2);
		_selectedStyle = MakeSlotStyle(SelectedBorder, 4);

		var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
		column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		column.OffsetTop = -(SlotSize + 60f);
		column.OffsetBottom = -16f;
		column.AddThemeConstantOverride("separation", 6);
		AddChild(column);

		_selectedName = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
		AddOutline(_selectedName, 18);
		column.AddChild(_selectedName);

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 4);
		column.AddChild(row);

		for (int i = 0; i < Inventory.SlotCount; i++)
		{
			var slot = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(SlotSize, SlotSize) };
			slot.AddThemeStyleboxOverride("panel", _slotStyle);
			row.AddChild(slot);

			var icon = new TextureRect
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			};
			slot.AddChild(icon);

			var number = new Label
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Text = (i + 1).ToString(),
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
				SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			};
			AddOutline(number, 12);
			slot.AddChild(number);

			_slots[i] = slot;
			_icons[i] = icon;
		}
	}

	private static StyleBoxFlat MakeSlotStyle(Color border, int borderWidth)
	{
		var style = new StyleBoxFlat { BgColor = SlotBg, BorderColor = border };
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(6);
		style.SetContentMarginAll(6);
		return style;
	}

	private static void AddOutline(Label label, int fontSize)
	{
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		label.AddThemeConstantOverride("outline_size", 4);
	}
}
