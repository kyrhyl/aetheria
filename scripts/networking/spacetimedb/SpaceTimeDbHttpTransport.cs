using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class SpaceTimeDbHttpTransport : ISpaceTimeDbTransport, IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly string _pingPath;
	private readonly string _loginPath;
	private readonly string _reducerCallTemplate;
	private readonly string _sqlPath;
	private readonly string _refreshPath;
	private readonly string _logoutPath;
	private readonly int _retryCount;
	private readonly float _retryBackoffSeconds;

	public SpaceTimeDbHttpTransport(SpaceTimeDbNetworkConfig config)
	{
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(config.ServerUrl.TrimEnd('/') + "/"),
			Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds))
		};

		_pingPath = BuildPath(config.PingEndpointTemplate, config.DatabaseName);
		_loginPath = BuildPath(config.LoginEndpointTemplate, config.DatabaseName);
		_reducerCallTemplate = BuildPath(config.ReducerCallEndpointTemplate, config.DatabaseName);
		_sqlPath = BuildPath("v1/database/{db}/sql", config.DatabaseName);
		_refreshPath = BuildPath(config.RefreshEndpointTemplate, config.DatabaseName);
		_logoutPath = BuildPath(config.LogoutEndpointTemplate, config.DatabaseName);
		_retryCount = Math.Max(0, config.RetryCount);
		_retryBackoffSeconds = Math.Max(0, config.RetryBackoffSeconds);
	}

	public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _pingPath);
		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		return response.IsSuccessStatusCode;
	}

	public async Task<string> TracePingAsync(CancellationToken cancellationToken = default)
	{
		var payload = new
		{
			traceId = Guid.NewGuid().ToString("N"),
			clientTimeUtc = DateTime.UtcNow.ToString("O")
		};

		string body = JsonSerializer.Serialize(payload);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _loginPath)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		string content = await response.Content.ReadAsStringAsync(cancellationToken);
		string compact = content.Length > 140 ? content[..140] + "..." : content;
		return $"{(int)response.StatusCode} {response.StatusCode}: {compact}";
	}

	public async Task<SpaceTimeDbHandshakeResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
	{
		var payload = new
		{
			username,
			password
		};

		string body = JsonSerializer.Serialize(payload);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _loginPath)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		string content = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Login request failed ({(int)response.StatusCode}): {content}");
		}

		SpaceTimeDbHandshakeDto dto = JsonSerializer.Deserialize<SpaceTimeDbHandshakeDto>(content, JsonOptions())
			?? throw new InvalidOperationException("Login response was empty.");

		string accessToken = string.IsNullOrWhiteSpace(dto.AccessToken) ? dto.Token : dto.AccessToken;

		if (string.IsNullOrWhiteSpace(dto.Identity) || string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("Login response missing identity or access token.");
		}

		return new SpaceTimeDbHandshakeResult(
			dto.Identity,
			new SpaceTimeDbAuthTokens(accessToken, string.IsNullOrWhiteSpace(dto.RefreshToken) ? accessToken : dto.RefreshToken)
		);
	}

	public async Task<SpaceTimeDbAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(_refreshPath))
		{
			return new SpaceTimeDbAuthTokens(refreshToken, refreshToken);
		}

		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			throw new InvalidOperationException("Refresh token is required.");
		}

		var payload = new
		{
			refreshToken
		};

		string body = JsonSerializer.Serialize(payload);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _refreshPath)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		string content = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Refresh request failed ({(int)response.StatusCode}): {content}");
		}

		SpaceTimeDbTokenDto dto = JsonSerializer.Deserialize<SpaceTimeDbTokenDto>(content, JsonOptions())
			?? throw new InvalidOperationException("Refresh response was empty.");

		if (string.IsNullOrWhiteSpace(dto.AccessToken))
		{
			throw new InvalidOperationException("Refresh response missing access token.");
		}

		return new SpaceTimeDbAuthTokens(dto.AccessToken, string.IsNullOrWhiteSpace(dto.RefreshToken) ? refreshToken : dto.RefreshToken);
	}

	public async Task CallReducerAsync(string accessToken, string reducerName, object args, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("Access token is required for reducer call.");
		}

		if (string.IsNullOrWhiteSpace(reducerName))
		{
			throw new InvalidOperationException("Reducer name is required.");
		}

		string path = _reducerCallTemplate.Replace("{reducer}", Uri.EscapeDataString(reducerName));
		string body = JsonSerializer.Serialize(args);
		byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
		ByteArrayContent jsonContent = new ByteArrayContent(bodyBytes);
		jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = jsonContent
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string content = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new InvalidOperationException($"Reducer call failed ({(int)response.StatusCode}): {content}");
		}
	}

	public async Task<string> ExecuteSqlAsync(string accessToken, string sql, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("Access token is required for SQL call.");
		}

		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _sqlPath)
		{
			Content = new StringContent(sql, Encoding.UTF8, "text/plain")
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		string content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"SQL request failed ({(int)response.StatusCode}): {content}");
		}

		return content;
	}

	public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(_logoutPath))
		{
			return;
		}

		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _logoutPath);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string content = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new InvalidOperationException($"Logout request failed ({(int)response.StatusCode}): {content}");
		}
	}

	public void Dispose()
	{
		_httpClient.Dispose();
	}

	private static string BuildPath(string template, string databaseName)
	{
		return template.Replace("{db}", Uri.EscapeDataString(databaseName));
	}

	private static JsonSerializerOptions JsonOptions()
	{
		return new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
	}

	private sealed class SpaceTimeDbHandshakeDto
	{
		public string Identity { get; set; } = string.Empty;
		public string AccessToken { get; set; } = string.Empty;
		public string Token { get; set; } = string.Empty;
		public string RefreshToken { get; set; } = string.Empty;
	}

	private sealed class SpaceTimeDbTokenDto
	{
		public string AccessToken { get; set; } = string.Empty;
		public string RefreshToken { get; set; } = string.Empty;
	}

	private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		for (int attempt = 0; ; attempt++)
		{
			HttpRequestMessage clonedRequest = await CloneRequestAsync(request, cancellationToken);
			try
			{
				HttpResponseMessage response = await _httpClient.SendAsync(clonedRequest, cancellationToken);
				if ((int)response.StatusCode >= 500 && attempt < _retryCount)
				{
					response.Dispose();
					await Task.Delay(BackoffMs(attempt), cancellationToken);
					continue;
				}

				return response;
			}
			catch (HttpRequestException) when (attempt < _retryCount)
			{
				await Task.Delay(BackoffMs(attempt), cancellationToken);
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _retryCount)
			{
				await Task.Delay(BackoffMs(attempt), cancellationToken);
			}
		}
	}

	private int BackoffMs(int attempt)
	{
		double factor = Math.Pow(2, attempt);
		return (int)(_retryBackoffSeconds * 1000 * factor);
	}

	private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		HttpRequestMessage clone = new HttpRequestMessage(request.Method, request.RequestUri);

		foreach (var header in request.Headers)
		{
			clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		if (request.Content != null)
		{
			byte[] contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
			ByteArrayContent contentClone = new ByteArrayContent(contentBytes);
			foreach (var header in request.Content.Headers)
			{
				contentClone.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}

			clone.Content = contentClone;
		}

		return clone;
	}
}
