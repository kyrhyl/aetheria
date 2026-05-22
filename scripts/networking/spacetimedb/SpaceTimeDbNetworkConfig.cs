using Godot;

public partial class SpaceTimeDbNetworkConfig : Resource
{
	[Export] public string ServerUrl = "http://localhost:3000";
	[Export] public string DatabaseName = "aetheria";
	[Export] public string PingEndpointTemplate = "v1/health";
	[Export] public string LoginEndpointTemplate = "v1/identity";
	[Export] public string ReducerCallEndpointTemplate = "v1/database/{db}/call/{reducer}";
	[Export] public string LoginAuditReducerName = "add";
	[Export] public string RefreshEndpointTemplate = "";
	[Export] public string LogoutEndpointTemplate = "";
	[Export] public int RequestTimeoutSeconds = 10;
	[Export] public int RetryCount = 2;
	[Export] public float RetryBackoffSeconds = 0.5f;

	[ExportGroup("Local Dev Auth")]
	[Export] public bool EnableLocalDevAuth = false;
	[Export] public bool AllowLocalDevFallbackOnPingFailure = false;
	[Export] public string DevUsername = "dev";
	[Export] public string DevPassword = "devpass123";
}
