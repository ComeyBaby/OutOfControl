using Godot;

public partial class PlayerStats : Node
{
	[Signal] public delegate void HealthChangedEventHandler(int currentHealth, int maxHealth);
	[Signal] public delegate void StaminaChangedEventHandler(float currentStamina, float maxStamina);

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

	[ExportGroup("Stats")]
	[Export] public int maxHealth = 100;
	[Export] public int maxStamina = 25;
	[Export] public float staminaDrainPerSecond = 10.0f;
	[Export] public float staminaRegenPerSecond = 8.0f;
	[Export] public int attackSpeedMultiplier = 5;
	[Export] public int defence = 0;
	[Export] public int armor = 0;
	[Export] public int damageMultiplier = 1;
	[Export] public int reach = 5;
	[Export] public bool debugGun = false;

	private int _currentHealth;
	private float _currentStamina;

	public int CurrentHealth => _currentHealth;
	public int MaxHealth => Mathf.Max(1, maxHealth);
	public float CurrentStamina => _currentStamina;
	public float MaxStamina => Mathf.Max(1, maxStamina);
	public bool HasStamina => _currentStamina > 0.1f;
	public int GunDamage => Mathf.Max(1, damageMultiplier * 10);
	public float GunRange => Mathf.Max(1f, reach);
	public float GunCooldown => 1f / Mathf.Max(1, attackSpeedMultiplier);

	public override void _Ready()
	{
		ResetHealth();
		ResetStamina();
	}

	public void ResetHealth()
	{
		SetHealth(MaxHealth);
	}

	public void SetHealth(int value)
	{
		ApplyHealth(value);
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0)
			return;

		var mitigation = Mathf.Max(0, defence + armor);
		var actualDamage = Mathf.Max(1, amount - mitigation);
		ApplyHealth(_currentHealth - actualDamage);
		SyncHealthAcrossPeers();
	}

	public void Heal(int amount)
	{
		if (amount <= 0)
			return;

		ApplyHealth(_currentHealth + amount);
		SyncHealthAcrossPeers();
	}

	public void ResetStamina()
	{
		SetStamina(MaxStamina);
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

	private void ApplyHealth(int value)
	{
		var clamped = Mathf.Clamp(value, 0, MaxHealth);
		if (_currentHealth == clamped)
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
	private void SyncHealthRpc(int currentHealth)
	{
		ApplyHealth(currentHealth);
	}

	private void ApplyStamina(float value)
	{
		var clamped = Mathf.Clamp(value, 0f, MaxStamina);
		if (Mathf.IsEqualApprox(_currentStamina, clamped))
			return;

		_currentStamina = clamped;
		EmitSignal(nameof(StaminaChanged), _currentStamina, MaxStamina);
	}

}
