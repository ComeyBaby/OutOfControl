using Godot;

public partial class PlayerStats : Node
{
	[ExportGroup("Stats")]

	[Export] public int maxHealth = 100;
	[Export] public int maxAmmo = 0;
	[Export] public int maxSpeed = 10;
	[Export] public int maxStamina = 25;
	[Export] public int attackSpeedMultiplier = 5;
	[Export] public int defence = 0;
	[Export] public int bonusHealth = 0;
	[Export] public int damageMultiplier = 1;
	[Export] public int reach = 5;

}
