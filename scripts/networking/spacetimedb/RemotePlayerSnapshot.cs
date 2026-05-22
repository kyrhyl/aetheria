using Godot;

public sealed class RemotePlayerSnapshot
{
	public string Identity { get; set; } = string.Empty;
	public string Profile { get; set; } = string.Empty;
	public string DisplayName { get; set; } = "Player";
	public Vector3 Position { get; set; } = Vector3.Zero;
}
