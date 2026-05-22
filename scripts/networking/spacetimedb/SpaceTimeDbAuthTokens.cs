public readonly struct SpaceTimeDbAuthTokens
{
	public string AccessToken { get; }
	public string RefreshToken { get; }

	public SpaceTimeDbAuthTokens(string accessToken, string refreshToken)
	{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
	}
}
