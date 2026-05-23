using Godot;

public partial class DeathPlane : Area3D
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private NetworkManager _networkManager;

	public override void _Ready()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController player)
			return;

		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer())
			return;

		var stats = player.GetStats();
		if (stats == null || stats.CurrentHealth <= 0f)
			return;

		stats.TakeDamage(stats.MaxHealth + 9999f);
		_networkManager?.ReportPlayerEliminated(0, player.GetMultiplayerAuthority());
	}
}
