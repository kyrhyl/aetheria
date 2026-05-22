using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class PlayerReplicationService : Node, IPlayerReplicationService
{
	public Task PublishLocalPlayerStateAsync(PlayerStateSnapshot snapshot, CancellationToken cancellationToken = default)
	{
		GD.Print($"[Replication] Local state publish placeholder: {snapshot.PlayerId}");
		return Task.CompletedTask;
	}

	public Task SubscribeRemotePlayersAsync(CancellationToken cancellationToken = default)
	{
		GD.Print("[Replication] Remote players subscription placeholder");
		return Task.CompletedTask;
	}
}
