using Gamejam.Core;
using Godot;

namespace Gamejam.Net;

// Bare-bones host/join menu plus a status line. Agent C may restyle or replace it,
// as long as HostPressed/JoinPressed and the Show* methods keep working.
public partial class Lobby : CanvasLayer
{
	[Signal] public delegate void HostPressedEventHandler(Era era);
	[Signal] public delegate void JoinPressedEventHandler(string address);

	private Control _menu;
	private OptionButton _hostEra;
	private LineEdit _address;
	private Button _hostButton;
	private Button _joinButton;
	private Label _menuStatus;
	private Label _banner;

	public override void _Ready()
	{
		// Above the HUD.
		Layer = 10;
		Build();

		GameState.Instance.GameStarted += OnGameStarted;
		GameState.Instance.Escaped += OnEscaped;
		ShowMenu("");
	}

	public override void _ExitTree()
	{
		GameState.Instance.GameStarted -= OnGameStarted;
		GameState.Instance.Escaped -= OnEscaped;
	}

	public void ShowMenu(string status, bool interactive = true)
	{
		_menu.Visible = true;
		_banner.Visible = false;
		_menuStatus.Text = status;
		_hostButton.Disabled = !interactive;
		_joinButton.Disabled = !interactive;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	// Menu hidden, message along the top so the player can already walk around.
	public void ShowWaiting(string status)
	{
		_menu.Visible = false;
		_banner.Visible = true;
		_banner.Text = status;
	}

	// net/Intro.cs takes over the screen from here.
	private void OnGameStarted() => _banner.Visible = false;

	private void OnEscaped(double elapsedSeconds)
	{
		var time = System.TimeSpan.FromSeconds(elapsedSeconds);
		ShowWaiting($"Escaped in {(int)time.TotalMinutes}:{time.Seconds:00}!");
	}

	private void Build()
	{
		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);
		_menu = center;

		var panel = new PanelContainer();
		center.AddChild(panel);
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);

		box.AddChild(new Label { Text = "Escape Room", HorizontalAlignment = HorizontalAlignment.Center });

		box.AddChild(new Label { Text = "Host plays in:" });
		_hostEra = new OptionButton();
		_hostEra.AddItem("Present (ruined tomb)", (int)Era.Present);
		_hostEra.AddItem("Past (intact tomb)", (int)Era.Past);
		box.AddChild(_hostEra);
		_hostButton = new Button { Text = "Host" };
		_hostButton.Pressed += () =>
			EmitSignal(SignalName.HostPressed, Variant.From((Era)_hostEra.GetSelectedId()));
		box.AddChild(_hostButton);

		box.AddChild(new HSeparator());

		box.AddChild(new Label { Text = "Host IP:" });
		_address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "e.g. 192.168.1.20" };
		box.AddChild(_address);
		_joinButton = new Button { Text = "Join" };
		_joinButton.Pressed += () =>
		{
			string address = _address.Text.StripEdges();
			EmitSignal(SignalName.JoinPressed, address == "" ? "127.0.0.1" : address);
		};
		box.AddChild(_joinButton);

		_menuStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_menuStatus);

		_banner = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_banner.OffsetTop = 24;
		_banner.AddThemeFontSizeOverride("font_size", 22);
		_banner.AddThemeColorOverride("font_outline_color", Colors.Black);
		_banner.AddThemeConstantOverride("outline_size", 6);
		AddChild(_banner);
	}
}
