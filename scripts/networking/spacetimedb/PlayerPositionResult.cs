using Godot;

public readonly struct PlayerPositionResult
{
	public bool Found { get; }
	public Vector3 Position { get; }

	public PlayerPositionResult(bool found, Vector3 position)
	{
		Found = found;
		Position = position;
	}
}
