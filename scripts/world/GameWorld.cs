using Godot;
using System;
using System.Collections.Generic;

public partial class GameWorld : Node3D
{
	private const string LoginScenePath = "res://scenes/ui/LoginScreen.tscn";
	private PlayerPawn _localPlayer = null!;
	private ISpaceTimeDbAuthService _authService = null!;
	private Label _debugLabel = null!;
	private Button _logoutButton = null!;
	private double _autosaveAccum;
	private double _debugRefreshAccum;
	private Vector3 _lastSavedPosition;
	private string _lastSaveStatus = "Not saved yet";
	private const double AutosaveIntervalSeconds = 5.0;
	private const float AutosaveDistanceThreshold = 0.75f;
	private const double RemotePollIntervalSeconds = 0.1;
	private const double PresencePublishIntervalSeconds = 0.1;
	private const float PresencePublishDistanceThreshold = 0.03f;
	private const float RemoteExtrapolateSeconds = 0.04f;
	private const float RemoteMaxCatchupSpeed = 10.0f;
	private const float RemoteSnapDistance = 16.0f;
	private const float RemoteSoftZoneDistance = 0.35f;
	private Node3D _remotePlayer;
	private RemoteTrack _remoteTrack;
	private bool _hasRemotePlayer;
	private double _remotePollAccum;
	private double _presencePublishAccum;
	private Vector3 _lastPresencePublishedPosition;
	private bool _isRefreshingRemotePlayers;
	private bool _isPublishingPresence;
	private string _lastRemoteIdentityLabel = "none";
	private double _lastRemoteSeenTime;

