using Godot;
using System.Collections.Generic;

public partial class PlayerStats : Node
{
	public enum Weapon
	{
		Assault,
		Sniper,
		Fists,
		Sword,
		Staff
	}

	[Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
	[Signal] public delegate void StaminaChangedEventHandler(float currentStamina, float maxStamina);
	[Signal] public delegate void WeaponChangedEventHandler(string weapon);

	[ExportGroup("Movement")]
	[Export] public bool canMove = true;
	[Export] public bool hasGravity = true;
	[Export] public bool canJump = true;
	[Export] public bool canSprint = true;
	[Export] public bool canFreefly = false;

	[Export] public float lookSpeed = 0.002f;
	[Export] public float baseSpeed = 7.0f;
	[Export] public float jumpVelocity = 4.5f;
	[Export] public float sprintSpeed = 10.0f;
	[Export] public float freeflySpeed = 25.0f;
	[Export] public float moveSpeedMultiplier = 1.0f;

	[ExportGroup("Input Actions")]
	[Export] public string inputLeft = "move_left";
	[Export] public string inputRight = "move_right";
	[Export] public string inputForward = "move_forward";
	[Export] public string inputBack = "move_backward";
	[Export] public string inputJump = "jump";
	[Export] public string inputSprint = "sprint";
	[Export] public string inputFreefly = "freefly";

	[ExportGroup("Network Smoothing")]
	[Export] public float networkSyncRate = 100.0f;
	[Export] public float remotePositionSmoothing = 14.0f;
	[Export] public float remoteRotationSmoothing = 14.0f;

	public float maxHealth = 100.0f;
	public float maxHealthMultiplier = 1.0f;
	public float maxStamina = 25.0f;
	public float staminaDrainPerSecond = 10.0f;
	public float staminaRegenPerSecond = 8.0f;
	public float healthRegenPerSecond = 0f;
	public float attackDamage = 10.0f;
	public float damageMultiplier = 1.0f;
	public float attackRange = 5.0f;
	public float rangeMultiplier = 1.0f;
	public int totalJumps = 1;
	public float projectileSpeed = 1.0f;
	public float projectileSpeedMultiplier = 1.0f;
	public float attackSpeed = 1.0f;
	public float attackSpeedMultiplier = 1.0f;
	public float knockback = 0.0f;
	public float headshotMultiplier = 1.0f;
	public int attackCapacity = 0;
	public bool debugAttack = false;
	public string selectedWeapon = "Assault";

	private readonly List<PerkDefinition> _appliedPerks = new();
	private float _currentHealth;
	private float _currentStamina;

	public float CurrentHealth => _currentHealth;
	public float MaxHealth => Mathf.Max(0f, maxHealth * maxHealthMultiplier);
	public float CurrentStamina => _currentStamina;
	public float MaxStamina => maxStamina;
	public bool HasStamina => _currentStamina > 0f;
	public float AttackCooldown
	{
		get
		{
			var effectiveAttackSpeed = attackSpeed * attackSpeedMultiplier;
			return effectiveAttackSpeed <= 0f ? 0f : 1.0f / effectiveAttackSpeed;
		}
	}
	public float AttackRange => Mathf.Max(0f, attackRange * rangeMultiplier);
	public float AttackDamage => Mathf.Max(0f, attackDamage * damageMultiplier);
	public float ProjectileSpeed => Mathf.Max(0f, projectileSpeed * projectileSpeedMultiplier);
	public string SelectedWeapon => selectedWeapon;
	public IReadOnlyList<PerkDefinition> AppliedPerks => _appliedPerks;

	public override void _Ready()
	{
		ApplyWeaponPreset(selectedWeapon, true);
		ResetHealth();
		ResetStamina();
	}

	public void SetWeapon(string weapon)
	{
		ApplyWeaponPreset(weapon, true);
		ReapplyPerks();
		ResetHealth();
		ResetStamina();
	}

	public void ApplyPerk(PerkDefinition perk)
	{
		if (perk == null)
			return;

		if (_appliedPerks.Contains(perk))
			return;

		_appliedPerks.Add(perk);
		ApplyPerkEffects(perk);
		ClampVitals();
	}

	public void ClearPerks()
	{
		if (_appliedPerks.Count == 0)
			return;

		_appliedPerks.Clear();
		RebuildFromWeaponPreset();
		ClampVitals();
	}

	public void ReapplyPerks()
	{
		RebuildFromWeaponPreset();

		foreach (var perk in _appliedPerks)
		{
			if (perk != null)
				ApplyPerkEffects(perk);
		}

		ClampVitals();
	}

	private void RebuildFromWeaponPreset()
	{
		ApplyWeaponPreset(selectedWeapon, false);
	}

	private void ApplyWeaponPreset(string weapon, bool emitSignal)
	{
		selectedWeapon = string.IsNullOrWhiteSpace(weapon) ? "Assault" : weapon;

		maxHealth = 100.0f;
		attackDamage = 10.0f;
		attackRange = 5.0f;
		damageMultiplier = 1.0f;
		rangeMultiplier = 1.0f;
		projectileSpeed = 1.0f;
		projectileSpeedMultiplier = 1.0f;
		attackSpeed = 1.0f;
		attackSpeedMultiplier = 1.0f;
		knockback = 1.0f;
		headshotMultiplier = 1.0f;
		attackCapacity = 0;

		switch (selectedWeapon)
		{
			case "Assault":
				maxHealth = 90.0f;
				attackDamage = 5.0f;
				attackRange = 35.0f;
				projectileSpeed = 65.0f;
				attackSpeed = 0.1f;
				attackSpeedMultiplier = 1.0f;
				knockback = 1.0f;
				attackCapacity = 30;
				headshotMultiplier = 1.15f;
				break;
			case "Sniper":
				maxHealth = 85.0f;
				attackDamage = 30.0f;
				attackRange = 80.0f;
				projectileSpeed = 120.0f;
				attackSpeed = 1.2f;
				attackSpeedMultiplier = 1.0f;
				knockback = 2.0f;
				attackCapacity = 3;
				headshotMultiplier = 2.0f;
				break;
			case "Fists":
				maxHealth = 150.0f;
				attackDamage = 10.0f;
				attackRange = 1.5f;
				attackSpeed = 3.5f;
				knockback = 0.8f;
				break;
			case "Sword":
				maxHealth = 125.0f;
				attackDamage = 20.0f;
				attackRange = 3.0f;
				attackSpeed = 1.5f;
				knockback = 1.4f;
				break;
			case "Staff":
				maxHealth = 100.0f;
				attackDamage = 14.0f;
				attackRange = 20.0f;
				projectileSpeed = 55.0f;
				attackSpeed = 1.0f;
				attackCapacity = 20;
				headshotMultiplier = 1.0f;
				break;
		}

		if (emitSignal)
			EmitSignal(nameof(WeaponChanged), selectedWeapon);
	}

	private void ApplyPerkEffects(PerkDefinition perk)
	{
		if (perk.Modifiers == null)
			return;

		foreach (var modifier in perk.Modifiers)
		{
			if (modifier == null)
				continue;

			ApplyStatModifier(modifier);
		}
	}

	private void ApplyStatModifier(PerkStatModifier modifier)
	{
		switch (modifier.Target)
		{
			case PlayerStatTarget.MaxHealthMultiplier:
				maxHealthMultiplier = ApplyMultiplierModifier(maxHealthMultiplier, modifier);
				break;
			case PlayerStatTarget.DamageMultiplier:
				damageMultiplier = ApplyMultiplierModifier(damageMultiplier, modifier);
				break;
			case PlayerStatTarget.MoveSpeedMultiplier:
				moveSpeedMultiplier = ApplyMultiplierModifier(moveSpeedMultiplier, modifier);
				break;
			case PlayerStatTarget.RangeMultiplier:
				rangeMultiplier = ApplyMultiplierModifier(rangeMultiplier, modifier);
				break;
			case PlayerStatTarget.TotalJumps:
				totalJumps = ApplyIntModifier(totalJumps, modifier);
				break;
			case PlayerStatTarget.ProjectileSpeed:
				projectileSpeed = ApplyFloatModifier(projectileSpeed, modifier);
				break;
			case PlayerStatTarget.ProjectileSpeedMultiplier:
				projectileSpeedMultiplier = ApplyMultiplierModifier(projectileSpeedMultiplier, modifier);
				break;
			case PlayerStatTarget.AttackSpeedMultiplier:
				attackSpeedMultiplier = ApplyMultiplierModifier(attackSpeedMultiplier, modifier);
				break;
			case PlayerStatTarget.Knockback:
				knockback = ApplyFloatModifier(knockback, modifier);
				break;
			case PlayerStatTarget.HeadshotMultiplier:
				headshotMultiplier = ApplyFloatModifier(headshotMultiplier, modifier);
				break;
			case PlayerStatTarget.AttackCapacity:
				attackCapacity = ApplyIntModifier(attackCapacity, modifier);
				break;

		}
	}

	private float ApplyFloatModifier(float currentValue, PerkStatModifier modifier)
	{
		return modifier.Operation switch
		{
			PerkModifierOperation.Add => currentValue + modifier.FloatValue,
			PerkModifierOperation.PercentAdd => currentValue + modifier.FloatValue,
			PerkModifierOperation.Multiply => currentValue * modifier.FloatValue,
			PerkModifierOperation.Set => modifier.FloatValue,
			_ => currentValue
		};
	}

	private float ApplyMultiplierModifier(float currentValue, PerkStatModifier modifier)
	{
		return modifier.Operation switch
		{
			PerkModifierOperation.Add => currentValue + modifier.FloatValue,
			PerkModifierOperation.PercentAdd => currentValue + modifier.FloatValue,
			PerkModifierOperation.Multiply => currentValue * modifier.FloatValue,
			PerkModifierOperation.Set => modifier.FloatValue,
			_ => currentValue
		};
	}

	private int ApplyIntModifier(int currentValue, PerkStatModifier modifier)
	{
		return modifier.Operation switch
		{
			PerkModifierOperation.Add => currentValue + modifier.IntValue,
			PerkModifierOperation.Multiply => Mathf.RoundToInt(currentValue * modifier.FloatValue),
			PerkModifierOperation.Set => modifier.IntValue,
			_ => currentValue
		};
	}

	private bool ApplyBoolModifier(bool currentValue, PerkStatModifier modifier)
	{
		return modifier.Operation switch
		{
			PerkModifierOperation.Set => modifier.BoolValue,
			_ => currentValue
		};
	}

	private void ClampVitals()
	{
		_currentHealth = Mathf.Clamp(_currentHealth, 0f, MaxHealth);
		_currentStamina = Mathf.Clamp(_currentStamina, 0f, maxStamina);
		EmitSignal(nameof(HealthChanged), _currentHealth, MaxHealth);
		EmitSignal(nameof(StaminaChanged), _currentStamina, maxStamina);
	}

	public void ResetHealth()
	{
		SetHealth(MaxHealth);
	}

	public void SetHealth(float value)
	{
		ApplyHealth(value);
	}

	public void TakeDamage(float amount)
	{
		if (amount <= 0f)
			return;

		ApplyHealth(_currentHealth - amount);
		SyncHealthAcrossPeers();
	}

	public void Heal(float amount)
	{
		if (amount <= 0f)
			return;

		ApplyHealth(_currentHealth + amount);
		SyncHealthAcrossPeers();
	}

	public void ResetStamina()
	{
		SetStamina(maxStamina);
	}

	public void SetStamina(float value)
	{
		ApplyStamina(value);
	}

	public void TickStamina(bool isSprinting, float delta)
	{
		if (delta <= 0f)
			return;

		var nextValue = _currentStamina;
		if (isSprinting)
			nextValue -= staminaDrainPerSecond * delta;
		else
			nextValue += staminaRegenPerSecond * delta;

		ApplyStamina(nextValue);
	}

	private void ApplyHealth(float value)
	{
		var clamped = Mathf.Clamp(value, 0f, MaxHealth);
		if (Mathf.IsEqualApprox(_currentHealth, clamped))
			return;

		_currentHealth = clamped;
		EmitSignal(nameof(HealthChanged), _currentHealth, MaxHealth);
	}

	private void SyncHealthAcrossPeers()
	{
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer())
			return;

		Rpc(nameof(SyncHealthRpc), _currentHealth);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void SyncHealthRpc(float currentHealth)
	{
		ApplyHealth(currentHealth);
	}

	private void ApplyStamina(float value)
	{
		var clamped = Mathf.Clamp(value, 0f, maxStamina);
		if (Mathf.IsEqualApprox(_currentStamina, clamped))
			return;

		_currentStamina = clamped;
		EmitSignal(nameof(StaminaChanged), _currentStamina, maxStamina);
	}

}
