public readonly struct SpaceTimeDbHandshakeResult
{
	public string Identity { get; }
	public SpaceTimeDbAuthTokens Tokens { get; }

	public SpaceTimeDbHandshakeResult(string identity, SpaceTimeDbAuthTokens tokens)
	{
		Identity = identity;
		Tokens = tokens;
	}
}