	public override void _Ready()
	{
		_localPlayer = GetNode<PlayerPawn>("PlayerPawn");
		_debugLabel = GetNode<Label>("UI/DebugPanel/DebugLabel");
		_logoutButton = GetNode<Button>("UI/LogoutButton");
		Node authNode = GetNodeOrNull<Node>("/root/ServiceRoot/SpaceTimeDbAuthService");
		_authService = authNode as ISpaceTimeDbAuthService;
		_logoutButton.Pressed += OnLogoutPressed;
		_ = InitializeSpawnAsync();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _Process(double delta)
	{
		if (_authService == null || string.IsNullOrWhiteSpace(_authService.CurrentSession.Identity))
		{
			return;
		}

		_autosaveAccum += delta;
		_debugRefreshAccum += delta;
		_remotePollAccum += delta;
		_presencePublishAccum += delta;
		if (_debugRefreshAccum >= 0.2)
		{
			_debugRefreshAccum = 0;
			UpdateDebugLabel();
		}

		if (_remotePollAccum >= RemotePollIntervalSeconds)
		{
			_remotePollAccum = 0;
			_ = RefreshRemotePlayersAsync();
		}

		if (_presencePublishAccum >= PresencePublishIntervalSeconds)
		{
			_presencePublishAccum = 0;
			PublishPresenceIfMoved();
		}

		UpdateRemotePlayerMotion((float)delta);

		if (_autosaveAccum < AutosaveIntervalSeconds)
		{
			return;
		}

		_autosaveAccum = 0;
		Vector3 current = _localPlayer.GlobalPosition;
		if (current.DistanceTo(_lastSavedPosition) < AutosaveDistanceThreshold)
		{
			return;
		}

		_ = SavePositionAsync(current);
	}

	public override void _ExitTree()
	{
		if (_logoutButton != null)
		{
			_logoutButton.Pressed -= OnLogoutPressed;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	private async System.Threading.Tasks.Task InitializeSpawnAsync()
	{
		if (_authService == null)
		{
			return;
		}

		try
		{
			PlayerPositionResult result = await _authService.GetSavedPlayerPositionAsync();
			if (result.Found)
			{
				_localPlayer.GlobalPosition = result.Position;
				_lastSaveStatus = "Loaded saved position";
			}
			else
			{
				_lastSaveStatus = "No saved position found";
			}
			_lastSavedPosition = _localPlayer.GlobalPosition;
			_lastPresencePublishedPosition = _localPlayer.GlobalPosition;
			_ = _authService.SavePlayerPositionAsync(_localPlayer.GlobalPosition);
			UpdateDebugLabel();
		}
		catch (Exception ex)
		{
			_lastSaveStatus = "Load failed";
			GD.PushWarning($"[World] Failed to load saved player position: {ex.Message}");
		}
	}

	private void PublishPresenceIfMoved()
	{
		if (_isPublishingPresence || _authService == null || string.IsNullOrWhiteSpace(_authService.CurrentSession.Identity))
		{
			return;
		}

		Vector3 current = _localPlayer.GlobalPosition;
		if (current.DistanceTo(_lastPresencePublishedPosition) < PresencePublishDistanceThreshold)
		{
			return;
		}

		_isPublishingPresence = true;
		_ = PublishPresenceAsync(current);
		_lastPresencePublishedPosition = current;
	}

	private async System.Threading.Tasks.Task PublishPresenceAsync(Vector3 position)
	{
		try
		{
			await _authService.SavePlayerPositionAsync(position);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[World] Presence publish failed: {ex.Message}");
		}
		finally
		{
			_isPublishingPresence = false;
		}
	}

	private async System.Threading.Tasks.Task RefreshRemotePlayersAsync()
	{
		if (_isRefreshingRemotePlayers || _authService == null || string.IsNullOrWhiteSpace(_authService.CurrentSession.Identity))
		{
			return;
		}

		_isRefreshingRemotePlayers = true;
		try
		{
			IReadOnlyList<RemotePlayerSnapshot> players = await _authService.GetRemotePlayersAsync();
			Vector3 localPos = _localPlayer.GlobalPosition;

			double now = Time.GetUnixTimeFromSystem();
			string chosenIdentity = string.Empty;
			Vector3 chosenPosition = Vector3.Zero;
			float chosenDistance = float.MaxValue;

			string chosenName = string.Empty;
			foreach (RemotePlayerSnapshot player in players)
			{
				string remoteIdentity = NormalizeIdentity(player.Identity);
				if (string.IsNullOrWhiteSpace(remoteIdentity))
				{
					continue;
				}

				float dist = player.Position.DistanceTo(localPos);
				if (dist < chosenDistance)
				{
					chosenDistance = dist;
					chosenIdentity = remoteIdentity;
					chosenPosition = player.Position;
					chosenName = player.DisplayName;
				}
			}

			if (!string.IsNullOrWhiteSpace(chosenIdentity))
			{
				Node3D remote = GetOrCreateRemotePlayer(chosenIdentity);
				SetRemotePlayerLabel(chosenName);
				if (_hasRemotePlayer)
				{
					double dt = Math.Max(0.001, now - _remoteTrack.LastSampleTime);
					_remoteTrack.Velocity = (chosenPosition - _remoteTrack.LastSamplePosition) / (float)dt;
				}
				else
				{
					remote.GlobalPosition = chosenPosition;
					_hasRemotePlayer = true;
				}

				_remoteTrack.Node = remote;
				_remoteTrack.LastSamplePosition = chosenPosition;
				_remoteTrack.LastSampleTime = now;
				_remoteTrack.TargetPosition = chosenPosition;
				_lastRemoteSeenTime = now;
				_lastRemoteIdentityLabel = string.IsNullOrWhiteSpace(chosenName) ? FormatIdentityLabel(chosenIdentity) : chosenName;
			}
			else
			{
				if (!_hasRemotePlayer)
				{
					_lastRemoteIdentityLabel = "none";
				}
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[World] Failed to refresh remote players: {ex.Message}");
		}
		finally
		{
			_isRefreshingRemotePlayers = false;
		}
	}

	private void UpdateRemotePlayerMotion(float delta)
	{
		double now = Time.GetUnixTimeFromSystem();
		if (!_hasRemotePlayer || _remoteTrack.Node == null || !IsInstanceValid(_remoteTrack.Node))
		{
			return;
		}

		float age = (float)Math.Max(0, now - _remoteTrack.LastSampleTime);
		float extra = Mathf.Min(age, RemoteExtrapolateSeconds);
		Vector3 predicted = _remoteTrack.TargetPosition + _remoteTrack.Velocity * extra;
		Vector3 current = _remoteTrack.Node.GlobalPosition;
		Vector3 offset = predicted - current;
		float distance = offset.Length();

		if (distance > RemoteSnapDistance)
		{
			_remoteTrack.Node.GlobalPosition = predicted;
			return;
		}

		if (distance < 0.01f)
		{
			return;
		}

		float maxStep = RemoteMaxCatchupSpeed * delta;
		if (distance <= RemoteSoftZoneDistance)
		{
			maxStep *= 0.45f;
		}

		if (distance <= maxStep)
		{
			_remoteTrack.Node.GlobalPosition = predicted;
			return;
		}

		_remoteTrack.Node.GlobalPosition = current + offset.Normalized() * maxStep;
	}

	private static string NormalizeIdentity(string identity)
	{
		if (string.IsNullOrWhiteSpace(identity))
		{
			return string.Empty;
		}

		string value = identity.Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			value = value[2..];
		}

		System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
			{
				sb.Append(char.ToLowerInvariant(c));
			}
		}

		return sb.ToString();
	}

	private Node3D GetOrCreateRemotePlayer(string identity)
	{
		if (_remotePlayer != null && IsInstanceValid(_remotePlayer))
		{
			return _remotePlayer;
		}

		Node3D root = new Node3D();
		root.Name = $"RemotePlayer_{identity[..Math.Min(8, identity.Length)]}";

		MeshInstance3D body = new MeshInstance3D
		{
			Mesh = new CapsuleMesh
			{
				Radius = 0.4f,
				Height = 1.4f
			}
		};
		body.Position = Vector3.Zero;

		StandardMaterial3D material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.26f, 0.8f, 0.44f)
		};
		body.MaterialOverride = material;

		Label3D tag = new Label3D
		{
			Name = "NameTag",
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Text = FormatIdentityLabel(identity),
			Position = new Vector3(0, 1.2f, 0),
			FontSize = 24,
			Modulate = new Color(0.9f, 1f, 0.9f)
		};

		root.AddChild(body);

		StaticBody3D colliderBody = new StaticBody3D();
		CollisionShape3D collisionShape = new CollisionShape3D
		{
			Shape = new CapsuleShape3D
			{
				Radius = 0.4f,
				Height = 1.4f
			}
		};
		colliderBody.AddChild(collisionShape);
		root.AddChild(colliderBody);

		root.AddChild(tag);
		AddChild(root);
		_remotePlayer = root;
		return root;
	}

