using Godot;

public partial class DeathPlane : Area3D
{
	public override void _Ready()
	{
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
	}
}
