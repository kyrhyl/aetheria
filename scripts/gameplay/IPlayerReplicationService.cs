using System.Threading;
using System.Threading.Tasks;

public interface IPlayerReplicationService
{
	Task PublishLocalPlayerStateAsync(PlayerStateSnapshot snapshot, CancellationToken cancellationToken = default);
	Task SubscribeRemotePlayersAsync(CancellationToken cancellationToken = default);
}