	private void SetRemotePlayerLabel(string displayName)
	{
		if (_remotePlayer == null || !IsInstanceValid(_remotePlayer) || string.IsNullOrWhiteSpace(displayName))
		{
			return;
		}

		Label3D label = _remotePlayer.GetNodeOrNull<Label3D>("NameTag");
		if (label == null)
		{
			foreach (Node child in _remotePlayer.GetChildren())
			{
				if (child is Label3D fallback)
				{
					label = fallback;
					break;
				}
			}
		}

		if (label != null)
		{
			label.Text = displayName;
		}
	}

	private static string FormatIdentityLabel(string identity)
	{
		if (identity.Length <= 10)
		{
			return identity;
		}

		return identity[..4] + ".." + identity[(identity.Length - 4)..];
	}

	private struct RemoteTrack
	{
		public Node3D Node;
		public Vector3 TargetPosition;
		public Vector3 LastSamplePosition;
		public Vector3 Velocity;
		public double LastSampleTime;
	}

	private async System.Threading.Tasks.Task SavePositionAsync(Vector3 position)
	{
		try
		{
			await _authService.SavePlayerPositionAsync(position);
			_lastSavedPosition = position;
			_lastSaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
		}
		catch (Exception ex)
		{
			_lastSaveStatus = "Save failed";
			GD.PushWarning($"[World] Failed to save player position: {ex.Message}");
		}
		finally
		{
			UpdateDebugLabel();
		}
	}

	private async void OnLogoutPressed()
	{
		_logoutButton.Disabled = true;
		try
		{
			await SavePositionAsync(_localPlayer.GlobalPosition);
			await _authService.LogoutAsync();
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, LoginScenePath);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[World] Logout failed: {ex.Message}");
			_logoutButton.Disabled = false;
		}
	}

	private void UpdateDebugLabel()
	{
		if (_debugLabel == null || !IsInstanceValid(_debugLabel) || _authService == null || _localPlayer == null || !IsInstanceValid(_localPlayer))
		{
			return;
		}

		Vector3 p = _localPlayer.GlobalPosition;
		string mode = _authService.IsUsingLocalDevAuth ? "LOCAL_DEV" : "SERVER";
		string username = string.IsNullOrWhiteSpace(_authService.CurrentUsername) ? "unknown" : _authService.CurrentUsername;
		string identity = string.IsNullOrWhiteSpace(_authService.CurrentSession.Identity) ? "none" : _authService.CurrentSession.Identity[..Math.Min(12, _authService.CurrentSession.Identity.Length)] + "...";
		float dist = p.DistanceTo(_lastSavedPosition);

		_debugLabel.Text =
			$"Mode: {mode}\n" +
			$"Username: {username}\n" +
			$"Identity: {identity}\n" +
			$"Visible players: {(_hasRemotePlayer ? 2 : 1)}\n" +
			$"Remote sample: {_lastRemoteIdentityLabel}\n" +
			$"Pos: {p.X:F2}, {p.Y:F2}, {p.Z:F2}\n" +
			$"Moved since save: {dist:F2}\n" +
			$"Last save: {_lastSaveStatus}";
	}
}
