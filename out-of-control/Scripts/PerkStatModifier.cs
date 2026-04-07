using Godot;

[GlobalClass]
public partial class PerkStatModifier : Resource
{
	[Export] public PlayerStatTarget Target;
	[Export] public PerkModifierOperation Operation;
	[Export] public float FloatValue { get; set; } = 0f;
	[Export] public int IntValue { get; set; } = 0;
	[Export] public bool BoolValue { get; set; } = false;
}
