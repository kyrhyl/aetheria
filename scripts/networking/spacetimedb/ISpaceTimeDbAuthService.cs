using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;

public interface ISpaceTimeDbAuthService
{
	bool IsServerConnected { get; }
	bool IsUsingLocalDevAuth { get; }
	string CurrentUsername { get; }
	SpaceTimeDbSession CurrentSession { get; }
	Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
	Task<string> TracePingAsync(CancellationToken cancellationToken = default);
	Task<SpaceTimeDbSession> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
	Task<PlayerPositionResult> GetSavedPlayerPositionAsync(CancellationToken cancellationToken = default);
	Task<IReadOnlyList<RemotePlayerSnapshot>> GetRemotePlayersAsync(CancellationToken cancellationToken = default);
	Task SavePlayerPositionAsync(Vector3 position, CancellationToken cancellationToken = default);
	Task<SpaceTimeDbSession> RefreshSessionAsync(CancellationToken cancellationToken = default);
	Task LogoutAsync(CancellationToken cancellationToken = default);
}
