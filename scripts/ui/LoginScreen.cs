using System;
using Godot;

public partial class LoginScreen : Control
{
	[Export] public PackedScene GameWorldScene = null!;

	private Label _statusLabel = null!;
	private LineEdit _usernameInput = null!;
	private LineEdit _passwordInput = null!;
	private Button _enterWorldButton = null!;
	private Button _quitButton = null!;
	private ISpaceTimeDbAuthService _authService = null!;

	public override void _Ready()
	{
		GD.Print("[UI] LoginScreen ready");
		RuntimeLaunchOptions.ParseFromCommandLine();
		ApplyWindowOptions();

		if (GameWorldScene == null)
		{
			GameWorldScene = GD.Load<PackedScene>("res://scenes/world/GameWorld.tscn");
		}

		EnsureUi();

		_statusLabel = GetNode<Label>("PanelContainer/MarginContainer/VBoxContainer/StatusLabel");
		_usernameInput = GetNode<LineEdit>("PanelContainer/MarginContainer/VBoxContainer/UsernameInput");
		_passwordInput = GetNode<LineEdit>("PanelContainer/MarginContainer/VBoxContainer/PasswordInput");
		_enterWorldButton = GetNode<Button>("PanelContainer/MarginContainer/VBoxContainer/EnterWorldButton");
		_quitButton = GetNode<Button>("PanelContainer/MarginContainer/VBoxContainer/QuitButton");

		Node authNode = GetNodeOrNull<Node>("/root/ServiceRoot/SpaceTimeDbAuthService");
		_authService = authNode as ISpaceTimeDbAuthService;
		if (_authService == null)
		{
			CallDeferred(nameof(EnsureAuthServiceNodeDeferred));
		}

		_enterWorldButton.Pressed += OnEnterWorldPressed;
		_quitButton.Pressed += OnQuitPressed;
		_usernameInput.TextSubmitted += OnCredentialsSubmitted;
		_passwordInput.TextSubmitted += OnCredentialsSubmitted;

		if (_authService != null)
		{
			_ = RefreshServerStatusAsync();
		}
		else
		{
			_statusLabel.Text = "Server: initializing...";
		}
	}

	private async void OnEnterWorldPressed()
	{
		if (_authService == null)
		{
			_statusLabel.Text = "Server: auth service unavailable";
			return;
		}

		SetButtonsDisabled(true);
		_statusLabel.Text = "Server: connecting...";

		string username = _usernameInput.Text.Trim();
		string password = _passwordInput.Text;

		if (string.IsNullOrWhiteSpace(username))
		{
			_statusLabel.Text = "Server: username required";
			SetButtonsDisabled(false);
			return;
		}

		try
		{
			bool connected = await _authService.ConnectAsync();
			if (!connected)
			{
				_statusLabel.Text = "Server: offline";
				return;
			}

			await _authService.LoginAsync(username, password);
			_statusLabel.Text = "Server: online";
			GetTree().ChangeSceneToPacked(GameWorldScene);
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"Server: error ({ex.Message})";
		}
		finally
		{
			SetButtonsDisabled(false);
		}
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}

	private void OnCredentialsSubmitted(string _value)
	{
		OnEnterWorldPressed();
	}

	private async System.Threading.Tasks.Task RefreshServerStatusAsync()
	{
		if (_authService == null)
		{
			_statusLabel.Text = "Server: auth service unavailable";
			return;
		}

		try
		{
			bool connected = await _authService.ConnectAsync();
			_statusLabel.Text = connected ? "Server: online" : "Server: offline";
		}
		catch
		{
			_statusLabel.Text = "Server: offline";
		}
	}

	private void SetButtonsDisabled(bool disabled)
	{
		_enterWorldButton.Disabled = disabled;
		_quitButton.Disabled = disabled;
	}

	private void EnsureUi()
	{
		if (GetNodeOrNull<Control>("PanelContainer") != null)
		{
			return;
		}

		LayoutMode = 3;
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		PanelContainer panel = new PanelContainer { Name = "PanelContainer" };
		panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		panel.OffsetLeft = -220;
		panel.OffsetTop = -130;
		panel.OffsetRight = 220;
		panel.OffsetBottom = 130;
		AddChild(panel);

		MarginContainer margin = new MarginContainer { Name = "MarginContainer" };
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		panel.AddChild(margin);

		VBoxContainer vbox = new VBoxContainer { Name = "VBoxContainer" };
		vbox.AddThemeConstantOverride("separation", 10);
		margin.AddChild(vbox);

		Label title = new Label { Name = "TitleLabel", Text = "Aetheria" };
		vbox.AddChild(title);

		Label status = new Label { Name = "StatusLabel", Text = "Server: checking...", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		vbox.AddChild(status);

		LineEdit user = new LineEdit { Name = "UsernameInput", PlaceholderText = "Username" };
		vbox.AddChild(user);

		LineEdit pass = new LineEdit { Name = "PasswordInput", PlaceholderText = "Password", Secret = true };
		vbox.AddChild(pass);

		Button enter = new Button { Name = "EnterWorldButton", Text = "Enter World" };
		vbox.AddChild(enter);

		Button quit = new Button { Name = "QuitButton", Text = "Quit" };
		vbox.AddChild(quit);
	}

	private void EnsureAuthServiceNodeDeferred()
	{
		Node root = GetTree().Root;
		Node serviceRoot = root.GetNodeOrNull<Node>("ServiceRoot");
		if (serviceRoot == null)
		{
			serviceRoot = new Node { Name = "ServiceRoot" };
			root.AddChild(serviceRoot);
		}

		Node auth = serviceRoot.GetNodeOrNull<Node>("SpaceTimeDbAuthService");
		if (auth == null)
		{
			auth = new SpaceTimeDbAuthService { Name = "SpaceTimeDbAuthService" };
			serviceRoot.AddChild(auth);
		}

		_authService = auth as ISpaceTimeDbAuthService;
		if (_authService != null)
		{
			_ = RefreshServerStatusAsync();
		}
		else
		{
			_statusLabel.Text = "Server: auth service unavailable";
		}
	}

	private void ApplyWindowOptions()
	{
		RuntimeLaunchOptions opts = RuntimeLaunchOptions.Current;
		if (!string.IsNullOrWhiteSpace(opts.InstanceLabel))
		{
			DisplayServer.WindowSetTitle($"Aetheria [{opts.InstanceLabel}]");
		}

		if (opts.WindowWidth.HasValue && opts.WindowHeight.HasValue)
		{
			DisplayServer.WindowSetSize(new Vector2I(opts.WindowWidth.Value, opts.WindowHeight.Value));
		}

		if (opts.WindowX.HasValue && opts.WindowY.HasValue)
		{
			DisplayServer.WindowSetPosition(new Vector2I(opts.WindowX.Value, opts.WindowY.Value));
		}
	}
}
