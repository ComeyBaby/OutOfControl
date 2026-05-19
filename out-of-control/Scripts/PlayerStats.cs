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
	public float reloadTime = 1.5f;
	public float knockback = 0.0f;
	public float headshotMultiplier = 1.0f;
	public int attackCapacity = 0;
	public bool debugAttack = false;
	public string selectedWeapon = "Assault";

	private readonly List<PerkDefinition> _appliedPerks = new();
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
		RebuildFromWeaponPreset();
		ClampVitals();
		ClampAmmo();
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
		ClampAmmo();
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
		reloadTime = 1.5f;
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
				reloadTime = 1.4f;
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
				reloadTime = 2.0f;
				knockback = 2.0f;
				attackCapacity = 3;
				headshotMultiplier = 2.0f;
				break;
			case "Fists":
				maxHealth = 150.0f;
				attackDamage = 10.0f;
				attackRange = 1.5f;
				attackSpeed = 0.65f;
				knockback = 0.8f;
				break;
			case "Sword":
				maxHealth = 125.0f;
				attackDamage = 20.0f;
				attackRange = 3.0f;
				attackSpeed = 1.0f;
				knockback = 1.4f;
				break;
			case "Staff":
				maxHealth = 100.0f;
				attackDamage = 14.0f;
				attackRange = 20.0f;
				projectileSpeed = 55.0f;
				attackSpeed = 1.0f;
				reloadTime = 1.6f;
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
