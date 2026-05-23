using Godot;
using System.Collections.Generic;

public partial class PlayerStats : Node
{
	[Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
	[Signal] public delegate void StaminaChangedEventHandler(float currentStamina, float maxStamina);
	[Signal] public delegate void WeaponChangedEventHandler(string weapon);
	[Signal] public delegate void AmmoChangedEventHandler(int currentAmmo, int maxAmmo, bool isReloading, float reloadRemaining);
	[Signal] public delegate void ShotFiredEventHandler();
	[Signal] public delegate void ReloadStartedEventHandler();

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
	[Export] public float networkSyncRate = 40.0f;
	[Export] public float remotePositionSmoothing = 14.0f;
	[Export] public float remoteRotationSmoothing = 14.0f;

	[ExportGroup("Weapon Handling")]
	[Export] public float assaultBloomPerShot = 0.5f;
	[Export] public float assaultBloomMax = 4.6f;
	[Export] public float assaultBloomRecoverPerSecond = 5.6f;
	[Export] public float assaultBloomResetWindowSeconds = 0.18f;
	[Export] public float assaultRecoilPitchDegrees = 1.08f;
	[Export] public float assaultRecoilYawDegrees = 0.6f;
	[Export] public float sniperRecoilPitchDegrees = 3.8f;
	[Export] public float sniperRecoilYawDegrees = 0.35f;

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
	public float reloadTime = 1.5f;
	public float knockback = 0.0f;
	public float headshotMultiplier = 1.0f;
	public int attackCapacity = 0;
	public int airDashCharges = 0;
	public float airDashSpeed = 18.0f;
	public float glideGravityMultiplier = 1.0f;
	public float groundPoundDamage = 0.0f;
	public float groundPoundRadius = 0.0f;
	public float lifeStealPercent = 0.0f;
	public int ricochetCount = 0;
	public int pierceCount = 0;
	public float blinkDistance = 0.0f;
	public float blinkCooldown = 0.0f;
	public int wallJumpCount = 0;
	public float wallJumpPush = 10.0f;
	public float momentumJumpBoost = 0.0f;
	public float landingShockwaveDamage = 0.0f;
	public float landingShockwaveRadius = 0.0f;
	public float landingShockwaveMinFallSpeed = 11.0f;
	public bool debugAttack = false;
	public string selectedWeapon = Weapons.Assault;

	private readonly List<PerkDefinition> _appliedPerks = new();
	private readonly Dictionary<PerkUseTrigger, int> _remainingPerkUses = new();
	private float _currentHealth;
	private float _currentStamina;
	private int _currentAmmo;
	private double _reloadEndTime = -1.0;

	public float CurrentHealth => _currentHealth;
	public float MaxHealth => Mathf.Max(0f, maxHealth * maxHealthMultiplier);
	public float CurrentStamina => _currentStamina;
	public float MaxStamina => maxStamina;
	public bool HasStamina => _currentStamina > 0f;
	public int MaxAmmo => Mathf.Max(0, attackCapacity);
	public bool HasAmmoSystem => MaxAmmo > 0;
	public int CurrentAmmo
	{
		get
		{
			SyncAmmoState();
			return HasAmmoSystem ? _currentAmmo : -1;
		}
	}
	public bool IsReloading
	{
		get
		{
			SyncAmmoState();
			return HasAmmoSystem && _reloadEndTime > 0.0;
		}
	}
	public float ReloadRemaining
	{
		get
		{
			SyncAmmoState();
			if (!HasAmmoSystem || _reloadEndTime <= 0.0)
				return 0f;

			return Mathf.Max(0f, (float)(_reloadEndTime - GetNowSeconds()));
		}
	}
	public float ReloadDuration => Mathf.Max(0f, reloadTime);
	public float AttackCooldown
	{
		get
		{
			var effectiveSpeedMultiplier = Mathf.Max(0.0001f, attackSpeedMultiplier);
			return attackSpeed <= 0f ? 0f : attackSpeed / effectiveSpeedMultiplier;
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
		ResetAmmo();
	}

	public void SetWeapon(string weapon)
	{
		ApplyWeaponPreset(weapon, true);
		ReapplyPerks();
		ResetHealth();
		ResetStamina();
		ResetAmmo();
	}

	public void ApplyPerk(PerkDefinition perk)
	{
		if (perk == null)
			return;

		_appliedPerks.Add(perk);
		RegisterPerkUsageBudget(perk);
		ApplyPerkEffects(perk);
		ClampVitals();
		ClampAmmo();
	}

	public void ApplyPerkModifiersFromNetwork(
		Godot.Collections.Array<int> targets,
		Godot.Collections.Array<int> operations,
		Godot.Collections.Array<float> floatValues,
		Godot.Collections.Array<int> intValues,
		Godot.Collections.Array<bool> boolValues)
	{
		if (targets == null || operations == null || floatValues == null || intValues == null || boolValues == null)
			return;

		var count = targets.Count;
		if (count == 0)
			return;

		if (operations.Count != count || floatValues.Count != count || intValues.Count != count || boolValues.Count != count)
			return;

		for (int i = 0; i < count; i++)
		{
			var target = (PlayerStatTarget)targets[i];
			var operation = (PerkModifierOperation)operations[i];
			var floatValue = floatValues[i];
			var intValue = intValues[i];

			ApplyStatModifierValues(target, operation, floatValue, intValue);
		}

		ClampVitals();
		ClampAmmo();
	}

	public void ClearPerks()
	{
		if (_appliedPerks.Count == 0)
			return;

		_appliedPerks.Clear();
		_remainingPerkUses.Clear();
		RebuildFromWeaponPreset();
		ClampVitals();
		ClampAmmo();
	}

	public void ReapplyPerks()
	{
		RebuildFromWeaponPreset();
		_remainingPerkUses.Clear();

		foreach (var perk in _appliedPerks)
		{
			if (perk != null)
			{
				RegisterPerkUsageBudget(perk);
				ApplyPerkEffects(perk);
			}
		}

		ClampVitals();
		ClampAmmo();
	}

	private void RebuildFromWeaponPreset()
	{
		ApplyWeaponPreset(selectedWeapon, false);
	}

	private void RegisterPerkUsageBudget(PerkDefinition perk)
	{
		if (perk == null || !perk.IsLimitedUse)
			return;

		var trigger = perk.UseTrigger;
		var usesToAdd = Mathf.Max(0, perk.MaxUses);
		if (usesToAdd <= 0)
			return;

		var existing = GetPerkUsesRemaining(trigger);
		_remainingPerkUses[trigger] = existing + usesToAdd;
	}

	public int GetPerkUsesRemaining(PerkUseTrigger trigger)
	{
		if (trigger == PerkUseTrigger.None)
			return int.MaxValue;

		return _remainingPerkUses.TryGetValue(trigger, out var remaining) ? Mathf.Max(0, remaining) : -1;
	}

	public bool CanUsePerkTrigger(PerkUseTrigger trigger)
	{
		if (trigger == PerkUseTrigger.None)
			return true;

		var remaining = GetPerkUsesRemaining(trigger);
		// -1 means no perk-specific limit configured for this trigger.
		return remaining != 0;
	}

	public bool ConsumePerkUse(PerkUseTrigger trigger)
	{
		if (trigger == PerkUseTrigger.None)
			return true;

		var remaining = GetPerkUsesRemaining(trigger);
		if (remaining < 0)
			return true;
		if (remaining <= 0)
			return false;

		_remainingPerkUses[trigger] = remaining - 1;
		return true;
	}

	private void ApplyWeaponPreset(string weapon, bool emitSignal)
	{
		selectedWeapon = Weapons.Normalize(weapon);

		maxHealth = 100.0f;
		attackDamage = 10.0f;
		attackRange = 5.0f;
		damageMultiplier = 1.0f;
		rangeMultiplier = 1.0f;
		projectileSpeed = 1.0f;
		projectileSpeedMultiplier = 1.0f;
		attackSpeed = 1.0f;
		attackSpeedMultiplier = 1.0f;
		reloadTime = 1.5f;
		knockback = 1.0f;
		headshotMultiplier = 1.0f;
		attackCapacity = 0;
		airDashCharges = 0;
		airDashSpeed = 18.0f;
		glideGravityMultiplier = 1.0f;
		groundPoundDamage = 0.0f;
		groundPoundRadius = 0.0f;
		lifeStealPercent = 0.0f;
		ricochetCount = 0;
		pierceCount = 0;
		blinkDistance = 0.0f;
		blinkCooldown = 0.0f;
		wallJumpCount = 0;
		wallJumpPush = 10.0f;
		momentumJumpBoost = 0.0f;
		landingShockwaveDamage = 0.0f;
		landingShockwaveRadius = 0.0f;
		landingShockwaveMinFallSpeed = 11.0f;
		assaultBloomPerShot = 0.5f;
		assaultBloomMax = 4.6f;
		assaultBloomRecoverPerSecond = 5.6f;
		assaultBloomResetWindowSeconds = 0.18f;
		assaultRecoilPitchDegrees = 1.08f;
		assaultRecoilYawDegrees = 0.6f;
		sniperRecoilPitchDegrees = 3.8f;
		sniperRecoilYawDegrees = 0.35f;

		switch (selectedWeapon)
		{
			case Weapons.Assault:
				maxHealth = 90.0f;
				attackDamage = 7.0f;
				attackRange = 40.0f;
				projectileSpeed = 65.0f;
				attackSpeed = 0.115f;
				attackSpeedMultiplier = 1.0f;
				reloadTime = 1.55f;
				knockback = 1.0f;
				attackCapacity = 28;
				headshotMultiplier = 1.3f;
				assaultBloomPerShot = 0.5f;
				assaultBloomMax = 4.6f;
				assaultBloomRecoverPerSecond = 5.6f;
				assaultBloomResetWindowSeconds = 0.18f;
				assaultRecoilPitchDegrees = 1.08f;
				assaultRecoilYawDegrees = 0.6f;
				break;
			case Weapons.Sniper:
				maxHealth = 80.0f;
				attackDamage = 34.0f;
				attackRange = 80.0f;
				projectileSpeed = 120.0f;
				attackSpeed = 1.35f;
				attackSpeedMultiplier = 1.0f;
				reloadTime = 2.25f;
				knockback = 2.0f;
				attackCapacity = 3;
				headshotMultiplier = 2.0f;
				sniperRecoilPitchDegrees = 15.0f;
				sniperRecoilYawDegrees = 0.35f;
				break;
			case Weapons.Fists:
				maxHealth = 155.0f;
				attackDamage = 12.0f;
				attackRange = 1.5f;
				attackSpeed = 0.55f;
				baseSpeed = 9.0f;
				sprintSpeed = 13.0f;
				knockback = 0.8f;
				break;
			case Weapons.Sword:
				maxHealth = 120.0f;
				attackDamage = 22.0f;
				attackRange = 2.6f;
				attackSpeed = 0.72f;
				baseSpeed = 8.5f;
				sprintSpeed = 12.0f;
				knockback = 1.8f;
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
		ApplyStatModifierValues(modifier.Target, modifier.Operation, modifier.FloatValue, modifier.IntValue);
	}

	private void ApplyStatModifierValues(PlayerStatTarget target, PerkModifierOperation operation, float floatValue, int intValue)
	{
		switch (target)
		{
			case PlayerStatTarget.MaxHealthMultiplier:
				maxHealthMultiplier = ApplyFloatModifier(maxHealthMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.DamageMultiplier:
				damageMultiplier = ApplyFloatModifier(damageMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.MoveSpeedMultiplier:
				moveSpeedMultiplier = ApplyFloatModifier(moveSpeedMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.RangeMultiplier:
				rangeMultiplier = ApplyFloatModifier(rangeMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.TotalJumps:
				totalJumps = ApplyIntModifier(totalJumps, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.ProjectileSpeed:
				projectileSpeed = ApplyFloatModifier(projectileSpeed, operation, floatValue);
				break;
			case PlayerStatTarget.ProjectileSpeedMultiplier:
				projectileSpeedMultiplier = ApplyFloatModifier(projectileSpeedMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.AttackSpeedMultiplier:
				attackSpeedMultiplier = ApplyFloatModifier(attackSpeedMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.Knockback:
				knockback = ApplyFloatModifier(knockback, operation, floatValue);
				break;
			case PlayerStatTarget.HeadshotMultiplier:
				headshotMultiplier = ApplyFloatModifier(headshotMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.AttackCapacity:
				attackCapacity = ApplyIntModifier(attackCapacity, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.AirDashCharges:
				airDashCharges = ApplyIntModifier(airDashCharges, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.AirDashSpeed:
				airDashSpeed = ApplyFloatModifier(airDashSpeed, operation, floatValue);
				break;
			case PlayerStatTarget.GlideGravityMultiplier:
				glideGravityMultiplier = ApplyFloatModifier(glideGravityMultiplier, operation, floatValue);
				break;
			case PlayerStatTarget.GroundPoundDamage:
				groundPoundDamage = ApplyFloatModifier(groundPoundDamage, operation, floatValue);
				break;
			case PlayerStatTarget.GroundPoundRadius:
				groundPoundRadius = ApplyFloatModifier(groundPoundRadius, operation, floatValue);
				break;
			case PlayerStatTarget.LifeStealPercent:
				lifeStealPercent = ApplyFloatModifier(lifeStealPercent, operation, floatValue);
				break;
			case PlayerStatTarget.RicochetCount:
				ricochetCount = ApplyIntModifier(ricochetCount, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.PierceCount:
				pierceCount = ApplyIntModifier(pierceCount, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.BlinkDistance:
				blinkDistance = ApplyFloatModifier(blinkDistance, operation, floatValue);
				break;
			case PlayerStatTarget.BlinkCooldown:
				blinkCooldown = ApplyFloatModifier(blinkCooldown, operation, floatValue);
				break;
			case PlayerStatTarget.WallJumpCount:
				wallJumpCount = ApplyIntModifier(wallJumpCount, operation, intValue, floatValue);
				break;
			case PlayerStatTarget.WallJumpPush:
				wallJumpPush = ApplyFloatModifier(wallJumpPush, operation, floatValue);
				break;
			case PlayerStatTarget.MomentumJumpBoost:
				momentumJumpBoost = ApplyFloatModifier(momentumJumpBoost, operation, floatValue);
				break;
			case PlayerStatTarget.LandingShockwaveDamage:
				landingShockwaveDamage = ApplyFloatModifier(landingShockwaveDamage, operation, floatValue);
				break;
			case PlayerStatTarget.LandingShockwaveRadius:
				landingShockwaveRadius = ApplyFloatModifier(landingShockwaveRadius, operation, floatValue);
				break;
			case PlayerStatTarget.LandingShockwaveMinFallSpeed:
				landingShockwaveMinFallSpeed = ApplyFloatModifier(landingShockwaveMinFallSpeed, operation, floatValue);
				break;

		}
	}

	private float ApplyFloatModifier(float currentValue, PerkStatModifier modifier)
	{
		return ApplyFloatModifier(currentValue, modifier.Operation, modifier.FloatValue);
	}

	private float ApplyFloatModifier(float currentValue, PerkModifierOperation operation, float value)
	{
		return operation switch
		{
			PerkModifierOperation.Add => currentValue + value,
			PerkModifierOperation.PercentAdd => currentValue + value,
			PerkModifierOperation.Multiply => currentValue * value,
			PerkModifierOperation.Set => value,
			_ => currentValue
		};
	}

	private int ApplyIntModifier(int currentValue, PerkStatModifier modifier)
	{
		return ApplyIntModifier(currentValue, modifier.Operation, modifier.IntValue, modifier.FloatValue);
	}

	private int ApplyIntModifier(int currentValue, PerkModifierOperation operation, int intValue, float floatValue)
	{
		return operation switch
		{
			PerkModifierOperation.Add => currentValue + intValue,
			PerkModifierOperation.Multiply => Mathf.RoundToInt(currentValue * floatValue),
			PerkModifierOperation.Set => intValue,
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

	private void ClampAmmo()
	{
		SyncAmmoState();
		if (!HasAmmoSystem)
		{
			_currentAmmo = -1;
			_reloadEndTime = -1.0;
			EmitSignal(nameof(AmmoChanged), _currentAmmo, MaxAmmo, false, 0f);
			return;
		}

		_currentAmmo = Mathf.Clamp(_currentAmmo, 0, MaxAmmo);
		EmitAmmoChanged();
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

	public void ResetAmmo()
	{
		if (!HasAmmoSystem)
		{
			_currentAmmo = -1;
			_reloadEndTime = -1.0;
			EmitSignal(nameof(AmmoChanged), _currentAmmo, MaxAmmo, false, 0f);
			return;
		}

		_currentAmmo = MaxAmmo;
		_reloadEndTime = -1.0;
		EmitAmmoChanged();
	}

	public void SetStamina(float value)
	{
		ApplyStamina(value);
	}

	public bool TrySpendAmmo()
	{
		SyncAmmoState();

		if (!HasAmmoSystem)
			return true;

		if (IsReloading)
			return false;

		if (_currentAmmo <= 0)
		{
			BeginReload();
			return false;
		}

		_currentAmmo--;
		EmitSignal(nameof(ShotFired));
		if (_currentAmmo <= 0)
			BeginReload();
		else
			EmitAmmoChanged();

		return true;
	}

	public bool TryStartReload()
	{
		SyncAmmoState();
		if (!HasAmmoSystem)
			return false;
		if (IsReloading)
			return false;
		if (_currentAmmo >= MaxAmmo)
			return false;

		BeginReload();
		return true;
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

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SyncHealthRpc(float currentHealth)
	{
		if (Multiplayer.MultiplayerPeer != null && Multiplayer.GetRemoteSenderId() != 1)
			return;

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

	private void BeginReload()
	{
		if (!HasAmmoSystem)
			return;

		_reloadEndTime = GetNowSeconds() + ReloadDuration;
		EmitSignal(nameof(ReloadStarted));
		EmitAmmoChanged();
	}

	private void SyncAmmoState()
	{
		if (!HasAmmoSystem)
			return;

		if (_reloadEndTime <= 0.0)
			return;

		if (GetNowSeconds() < _reloadEndTime)
			return;

		_currentAmmo = MaxAmmo;
		_reloadEndTime = -1.0;
		EmitAmmoChanged();
	}

	private double GetNowSeconds()
	{
		return Time.GetTicksMsec() / 1000.0;
	}

	private void EmitAmmoChanged()
	{
		var isReloading = HasAmmoSystem && _reloadEndTime > 0.0;
		var remaining = isReloading ? Mathf.Max(0f, (float)(_reloadEndTime - GetNowSeconds())) : 0f;
		EmitSignal(nameof(AmmoChanged), _currentAmmo, MaxAmmo, isReloading, remaining);
	}

}
