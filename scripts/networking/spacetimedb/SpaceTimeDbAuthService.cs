using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;

public partial class SpaceTimeDbAuthService : Node, ISpaceTimeDbAuthService
{
	[Export] public SpaceTimeDbNetworkConfig NetworkConfig = null!;

	public bool IsServerConnected { get; private set; }
	public bool IsUsingLocalDevAuth { get; private set; }
	public SpaceTimeDbSession CurrentSession { get; private set; }

	private ISpaceTimeDbTransport _transport = null!;
	private string _tokenCachePath = "user://spacetimedb_token_default.txt";
	private string _lastBoundProfile = string.Empty;
	private DateTime _lastBoundProfileAtUtc = DateTime.MinValue;

	public override void _Ready()
	{
		if (NetworkConfig == null)
		{
			NetworkConfig = ResourceLoader.Load<SpaceTimeDbNetworkConfig>("res://data/configs/spacetimedb.network.tres");
		}

		if (NetworkConfig == null)
		{
			throw new InvalidOperationException("SpaceTimeDbNetworkConfig is missing.");
		}

		CurrentSession = new SpaceTimeDbSession(string.Empty, new SpaceTimeDbAuthTokens(string.Empty, string.Empty));
		_transport = new SpaceTimeDbHttpTransport(NetworkConfig);
		_tokenCachePath = $"user://spacetimedb_token_{SanitizeFileSegment(RuntimeLaunchOptions.Current.Profile)}.txt";
		LogRuntimeConfig();
	}

