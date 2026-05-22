using System;
using Godot;

public sealed class RuntimeLaunchOptions
{
	public string Profile { get; private set; } = "default";
	public string InstanceLabel { get; private set; } = "";
	public int? WindowX { get; private set; }
	public int? WindowY { get; private set; }
	public int? WindowWidth { get; private set; }
	public int? WindowHeight { get; private set; }

	public static RuntimeLaunchOptions Current { get; private set; } = new RuntimeLaunchOptions();

	public static void ParseFromCommandLine()
	{
		RuntimeLaunchOptions options = new RuntimeLaunchOptions();
		string[] args = OS.GetCmdlineArgs();

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (!arg.StartsWith("--", StringComparison.Ordinal))
			{
				continue;
			}

			string key = arg;
			string value = "";
			int eq = arg.IndexOf('=');
			if (eq > 0)
			{
				key = arg[..eq];
				value = arg[(eq + 1)..];
			}
			else if (i + 1 < args.Length)
			{
				value = args[i + 1];
				i++;
			}

			switch (key)
			{
				case "--profile":
					if (!string.IsNullOrWhiteSpace(value))
					{
						options.Profile = value.Trim();
					}
					break;
				case "--instance":
					options.InstanceLabel = value.Trim();
					break;
				case "--x":
					if (int.TryParse(value, out int x)) options.WindowX = x;
					break;
				case "--y":
					if (int.TryParse(value, out int y)) options.WindowY = y;
					break;
				case "--w":
					if (int.TryParse(value, out int w)) options.WindowWidth = w;
					break;
				case "--h":
					if (int.TryParse(value, out int h)) options.WindowHeight = h;
					break;
			}
		}

		Current = options;
	}
}
