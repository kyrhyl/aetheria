using System.Threading;
using System.Threading.Tasks;

public interface ISpaceTimeDbTransport
{
	Task<bool> PingAsync(CancellationToken cancellationToken = default);
	Task<string> TracePingAsync(CancellationToken cancellationToken = default);
	Task<SpaceTimeDbHandshakeResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
	Task<string> ExecuteSqlAsync(string accessToken, string sql, CancellationToken cancellationToken = default);
	Task CallReducerAsync(string accessToken, string reducerName, object args, CancellationToken cancellationToken = default);
	Task<SpaceTimeDbAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
	Task LogoutAsync(string accessToken, CancellationToken cancellationToken = default);
}
