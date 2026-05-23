using Godot;
using System.Collections.Generic;

public static class PerkCatalog
{
	public static PerkDefinition[] BuildExpandedPerks()
	{
		var perks = new List<PerkDefinition>();
		perks.AddRange(BuildLegacyPerks());
		perks.AddRange(new[]
		{
			// Universal mobility and combat perks
			Perk(
				"Adrenal Frame",
				PerkRarity.Common,
				PerkWeaponRestriction.All,
				"Boosts run speed to help you take better angles.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.12f)),
			Perk(
				"Juggernaut Weave",
				PerkRarity.Rare,
				PerkWeaponRestriction.All,
				"More max health with slightly stronger hit push.",
				ModFloat(PlayerStatTarget.MaxHealthMultiplier, 0.22f),
				ModFloat(PlayerStatTarget.Knockback, 0.3f)),
			Perk(
				"High-Tension Springs",
				PerkRarity.Epic,
				PerkWeaponRestriction.All,
				"Gain one extra jump and stronger forward jump burst.",
				ModInt(PlayerStatTarget.TotalJumps, 1),
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 2.2f)),
			Perk(
				"Aerodynamic Payload",
				PerkRarity.Common,
				PerkWeaponRestriction.All,
				"Projectiles fly faster and hold strength at longer distances.",
				ModFloat(PlayerStatTarget.ProjectileSpeedMultiplier, 0.2f),
				ModFloat(PlayerStatTarget.RangeMultiplier, 0.1f)),
			Perk(
				"Catalyst Rounds",
				PerkRarity.Epic,
				PerkWeaponRestriction.All,
				"Higher damage output and faster attack cycling.",
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.16f),
				ModFloat(PlayerStatTarget.AttackSpeedMultiplier, 0.14f)),
			Perk(
				"Viscera Leech",
				PerkRarity.Epic,
				PerkWeaponRestriction.All,
				"Recover health from dealt damage.",
				ModFloat(PlayerStatTarget.LifeStealPercent, 0.08f)),
			Perk(
				"Impact Reactor",
				PerkRarity.Rare,
				PerkWeaponRestriction.All,
				"Hard landings hit wider and harder.",
				ModFloat(PlayerStatTarget.LandingShockwaveDamage, 10.0f),
				ModFloat(PlayerStatTarget.LandingShockwaveRadius, 1.2f)),
			Perk(
				"Apex Circuit",
				PerkRarity.Legendary,
				PerkWeaponRestriction.All,
				"Major offense and mobility spike with a slight health tradeoff.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.16f),
				ModFloat(PlayerStatTarget.AttackSpeedMultiplier, 0.22f),
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.14f),
				ModFloat(PlayerStatTarget.MaxHealthMultiplier, -0.1f)),

			// Assault-focused perks
			Perk(
				"Run-and-Gun Rig",
				PerkRarity.Rare,
				PerkWeaponRestriction.Assault,
				"Assault gains speed and faster fire cadence while moving.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.18f),
				ModFloat(PlayerStatTarget.AttackSpeedMultiplier, 0.12f)),
			Perk(
				"Lane Bender",
				PerkRarity.Rare,
				PerkWeaponRestriction.Assault,
				"Assault bullets bounce once and keep pressure at range.",
				ModInt(PlayerStatTarget.RicochetCount, 1),
				ModFloat(PlayerStatTarget.RangeMultiplier, 0.12f)),
			Perk(
				"Armor Piercer",
				PerkRarity.Epic,
				PerkWeaponRestriction.Assault,
				"Assault rounds pierce one enemy and gain damage.",
				ModInt(PlayerStatTarget.PierceCount, 1),
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.12f)),
			Perk(
				"Burst Glide",
				PerkRarity.Epic,
				PerkWeaponRestriction.Assault,
				"Gain a stronger air dash burst with limited uses each life.",
				new[]
				{
					ModInt(PlayerStatTarget.AirDashCharges, 1),
					ModFloat(PlayerStatTarget.AirDashSpeed, 3.0f)
				},
				PerkUseTrigger.AirDash,
				2),
			Perk(
				"Clip and Rip",
				PerkRarity.Common,
				PerkWeaponRestriction.Assault,
				"Larger assault magazine plus better jump entry momentum.",
				ModInt(PlayerStatTarget.AttackCapacity, 8),
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 1.6f)),
			Perk(
				"Cover Hopper",
				PerkRarity.Rare,
				PerkWeaponRestriction.Assault,
				"Use wall-jump repositioning twice per life.",
				new[]
				{
					ModInt(PlayerStatTarget.WallJumpCount, 1),
					ModFloat(PlayerStatTarget.WallJumpPush, 2.0f)
				},
				PerkUseTrigger.WallJump,
				2),

			// Sniper-focused perks
			Perk(
				"Eagle Vein",
				PerkRarity.Rare,
				PerkWeaponRestriction.Sniper,
				"Sniper headshots hit even harder.",
				ModFloat(PlayerStatTarget.HeadshotMultiplier, 0.35f)),
			Perk(
				"Relocation Core",
				PerkRarity.Legendary,
				PerkWeaponRestriction.Sniper,
				"Longer blink step with limited tactical uses.",
				new[]
				{
					ModFloat(PlayerStatTarget.BlinkDistance, 2.2f),
					ModFloat(PlayerStatTarget.BlinkCooldown, 4.2f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.BlinkStep,
				2),
			Perk(
				"Razor Wind",
				PerkRarity.Common,
				PerkWeaponRestriction.Sniper,
				"Faster movement and projectile travel for reposition sniping.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.14f),
				ModFloat(PlayerStatTarget.ProjectileSpeedMultiplier, 0.15f)),
			Perk(
				"Throughline",
				PerkRarity.Epic,
				PerkWeaponRestriction.Sniper,
				"Sniper shots pierce once and retain more effective range.",
				ModInt(PlayerStatTarget.PierceCount, 1),
				ModFloat(PlayerStatTarget.RangeMultiplier, 0.15f)),
			Perk(
				"Cliffstep",
				PerkRarity.Rare,
				PerkWeaponRestriction.Sniper,
				"Use precision wall jumps to reclaim high ground.",
				new[]
				{
					ModInt(PlayerStatTarget.WallJumpCount, 1),
					ModFloat(PlayerStatTarget.WallJumpPush, 3.0f)
				},
				PerkUseTrigger.WallJump,
				2),
			Perk(
				"Deadeye Reserve",
				PerkRarity.Common,
				PerkWeaponRestriction.Sniper,
				"One extra sniper round per mag and stronger body-shot pressure.",
				ModInt(PlayerStatTarget.AttackCapacity, 1),
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.1f)),

			// Fists-focused perks
			Perk(
				"Street Sprint",
				PerkRarity.Common,
				PerkWeaponRestriction.Fists,
				"Significant speed increase to close gaps on ranged targets.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.24f)),
			Perk(
				"Chain Dash",
				PerkRarity.Epic,
				PerkWeaponRestriction.Fists,
				"Extra air dash charge with faster dash velocity.",
				new[]
				{
					ModInt(PlayerStatTarget.AirDashCharges, 1),
					ModFloat(PlayerStatTarget.AirDashSpeed, 4.0f)
				},
				PerkUseTrigger.AirDash,
				3),
			Perk(
				"Bone Breaker",
				PerkRarity.Rare,
				PerkWeaponRestriction.Fists,
				"Heavier punches that shove enemies out of position.",
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.2f),
				ModFloat(PlayerStatTarget.Knockback, 0.8f)),
			Perk(
				"Apex Slam",
				PerkRarity.Epic,
				PerkWeaponRestriction.Fists,
				"Ground pound becomes a high-impact engage tool.",
				new[]
				{
					ModFloat(PlayerStatTarget.GroundPoundDamage, 22.0f),
					ModFloat(PlayerStatTarget.GroundPoundRadius, 1.6f)
				},
				PerkUseTrigger.GroundPound,
				2),
			Perk(
				"Blood Cycler",
				PerkRarity.Epic,
				PerkWeaponRestriction.Fists,
				"More sustain and durability in brawl range.",
				ModFloat(PlayerStatTarget.LifeStealPercent, 0.12f),
				ModFloat(PlayerStatTarget.MaxHealthMultiplier, 0.12f)),
			Perk(
				"Corner Vault",
				PerkRarity.Rare,
				PerkWeaponRestriction.Fists,
				"Wall mobility and momentum burst for aggressive entry routes.",
				new[]
				{
					ModInt(PlayerStatTarget.WallJumpCount, 1),
					ModFloat(PlayerStatTarget.MomentumJumpBoost, 3.5f)
				},
				PerkUseTrigger.WallJump,
				2),

			// Sword-focused perks
			Perk(
				"Duelist Tempo",
				PerkRarity.Common,
				PerkWeaponRestriction.Sword,
				"Faster swings and movement for tighter duel pressure.",
				ModFloat(PlayerStatTarget.AttackSpeedMultiplier, 0.22f),
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.12f)),
			Perk(
				"Phase Lunge",
				PerkRarity.Epic,
				PerkWeaponRestriction.Sword,
				"Blink farther with a shorter base blink cooldown.",
				new[]
				{
					ModFloat(PlayerStatTarget.BlinkDistance, 1.8f),
					ModFloat(PlayerStatTarget.BlinkCooldown, 3.8f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.BlinkStep,
				2),
			Perk(
				"Shatter Arc",
				PerkRarity.Epic,
				PerkWeaponRestriction.Sword,
				"Landing shockwave becomes a stronger duel finisher.",
				new[]
				{
					ModFloat(PlayerStatTarget.LandingShockwaveDamage, 14.0f),
					ModFloat(PlayerStatTarget.LandingShockwaveRadius, 1.1f)
				},
				PerkUseTrigger.LandingShockwave,
				2),
			Perk(
				"Windcut",
				PerkRarity.Rare,
				PerkWeaponRestriction.Sword,
				"Extended effective strike reach with bonus damage.",
				ModFloat(PlayerStatTarget.RangeMultiplier, 0.18f),
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.08f)),
			Perk(
				"Parry Step",
				PerkRarity.Common,
				PerkWeaponRestriction.Sword,
				"Adds agile dash repositioning for sword engages.",
				new[]
				{
					ModInt(PlayerStatTarget.AirDashCharges, 1),
					ModFloat(PlayerStatTarget.AirDashSpeed, 2.5f)
				},
				PerkUseTrigger.AirDash,
				2),
			Perk(
				"Relentless Edge",
				PerkRarity.Rare,
				PerkWeaponRestriction.Sword,
				"More health and jump momentum to stick onto ranged targets.",
				ModFloat(PlayerStatTarget.MaxHealthMultiplier, 0.16f),
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 2.0f))
		});

		return perks.ToArray();
	}

	private static IEnumerable<PerkDefinition> BuildLegacyPerks()
	{
		return new[]
		{
			Perk("Movement Speed Boost", PerkRarity.Common, PerkWeaponRestriction.All, "Increases player movement speed.",
				ModFloat(PlayerStatTarget.MoveSpeedMultiplier, 0.1f, PerkModifierOperation.PercentAdd)),
			Perk("Health Boost", PerkRarity.Common, PerkWeaponRestriction.All, "Increases maximum health pool.",
				ModFloat(PlayerStatTarget.MaxHealthMultiplier, 0.1f)),
			Perk("Damage Boost", PerkRarity.Rare, PerkWeaponRestriction.All, " Increases attack damage output.",
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.05f, PerkModifierOperation.PercentAdd)),
			Perk("Attack Speed Boost", PerkRarity.Rare, PerkWeaponRestriction.All, "Increases attack rate.",
				ModFloat(PlayerStatTarget.AttackSpeedMultiplier, 0.05f, PerkModifierOperation.PercentAdd)),
			Perk("Projectile Speed Boost", PerkRarity.Common, PerkWeaponRestriction.Assault | PerkWeaponRestriction.Sniper, "Increases projectile velocity.",
				ModFloat(PlayerStatTarget.ProjectileSpeedMultiplier, 0.1f, PerkModifierOperation.PercentAdd)),
			Perk("Double Jump", PerkRarity.Epic, PerkWeaponRestriction.All, "Grants additional jumps.",
				ModInt(PlayerStatTarget.TotalJumps, 1)),
			Perk("Weapon Reach", PerkRarity.Rare, PerkWeaponRestriction.Fists | PerkWeaponRestriction.Sword, "Increases melee range.",
				ModFloat(PlayerStatTarget.RangeMultiplier, 0.1f, PerkModifierOperation.PercentAdd)),
			Perk("Fist Damage Boost", PerkRarity.Rare, PerkWeaponRestriction.Fists, "Increases fist damage.",
				ModFloat(PlayerStatTarget.DamageMultiplier, 0.1f, PerkModifierOperation.PercentAdd)),
			Perk("Knockback", PerkRarity.Common, PerkWeaponRestriction.Fists | PerkWeaponRestriction.Sword, "Adds strong knockback to attacks.",
				ModFloat(PlayerStatTarget.Knockback, 5.0f)),
			Perk("Air Dash", PerkRarity.Epic, PerkWeaponRestriction.All, "Press sprint in mid-air to dash forward once.",
				new[]
				{
					ModInt(PlayerStatTarget.AirDashCharges, 1),
					ModFloat(PlayerStatTarget.AirDashSpeed, 4.5f)
				}),
			Perk("Featherfall", PerkRarity.Common, PerkWeaponRestriction.All, "Hold jump while falling to glide and descend slowly.",
				ModFloat(PlayerStatTarget.GlideGravityMultiplier, 0.45f, PerkModifierOperation.Multiply)),
			Perk("Ground Pound", PerkRarity.Epic, PerkWeaponRestriction.All, "Press sprint + reload mid-air to slam down and blast nearby enemies on landing.",
				new[]
				{
					ModFloat(PlayerStatTarget.GroundPoundDamage, 28.0f),
					ModFloat(PlayerStatTarget.GroundPoundRadius, 3.8f)
				},
				PerkUseTrigger.GroundPound,
				1),
			Perk("Vampire Rounds", PerkRarity.Epic, PerkWeaponRestriction.Assault | PerkWeaponRestriction.Sniper, "Heal for a portion of damage dealt.",
				ModFloat(PlayerStatTarget.LifeStealPercent, 0.1f)),
			Perk("Ricochet Rounds", PerkRarity.Rare, PerkWeaponRestriction.Assault | PerkWeaponRestriction.Sniper, "Ranged attacks bounce once off surfaces.",
				ModInt(PlayerStatTarget.RicochetCount, 1)),
			Perk("Piercing Shots", PerkRarity.Epic, PerkWeaponRestriction.Assault | PerkWeaponRestriction.Sniper, "Ranged attacks pierce through one enemy.",
				ModInt(PlayerStatTarget.PierceCount, 1)),
			Perk("Blink Step", PerkRarity.Legendary, PerkWeaponRestriction.All, "Press sprint + reload on the ground to blink forward through danger.",
				new[]
				{
					ModFloat(PlayerStatTarget.BlinkDistance, 7.0f),
					ModFloat(PlayerStatTarget.BlinkCooldown, 6.0f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.BlinkStep,
				1),
			Perk("Wall Rebound", PerkRarity.Rare, PerkWeaponRestriction.All, "Jump off walls once per airtime with a strong push.",
				new[]
				{
					ModInt(PlayerStatTarget.WallJumpCount, 1),
					ModFloat(PlayerStatTarget.WallJumpPush, 2.5f)
				}),
			Perk("Momentum Vault", PerkRarity.Rare, PerkWeaponRestriction.All, "Jumping while moving launches you forward.",
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 4.5f)),
			Perk("Seismic Landing", PerkRarity.Epic, PerkWeaponRestriction.All, "Hard landings release a shockwave that damages nearby enemies.",
				new[]
				{
					ModFloat(PlayerStatTarget.LandingShockwaveDamage, 16.0f),
					ModFloat(PlayerStatTarget.LandingShockwaveRadius, 3.5f),
					ModFloat(PlayerStatTarget.LandingShockwaveMinFallSpeed, 12.0f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.LandingShockwave,
				1),
			Perk("Breacher Dash", PerkRarity.Epic, PerkWeaponRestriction.Assault, "Assault class gains an extra mid-air dash burst.",
				new[]
				{
					ModInt(PlayerStatTarget.AirDashCharges, 1),
					ModFloat(PlayerStatTarget.AirDashSpeed, 2.2f)
				}),
			Perk("Tracer Skip", PerkRarity.Rare, PerkWeaponRestriction.Assault, "Assault bullets bounce once for lane-denial angles.",
				ModInt(PlayerStatTarget.RicochetCount, 1)),
			Perk("Siege Magazine", PerkRarity.Rare, PerkWeaponRestriction.Assault, "Assault gets a larger mag and stronger jump momentum.",
				ModInt(PlayerStatTarget.AttackCapacity, 10),
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 2.0f)),
			Perk("Phase Scope", PerkRarity.Legendary, PerkWeaponRestriction.Sniper, "Sniper blink (sprint + reload) goes farther and comes back sooner.",
				new[]
				{
					ModFloat(PlayerStatTarget.BlinkDistance, 3.0f),
					ModFloat(PlayerStatTarget.BlinkCooldown, 4.8f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.BlinkStep,
				1),
			Perk("Rail Pierce", PerkRarity.Epic, PerkWeaponRestriction.Sniper, "Sniper shots pierce one enemy.",
				ModInt(PlayerStatTarget.PierceCount, 1)),
			Perk("Vantage Hook", PerkRarity.Rare, PerkWeaponRestriction.Sniper, "Sniper can wall-jump once to reclaim high ground.",
				ModInt(PlayerStatTarget.WallJumpCount, 1),
				ModFloat(PlayerStatTarget.WallJumpPush, 2.5f)),
			Perk("Meteor Knuckles", PerkRarity.Epic, PerkWeaponRestriction.Fists, "Fists massively empower ground pound impact.",
				new[]
				{
					ModFloat(PlayerStatTarget.GroundPoundDamage, 18.0f),
					ModFloat(PlayerStatTarget.GroundPoundRadius, 1.1f)
				},
				PerkUseTrigger.GroundPound,
				1),
			Perk("Bloodrush", PerkRarity.Epic, PerkWeaponRestriction.Fists, "Fists gain bonus lifesteal for sustained brawls.",
				ModFloat(PlayerStatTarget.LifeStealPercent, 0.08f)),
			Perk("Parkour Brawler", PerkRarity.Rare, PerkWeaponRestriction.Fists, "Fists gain extra wall mobility and jump launch power.",
				ModInt(PlayerStatTarget.WallJumpCount, 1),
				ModFloat(PlayerStatTarget.MomentumJumpBoost, 3.0f)),
			Perk("Duelist Blink", PerkRarity.Epic, PerkWeaponRestriction.Sword, "Sword blink (sprint + reload) becomes a faster duel reset.",
				new[]
				{
					ModFloat(PlayerStatTarget.BlinkDistance, 2.5f),
					ModFloat(PlayerStatTarget.BlinkCooldown, 5.2f, PerkModifierOperation.Set)
				},
				PerkUseTrigger.BlinkStep,
				1),
			Perk("Sky Cleaver", PerkRarity.Rare, PerkWeaponRestriction.Sword, "Sword gains an extra air dash and stronger aerial wall-push.",
				ModInt(PlayerStatTarget.AirDashCharges, 1),
				ModFloat(PlayerStatTarget.WallJumpPush, 2.0f)),
			Perk("Shockwave Slash", PerkRarity.Epic, PerkWeaponRestriction.Sword, "Sword landings release a stronger close-range shockwave.",
				new[]
				{
					ModFloat(PlayerStatTarget.LandingShockwaveDamage, 10.0f),
					ModFloat(PlayerStatTarget.LandingShockwaveRadius, 1.2f)
				},
				PerkUseTrigger.LandingShockwave,
				1)
		};
	}

	private static PerkDefinition Perk(
		string name,
		PerkRarity rarity,
		PerkWeaponRestriction weapons,
		string description,
		params PerkStatModifier[] modifiers)
	{
		return Perk(name, rarity, weapons, description, modifiers, PerkUseTrigger.None, 0);
	}

	private static PerkDefinition Perk(
		string name,
		PerkRarity rarity,
		PerkWeaponRestriction weapons,
		string description,
		PerkStatModifier[] modifiers,
		PerkUseTrigger trigger,
		int maxUses)
	{
		return new PerkDefinition
		{
			PerkName = name,
			Rarity = rarity,
			AllowedWeapons = weapons,
			Description = description,
			Modifiers = modifiers ?? System.Array.Empty<PerkStatModifier>(),
			UseTrigger = trigger,
			MaxUses = Mathf.Max(0, maxUses)
		};
	}

	private static PerkStatModifier ModFloat(
		PlayerStatTarget target,
		float value,
		PerkModifierOperation op = PerkModifierOperation.Add)
	{
		return new PerkStatModifier
		{
			Target = target,
			Operation = op,
			FloatValue = value
		};
	}

	private static PerkStatModifier ModInt(
		PlayerStatTarget target,
		int value,
		PerkModifierOperation op = PerkModifierOperation.Add)
	{
		return new PerkStatModifier
		{
			Target = target,
			Operation = op,
			IntValue = value
		};
	}
}