	public override void _ExitTree()
	{
		if (_transport is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}

	public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
	{
		IsUsingLocalDevAuth = false;
		IsServerConnected = await _transport.PingAsync(cancellationToken);
		if (IsServerConnected)
		{
			GD.Print($"[SpaceTimeDB] Connected to {NetworkConfig.ServerUrl}/{NetworkConfig.DatabaseName}");
		}
		else if (NetworkConfig.EnableLocalDevAuth && NetworkConfig.AllowLocalDevFallbackOnPingFailure)
		{
			IsServerConnected = true;
			IsUsingLocalDevAuth = true;
			GD.PushWarning("[SpaceTimeDB] Ping failed; using local dev auth mode.");
		}
		else
		{
			GD.PushWarning($"[SpaceTimeDB] Ping failed for {NetworkConfig.ServerUrl}/{NetworkConfig.DatabaseName} (fallback disabled)");
		}

		return IsServerConnected;
	}

	public async Task<string> TracePingAsync(CancellationToken cancellationToken = default)
	{
		string result = await _transport.TracePingAsync(cancellationToken);
		GD.Print($"[SpaceTimeDB] Trace ping: {result}");
		return result;
	}

	public async Task<SpaceTimeDbSession> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
	{
		if (!IsServerConnected)
		{
			throw new InvalidOperationException("SpaceTimeDB client is not connected.");
		}

		if (IsUsingLocalDevAuth)
		{
			if (string.IsNullOrWhiteSpace(username))
			{
				throw new ArgumentException("Username is required.", nameof(username));
			}

			if (username == NetworkConfig.DevUsername && password == NetworkConfig.DevPassword)
			{
				SpaceTimeDbAuthTokens devTokens = new SpaceTimeDbAuthTokens(
					$"dev-access-{username}",
					$"dev-refresh-{username}"
				);
				CurrentSession = new SpaceTimeDbSession(username, devTokens);
				GD.Print($"[SpaceTimeDB] Local dev login success for {CurrentSession.Identity}");
				return CurrentSession;
			}

			throw new InvalidOperationException("Invalid dev credentials.");
		}

		string cachedToken = LoadCachedToken();
		if (!string.IsNullOrWhiteSpace(cachedToken))
		{
			string cachedIdentity = IdentityFromToken(cachedToken);
			if (!string.IsNullOrWhiteSpace(cachedIdentity))
			{
				CurrentSession = new SpaceTimeDbSession(cachedIdentity, new SpaceTimeDbAuthTokens(cachedToken, cachedToken));
				await BindUsernameAsync(username, cancellationToken);
				await BindRuntimeProfileAsync(cancellationToken);
				GD.Print($"[SpaceTimeDB] Reused cached token for {cachedIdentity}");
				return CurrentSession;
			}
		}

		if (string.IsNullOrWhiteSpace(username))
		{
			throw new ArgumentException("Username is required.", nameof(username));
		}

		SpaceTimeDbHandshakeResult handshake = await _transport.LoginAsync(username, password, cancellationToken);
		CurrentSession = new SpaceTimeDbSession(handshake.Identity, handshake.Tokens);
		await BindUsernameAsync(username, cancellationToken);
		await BindRuntimeProfileAsync(cancellationToken);
		SaveCachedToken(CurrentSession.Tokens.AccessToken);
		await TryWriteLoginAuditAsync(username, cancellationToken);
		GD.Print($"[SpaceTimeDB] Login success for {CurrentSession.Identity}");
		return CurrentSession;
	}

	public async Task<PlayerPositionResult> GetSavedPlayerPositionAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(CurrentSession.Identity) || string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken))
		{
			return new PlayerPositionResult(false, Vector3.Zero);
		}

		string sql = $"select x, y, z from player_profile where identity = '0x{CurrentSession.Identity}' limit 1;";
		string content = await _transport.ExecuteSqlAsync(CurrentSession.Tokens.AccessToken, sql, cancellationToken);
		using JsonDocument doc = JsonDocument.Parse(content);
		JsonElement root = doc.RootElement;
		if (root.GetArrayLength() == 0)
		{
			return new PlayerPositionResult(false, Vector3.Zero);
		}

		JsonElement rows = root[0].GetProperty("rows");
		if (rows.GetArrayLength() == 0)
		{
			return new PlayerPositionResult(false, Vector3.Zero);
		}

		JsonElement row = rows[0];
		Vector3 pos = new Vector3(
			row[0].GetSingle(),
			row[1].GetSingle(),
			row[2].GetSingle()
		);
		return new PlayerPositionResult(true, pos);
	}

	public async Task<Dictionary<string, Vector3>> GetAllPlayerPositionsAsync(CancellationToken cancellationToken = default)
	{
		Dictionary<string, Vector3> players = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken))
		{
			return players;
		}

		await BindRuntimeProfileAsync(cancellationToken);

		string currentProfile = RuntimeLaunchOptions.Current.Profile?.Trim() ?? string.Empty;
		string escapedProfile = currentProfile.Replace("'", "''");
		string sql = $"select identity, x, y, z from player_presence where profile <> '' and profile <> '{escapedProfile}';";
		string content = await _transport.ExecuteSqlAsync(CurrentSession.Tokens.AccessToken, sql, cancellationToken);
		using JsonDocument doc = JsonDocument.Parse(content);
		JsonElement root = doc.RootElement;
		if (root.GetArrayLength() == 0)
		{
			return players;
		}

		JsonElement rows = root[0].GetProperty("rows");
		for (int i = 0; i < rows.GetArrayLength(); i++)
		{
			JsonElement row = rows[i];
			if (row.GetArrayLength() < 4)
			{
				continue;
			}

			string identity = ReadIdentityCell(row[0]);
			if (identity.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				identity = identity[2..];
			}

			if (string.IsNullOrWhiteSpace(identity))
			{
				continue;
			}

			Vector3 pos = new Vector3(
				row[1].GetSingle(),
				row[2].GetSingle(),
				row[3].GetSingle()
			);

			players[identity] = pos;
		}

		return players;
	}

	private static string ReadIdentityCell(JsonElement identityCell)
	{
		if (identityCell.ValueKind == JsonValueKind.String)
		{
			return identityCell.GetString()?.Trim() ?? string.Empty;
		}

		if (identityCell.ValueKind == JsonValueKind.Object)
		{
			if (identityCell.TryGetProperty("0", out JsonElement zero) && zero.ValueKind == JsonValueKind.String)
			{
				return zero.GetString()?.Trim() ?? string.Empty;
			}

			if (identityCell.TryGetProperty("hex_identity", out JsonElement hexIdentity) && hexIdentity.ValueKind == JsonValueKind.String)
			{
				return hexIdentity.GetString()?.Trim() ?? string.Empty;
			}
		}

		return identityCell.ToString().Trim();
	}

	public async Task SavePlayerPositionAsync(Vector3 position, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken))
		{
			return;
		}

		await BindRuntimeProfileAsync(cancellationToken);

		await _transport.CallReducerAsync(
			CurrentSession.Tokens.AccessToken,
			"save_player_position_for_profile",
			new { profile = RuntimeLaunchOptions.Current.Profile, x = position.X, y = position.Y, z = position.Z },
			cancellationToken
		);
	}

	private async Task BindRuntimeProfileAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken))
		{
			return;
		}

		string profile = string.IsNullOrWhiteSpace(RuntimeLaunchOptions.Current.Profile)
			? "default"
			: RuntimeLaunchOptions.Current.Profile.Trim();

		if (string.Equals(profile, _lastBoundProfile, StringComparison.Ordinal) &&
			(DateTime.UtcNow - _lastBoundProfileAtUtc).TotalSeconds < 2.0)
		{
			return;
		}

		await _transport.CallReducerAsync(
			CurrentSession.Tokens.AccessToken,
			"set_player_profile",
			new { profile },
			cancellationToken
		);

		_lastBoundProfile = profile;
		_lastBoundProfileAtUtc = DateTime.UtcNow;
	}

	private async Task BindUsernameAsync(string username, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken) || string.IsNullOrWhiteSpace(username))
		{
			return;
		}

		await _transport.CallReducerAsync(
			CurrentSession.Tokens.AccessToken,
			"set_player_username",
			new { username = username.Trim() },
			cancellationToken
		);
	}

	public async Task<SpaceTimeDbSession> RefreshSessionAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(CurrentSession.Identity) || string.IsNullOrWhiteSpace(CurrentSession.Tokens.RefreshToken))
		{
			throw new InvalidOperationException("No active session to refresh.");
		}

		SpaceTimeDbAuthTokens refreshedTokens = await _transport.RefreshAsync(CurrentSession.Tokens.RefreshToken, cancellationToken);
		CurrentSession = new SpaceTimeDbSession(CurrentSession.Identity, refreshedTokens);
		GD.Print($"[SpaceTimeDB] Session refreshed for {CurrentSession.Identity}");
		return CurrentSession;
	}

	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		if (!IsServerConnected)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(CurrentSession.Tokens.AccessToken))
		{
			await _transport.LogoutAsync(CurrentSession.Tokens.AccessToken, cancellationToken);
		}
		CurrentSession = new SpaceTimeDbSession(string.Empty, new SpaceTimeDbAuthTokens(string.Empty, string.Empty));
		if (IsUsingLocalDevAuth)
		{
			IsServerConnected = false;
		}
		IsUsingLocalDevAuth = false;
		GD.Print("[SpaceTimeDB] Logout success");
	}

	private void LogRuntimeConfig()
	{
		string resourcePath = string.IsNullOrWhiteSpace(NetworkConfig.ResourcePath) ? "<runtime>" : NetworkConfig.ResourcePath;
		GD.Print($"[SpaceTimeDB] Config loaded: {resourcePath}");
		GD.Print($"[SpaceTimeDB] ServerUrl={NetworkConfig.ServerUrl}, DatabaseName={NetworkConfig.DatabaseName}");
		GD.Print($"[SpaceTimeDB] Endpoints ping={NetworkConfig.PingEndpointTemplate}, login={NetworkConfig.LoginEndpointTemplate}, refresh={NetworkConfig.RefreshEndpointTemplate}, logout={NetworkConfig.LogoutEndpointTemplate}");
		GD.Print($"[SpaceTimeDB] Reducer call template={NetworkConfig.ReducerCallEndpointTemplate}, loginAuditReducer={NetworkConfig.LoginAuditReducerName}");
		GD.Print($"[SpaceTimeDB] LocalDevAuth enabled={NetworkConfig.EnableLocalDevAuth}, allowPingFallback={NetworkConfig.AllowLocalDevFallbackOnPingFailure}");
		GD.Print($"[SpaceTimeDB] Profile={RuntimeLaunchOptions.Current.Profile}, tokenCache={_tokenCachePath}");
	}

	private async Task TryWriteLoginAuditAsync(string username, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(NetworkConfig.LoginAuditReducerName))
		{
			return;
		}

		string marker = $"login:{username}:{DateTime.UtcNow:O}";
		try
		{
			await _transport.CallReducerAsync(
				CurrentSession.Tokens.AccessToken,
				NetworkConfig.LoginAuditReducerName,
				new { name = marker },
				cancellationToken
			);
			GD.Print($"[SpaceTimeDB] Login audit reducer called with marker '{marker}'");
		}
		catch (Exception ex)
		{
			if (ex.Message.Contains("(415)", StringComparison.Ordinal))
			{
				GD.Print("[SpaceTimeDB] Login audit reducer skipped: endpoint rejected JSON content type.");
				return;
			}

			GD.PushWarning($"[SpaceTimeDB] Login audit reducer call failed: {ex.Message}");
		}
	}

	private string LoadCachedToken()
	{
		string path = ProjectSettings.GlobalizePath(_tokenCachePath);
		if (!File.Exists(path))
		{
			return string.Empty;
		}

		return File.ReadAllText(path).Trim();
	}

	private void SaveCachedToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return;
		}

		string path = ProjectSettings.GlobalizePath(_tokenCachePath);
		File.WriteAllText(path, token);
	}

	private void ClearCachedToken()
	{
		string path = ProjectSettings.GlobalizePath(_tokenCachePath);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private string IdentityFromToken(string token)
	{
		try
		{
			string[] parts = token.Split('.');
			if (parts.Length < 2)
			{
				return string.Empty;
			}

			byte[] payloadBytes = Convert.FromBase64String(PadBase64(parts[1].Replace('-', '+').Replace('_', '/')));
			string payloadJson = Encoding.UTF8.GetString(payloadBytes);
			using JsonDocument doc = JsonDocument.Parse(payloadJson);
			if (doc.RootElement.TryGetProperty("hex_identity", out JsonElement identityEl))
			{
				return identityEl.GetString() ?? string.Empty;
			}
		}
		catch
		{
			return string.Empty;
		}

		return string.Empty;
	}

	private string PadBase64(string base64)
	{
		int pad = 4 - (base64.Length % 4);
		if (pad is > 0 and < 4)
		{
			return base64 + new string('=', pad);
		}

		return base64;
	}

	private string SanitizeFileSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "default";
		}

		char[] chars = value.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
			{
				chars[i] = '_';
			}
		}

		return new string(chars);
	}
}
