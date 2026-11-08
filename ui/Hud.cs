using Gamejam.Core;
using Gamejam.Gameplay;
using Godot;

namespace Gamejam.UI;

// Crosshair, interact prompt and the hotbar for the local player. Only reads Player and Inventory.
// The hotbar shows only the slots holding something (each keeps its number key), inside a gold-edged cartouche.
public partial class Hud : CanvasLayer
{
	[Export] public Texture2D GrabIcon { get; set; }
	[Export] public float SlotSize { get; set; } = 64f;

	// Basalt and sandstone-gold, like the tomb's painted bands.
	private static readonly Color SlotBg = new Color(0.1f, 0.075f, 0.05f, 0.82f);
	private static readonly Color SlotBorder = new Color(0.62f, 0.47f, 0.25f, 0.9f);
	private static readonly Color SelectedBorder = new Color(1f, 0.82f, 0.4f, 1f);
	private static readonly Color Gold = new Color(0.96f, 0.8f, 0.45f);
	private static readonly Color Cream = new Color(0.98f, 0.92f, 0.76f);

	private Player _player;
	private Inventory _inventory;

	private Control _crosshair;
	private TextureRect _grabHand;
	private Label _prompt;
	private Label _selectedName;
	private Label _message;
	private Label _timer;
	private PanelContainer _timerPanel;
	private Tween _messageTween;
	private Control _hotbar;
	private readonly PanelContainer[] _slots = new PanelContainer[Inventory.SlotCount];
	private readonly TextureRect[] _icons = new TextureRect[Inventory.SlotCount];
	private StyleBoxFlat _slotStyle;
	private StyleBoxFlat _selectedStyle;
	private Font _serif;

