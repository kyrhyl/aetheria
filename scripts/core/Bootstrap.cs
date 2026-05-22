using Godot;

public partial class Bootstrap : Node
{
	[Export] public PackedScene LoginScene = null!;

	public override void _Ready()
	{
		CallDeferred(nameof(RunBootstrap));
	}

	private void RunBootstrap()
	{
		RuntimeLaunchOptions.ParseFromCommandLine();
		ApplyWindowOptions();
		EnsureServiceRoot();
		PackedScene scene = LoginScene ?? GD.Load<PackedScene>("res://scenes/ui/LoginScreen.tscn");
		if (scene != null)
		{
			GetTree().ChangeSceneToPacked(scene);
		}
		else
		{
			GD.PushWarning("[Bootstrap] Login scene missing in export. Using runtime fallback UI.");
			Node root = GetTree().Root;
			LoginScreen fallback = new LoginScreen { Name = "LoginScreen" };
			root.AddChild(fallback);
			GetTree().CurrentScene = fallback;
			QueueFree();
		}
		GD.Print("Aetheria bootstrap ready.");
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

	private void EnsureServiceRoot()
	{
		Node serviceRoot = GetTree().Root.GetNodeOrNull<Node>("ServiceRoot") ?? CreateServiceRoot();

		if (serviceRoot.GetNodeOrNull<SpaceTimeDbAuthService>("SpaceTimeDbAuthService") == null)
		{
			SpaceTimeDbAuthService authService = new SpaceTimeDbAuthService
			{
				Name = "SpaceTimeDbAuthService"
			};
			serviceRoot.AddChild(authService);
		}

		if (serviceRoot.GetNodeOrNull<PlayerReplicationService>("PlayerReplicationService") == null)
		{
			PlayerReplicationService replicationService = new PlayerReplicationService
			{
				Name = "PlayerReplicationService"
			};
			serviceRoot.AddChild(replicationService);
		}
	}

	private Node CreateServiceRoot()
	{
		Node serviceRoot = new Node
		{
			Name = "ServiceRoot"
		};
		GetTree().Root.AddChild(serviceRoot);
		return serviceRoot;
	}
}
