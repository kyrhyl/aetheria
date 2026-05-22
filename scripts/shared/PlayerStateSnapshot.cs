using Godot;

public readonly struct PlayerStateSnapshot
{
	public string PlayerId { get; }
	public Vector3 Position { get; }
	public Vector3 Velocity { get; }
	public double ServerTimeSeconds { get; }

	public PlayerStateSnapshot(string playerId, Vector3 position, Vector3 velocity, double serverTimeSeconds)
	{
		PlayerId = playerId;
		Position = position;
		Velocity = velocity;
		ServerTimeSeconds = serverTimeSeconds;
	}
}