	public override void _Ready()
	{
		GrabIcon ??= GD.Load<Texture2D>("res://placeholders/ui_hand_grab.svg");
		// Whichever of these the computer has; all are carved-looking serifs.
		_serif = new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } };
		BuildCrosshair();
		BuildHotbar();
		BuildMessage();
		BuildTimer();
		if (Events.Instance != null)
			Events.Instance.ShowMessage += OnShowMessage;
		if (GameState.Instance != null)
		{
			GameState.Instance.GameStarted += OnGameStarted;
			GameState.Instance.Escaped += OnGameEnded;
		}
	}

	public override void _Process(double delta)
	{
		// Players may be spawned after the HUD, so keep looking until the local one shows up.
		if (_player == null || !IsInstanceValid(_player))
			FindLocalPlayer();
		if (GameState.Instance != null && GameState.Instance.IsRunning)
			RefreshTimer();
		// Removed TorchPuzzle related code
	}

	public override void _ExitTree()
	{
		Unbind();
		if (Events.Instance != null)
			Events.Instance.ShowMessage -= OnShowMessage;
		if (GameState.Instance != null)
		{
			GameState.Instance.GameStarted -= OnGameStarted;
			GameState.Instance.Escaped -= OnGameEnded;
		}
	}

	private void OnGameStarted()
	{
		_timerPanel.Visible = true;
		RefreshTimer();
	}

	private void OnGameEnded(double elapsedSeconds)
	{
		_timerPanel.Visible = false;
	}

	private void RefreshTimer()
	{
		double remaining = Mathf.Max(0d, GameState.Instance.TimeLimitSeconds - GameState.Instance.ElapsedSeconds);
		int totalSeconds = Mathf.CeilToInt((float)remaining);
		_timer.Text = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
		_timer.AddThemeColorOverride("font_color", remaining <= 30d ? new Color(1f, 0.43f, 0.3f) : Gold);
	}

	// Events.ShowMessage: fades in, stays for duration seconds, fades out. A new message replaces the old one.
	private void OnShowMessage(string text, float duration)
	{
		_messageTween?.Kill();
		_message.Text = text;
		_message.Modulate = new Color(1f, 1f, 1f, 0f);
		_messageTween = CreateTween();
		_messageTween.TweenProperty(_message, "modulate:a", 1f, 0.3f);
		_messageTween.TweenInterval(duration);
		_messageTween.TweenProperty(_message, "modulate:a", 0f, 0.8f);
	}

	private void BuildMessage()
	{
		_message = new Label
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1f, 1f, 1f, 0f),
		};
		_message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		_message.OffsetTop = 220f;
		AddOutline(_message, 30);
		_message.AddThemeFontOverride("font", _serif);
		_message.AddThemeColorOverride("font_color", Cream);
		_message.AddThemeConstantOverride("outline_size", 8);
		AddChild(_message);
	}

	private void BuildTimer()
	{
		_timerPanel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = GameState.Instance != null && GameState.Instance.IsRunning,
		};
		_timerPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
		_timerPanel.OffsetLeft = 24f;
		_timerPanel.OffsetTop = 24f;

		var frame = new StyleBoxFlat
		{
			BgColor = new Color(0.055f, 0.04f, 0.025f, 0.9f),
			BorderColor = SlotBorder,
			ShadowColor = new Color(0f, 0f, 0f, 0.45f),
			ShadowSize = 8,
		};
		frame.SetBorderWidthAll(2);
		frame.SetCornerRadiusAll(5);
		frame.ContentMarginLeft = 15;
		frame.ContentMarginRight = 15;
		frame.ContentMarginTop = 8;
		frame.ContentMarginBottom = 9;
		_timerPanel.AddThemeStyleboxOverride("panel", frame);
		AddChild(_timerPanel);

		var content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		content.AddThemeConstantOverride("separation", 0);
		_timerPanel.AddChild(content);

		var title = new Label
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Text = "TIME REMAINING",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontOverride("font", _serif);
		title.AddThemeFontSizeOverride("font_size", 11);
		title.AddThemeColorOverride("font_color", Cream);
		content.AddChild(title);

		_timer = new Label
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Text = "5:00",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		_timer.AddThemeFontOverride("font", _serif);
		_timer.AddThemeFontSizeOverride("font_size", 30);
		_timer.AddThemeColorOverride("font_color", Gold);
		AddOutline(_timer, 30);
		content.AddChild(_timer);

		// Removed TorchPuzzle related UI elements
	}

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
			_slots[i].Visible = item != null;
			bool inHand = i == _inventory.SelectedIndex && !_inventory.Holstered;
			_slots[i].AddThemeStyleboxOverride("panel", inHand ? _selectedStyle : _slotStyle);
		}
		Pickup selected = _inventory.SelectedItem;
		_selectedName.Text = selected == null ? "" : selected.ItemName.ToUpperInvariant();
		// Only show the hotbar while carrying something.
		_hotbar.Visible = HasAnyItem();
	}

	private bool HasAnyItem()
	{
		for (int i = 0; i < Inventory.SlotCount; i++)
		{
			if (_inventory.GetItem(i) != null)
				return true;
		}
		return false;
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
		_selectedStyle = MakeSlotStyle(SelectedBorder, 3, glow: true);

		var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
		column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		column.OffsetTop = -(SlotSize + 80f);
		column.OffsetBottom = -16f;
		column.AddThemeConstantOverride("separation", 6);
		column.Visible = false;
		AddChild(column);
		_hotbar = column;

		_selectedName = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
		AddOutline(_selectedName, 20);
		_selectedName.AddThemeFontOverride("font", new FontVariation { BaseFont = _serif, SpacingGlyph = 3 });
		_selectedName.AddThemeColorOverride("font_color", Cream);
		column.AddChild(_selectedName);

		// The cartouche: a rounded, gold-edged frame around the slots, sized to the ones shown.
		var cartouche = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
		var frame = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.045f, 0.03f, 0.55f),
			BorderColor = new Color(0.78f, 0.6f, 0.3f, 0.95f),
			ShadowColor = new Color(0f, 0f, 0f, 0.35f),
			ShadowSize = 6,
		};
		frame.SetBorderWidthAll(2);
		frame.SetCornerRadiusAll(28);
		frame.ContentMarginLeft = 18;
		frame.ContentMarginRight = 18;
		frame.ContentMarginTop = 7;
		frame.ContentMarginBottom = 7;
		cartouche.AddThemeStyleboxOverride("panel", frame);
		column.AddChild(cartouche);

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 8);
		cartouche.AddChild(row);

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
			AddOutline(number, 13);
			number.AddThemeFontOverride("font", _serif);
			number.AddThemeColorOverride("font_color", Gold);
			slot.AddChild(number);

			_slots[i] = slot;
			_icons[i] = icon;
		}
	}

	private static StyleBoxFlat MakeSlotStyle(Color border, int borderWidth, bool glow = false)
	{
		var style = new StyleBoxFlat { BgColor = SlotBg, BorderColor = border };
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(3);
		style.SetContentMarginAll(7);
		// The item in hand glows softly gold.
		if (glow)
		{
			style.ShadowColor = new Color(1f, 0.75f, 0.3f, 0.45f);
			style.ShadowSize = 9;
		}
		return style;
	}

	private static void AddOutline(Label label, int fontSize)
	{
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		label.AddThemeConstantOverride("outline_size", 4);
	}
}
