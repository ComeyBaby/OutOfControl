using Godot;

[GlobalClass]
public partial class PerkStatModifier : Resource
{
	[Export] public PlayerStatTarget Target { get; set; } = PlayerStatTarget.MaxHealthMultiplier;
	[Export] public PerkModifierOperation Operation { get; set; } = PerkModifierOperation.Add;
	[Export] public float FloatValue { get; set; } = 0f;
	[Export] public int IntValue { get; set; } = 0;
	[Export] public bool BoolValue { get; set; } = false;
}
