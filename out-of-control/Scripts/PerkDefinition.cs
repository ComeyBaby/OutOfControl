using Godot;

public enum PlayerStatTarget
{
	MaxHealthMultiplier,
	DamageMultiplier,
	MoveSpeedMultiplier,
	RangeMultiplier,
	TotalJumps,
	ProjectileSpeed,
	AttackSpeedMultiplier,
	Knockback,
	HeadshotMultiplier,
	AttackCapacity,
	ProjectileSpeedMultiplier,
	AirDashCharges,
	AirDashSpeed,
	GlideGravityMultiplier,
	GroundPoundDamage,
	GroundPoundRadius,
	LifeStealPercent,
	RicochetCount,
	PierceCount,
	BlinkDistance,
	BlinkCooldown,
	WallJumpCount,
	WallJumpPush,
	MomentumJumpBoost,
	LandingShockwaveDamage,
	LandingShockwaveRadius,
	LandingShockwaveMinFallSpeed
}

public enum PerkModifierOperation
{
	Add,
	PercentAdd,
	Multiply,
	Set
}

public enum PerkRarity
{
	Common,
	Rare,
	Epic,
	Legendary
}

public enum PerkUseTrigger
{
	None = 0,
	AirDash = 1,
	BlinkStep = 2,
	GroundPound = 3,
	WallJump = 4,
	LandingShockwave = 5
}

[System.Flags]
public enum PerkWeaponRestriction
{
	None = 0,
	Assault = 1 << 0,
	Sniper = 1 << 1,
	Fists = 1 << 2,
	Sword = 1 << 3,
	All = Assault | Sniper | Fists | Sword
}

[GlobalClass]
public partial class PerkDefinition : Resource
{
	[Export] public string PerkName { get; set; } = "";
	[Export] public PerkRarity Rarity { get; set; } = PerkRarity.Common;
	[Export] public PerkWeaponRestriction AllowedWeapons { get; set; } = PerkWeaponRestriction.All;
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
	[Export] public PerkStatModifier[] Modifiers { get; set; } = System.Array.Empty<PerkStatModifier>();
	[ExportGroup("Usage Limits")]
	[Export] public PerkUseTrigger UseTrigger { get; set; } = PerkUseTrigger.None;
	[Export(PropertyHint.Range, "0,20,1")] public int MaxUses { get; set; } = 0;

	public bool IsLimitedUse => UseTrigger != PerkUseTrigger.None && MaxUses > 0;

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(PerkName)
		|| (Modifiers != null && Modifiers.Length > 0)
		|| !string.IsNullOrWhiteSpace(Description);

	public bool IsAvailableForWeapon(string weapon)
	{
		var weaponRestriction = GetWeaponRestriction(weapon);
		if (AllowedWeapons == PerkWeaponRestriction.All)
			return true;

		if (AllowedWeapons == PerkWeaponRestriction.None)
			return false;

		return (AllowedWeapons & weaponRestriction) != 0;
	}

	private PerkWeaponRestriction GetWeaponRestriction(string weapon)
	{
		if (string.IsNullOrWhiteSpace(weapon))
			return PerkWeaponRestriction.All;

		return weapon.Trim() switch
		{
			"Assault" => PerkWeaponRestriction.Assault,
			"Sniper" => PerkWeaponRestriction.Sniper,
			"Fists" => PerkWeaponRestriction.Fists,
			"Sword" => PerkWeaponRestriction.Sword,
			_ => PerkWeaponRestriction.All
		};
	}
}
