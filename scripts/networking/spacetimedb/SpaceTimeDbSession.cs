public readonly struct SpaceTimeDbSession
{
	public string Identity { get; }
	public SpaceTimeDbAuthTokens Tokens { get; }

	public SpaceTimeDbSession(string identity, SpaceTimeDbAuthTokens tokens)
	{
		Identity = identity;
		Tokens = tokens;
	}
}
