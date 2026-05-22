using Godot;

public partial class PlayerController : CharacterBody3D
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string RoundManagerNodeName = "RoundManager";

	[Export] private Node3D _head;
	[Export] private MeshInstance3D _mesh;
	[Export] private Label3D _nameLabel;
	[Export] private CollisionShape3D _collider;
	[Export] private Area3D _meleeHurtbox;
	[Export] private CollisionShape3D _meleeHurtboxShape;
	[Export] private PlayerStats _stats;
	[Export] private Camera3D _camera;
	[Export] private AudioStreamPlayer3D _fireAudioPlayer;
	[Export] private AudioStreamPlayer3D _reloadAudioPlayer;
	[Export] private AudioStream _assaultFireClip;
	[Export] private AudioStream _sniperFireClip;
	[Export] private AudioStream _assaultReloadClip;
	[Export] private AudioStream _sniperReloadClip;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private Callable _roundChangedCallable;
	private Callable _shotFiredCallable;
	private Callable _reloadStartedCallable;
	private Callable _weaponChangedCallable;
	private readonly RandomNumberGenerator _audioRng = new();

	private Vector2 _lookRotation;
	private Vector2 _pendingLookDelta;
	private float _moveSpeed = 0f;
	private bool _freeflying = false;
	private bool _mouseButtonPressed = false;

	private Vector3 _netTargetPosition;
	private Vector3 _netTargetRotation;
	private bool _hasNetTarget = false;
	private double _networkSyncAccumulator = 0.0;

	private string _displayName = "";
	private double _lastShotTime = -999.0;
	private bool _controlsEnabled = true;
	private bool _pauseControlsLocked = false;
	private bool _isDead = false;
	private int _remainingAirJumps = 0;
	private int _remainingAirDashes = 0;
	private int _remainingWallJumps = 0;
	private double _lastAirDashTime = -999.0;
	private double _lastBlinkTime = -999.0;
	private float _dashLockTimeRemaining = 0f;
	private bool _isGroundPounding = false;
	private float _fallSpeedBeforeLanding = 0f;
	private bool _wasOnFloor = false;
	private RoundPhase _lastRoundPhase = RoundPhase.Lobby;
	private Input.MouseModeEnum? _appliedMouseMode;
	private bool _hasVisibleInteractiveUiCached = false;
	private float _uiVisibilityRefreshAccumulator = 0.0f;
	private const float UiVisibilityRefreshIntervalSeconds = 0.12f;

	private bool CanMove => _stats?.canMove ?? true;
	private bool HasGravity => _stats?.hasGravity ?? true;
	private bool CanJump => _stats?.canJump ?? true;
	private bool CanSprint => _stats?.canSprint ?? false;
	private bool CanFreefly => _stats?.canFreefly ?? false;

	private float LookSpeed => _stats?.lookSpeed ?? 0.002f;
	private float MoveSpeedMultiplier => _stats?.moveSpeedMultiplier ?? 1.0f;
	private float BaseSpeed => (_stats?.baseSpeed ?? 7.0f) * MoveSpeedMultiplier;
	private float JumpVelocity => _stats?.jumpVelocity ?? 4.5f;
	private float SprintSpeed => (_stats?.sprintSpeed ?? 10.0f) * MoveSpeedMultiplier;
	private float FreeflySpeed => (_stats?.freeflySpeed ?? 25.0f) * MoveSpeedMultiplier;

	private string InputLeft => _stats?.inputLeft ?? "move_left";
	private string InputRight => _stats?.inputRight ?? "move_right";
	private string InputForward => _stats?.inputForward ?? "move_forward";
	private string InputBack => _stats?.inputBack ?? "move_backward";
	private string InputJump => _stats?.inputJump ?? "jump";
	private string InputSprint => _stats?.inputSprint ?? "sprint";
	private string InputFreefly => _stats?.inputFreefly ?? "freefly";
	private string InputShoot => "shoot";
	private string InputReload => "reload";

	private float NetworkSyncRate => _stats?.networkSyncRate ?? 100.0f;
	private float RemotePositionSmoothing => _stats?.remotePositionSmoothing ?? 14.0f;
	private float RemoteRotationSmoothing => _stats?.remoteRotationSmoothing ?? 14.0f;

	public override void _Ready()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		_roundManager = _networkManager?.GetRoundManager()
			?? GetTree().Root.GetNodeOrNull<RoundManager>(RoundManagerNodeName);
		_roundChangedCallable = new Callable(this, nameof(OnRoundChanged));
		if (_roundManager != null && !_roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Connect(nameof(RoundManager.RoundChanged), _roundChangedCallable);

		_lookRotation.Y = Rotation.Y;
		_lookRotation.X = _head.Rotation.X;
		ResetPhysicsInterpolation();
		_wasOnFloor = IsOnFloor();
		ResetJumpState();
		if (string.IsNullOrWhiteSpace(_displayName) && _nameLabel != null)
			_displayName = _nameLabel.Text;
		ApplyDisplayName();
		if (_stats != null)
		{
			var healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
			if (!_stats.IsConnected(nameof(PlayerStats.HealthChanged), healthChangedCallable))
				_stats.Connect(nameof(PlayerStats.HealthChanged), healthChangedCallable);
			_shotFiredCallable = new Callable(this, nameof(OnShotFired));
			if (!_stats.IsConnected(nameof(PlayerStats.ShotFired), _shotFiredCallable))
				_stats.Connect(nameof(PlayerStats.ShotFired), _shotFiredCallable);
			_reloadStartedCallable = new Callable(this, nameof(OnReloadStarted));
			if (!_stats.IsConnected(nameof(PlayerStats.ReloadStarted), _reloadStartedCallable))
				_stats.Connect(nameof(PlayerStats.ReloadStarted), _reloadStartedCallable);
			_weaponChangedCallable = new Callable(this, nameof(OnWeaponChanged));
			if (!_stats.IsConnected(nameof(PlayerStats.WeaponChanged), _weaponChangedCallable))
				_stats.Connect(nameof(PlayerStats.WeaponChanged), _weaponChangedCallable);
			OnWeaponChanged(_stats.SelectedWeapon);
			OnHealthChanged(_stats.CurrentHealth, _stats.MaxHealth);
		}
		RefreshAuthorityState();
	}

	public override void _ExitTree()
	{
		if (_roundManager != null && _roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Disconnect(nameof(RoundManager.RoundChanged), _roundChangedCallable);

		if (_stats != null)
		{
			var healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
			if (_stats.IsConnected(nameof(PlayerStats.HealthChanged), healthChangedCallable))
				_stats.Disconnect(nameof(PlayerStats.HealthChanged), healthChangedCallable);
			if (_stats.IsConnected(nameof(PlayerStats.ShotFired), _shotFiredCallable))
				_stats.Disconnect(nameof(PlayerStats.ShotFired), _shotFiredCallable);
			if (_stats.IsConnected(nameof(PlayerStats.ReloadStarted), _reloadStartedCallable))
				_stats.Disconnect(nameof(PlayerStats.ReloadStarted), _reloadStartedCallable);
			if (_stats.IsConnected(nameof(PlayerStats.WeaponChanged), _weaponChangedCallable))
				_stats.Disconnect(nameof(PlayerStats.WeaponChanged), _weaponChangedCallable);
		}
	}

	private void OnWeaponChanged(string weapon)
	{
		UpdateMeleeHurtboxState(weapon);
	}

	private void UpdateMeleeHurtboxState(string weapon)
	{
		if (_meleeHurtbox == null)
			return;

		var enabled = !_isDead && IsMeleeWeapon(weapon);
		_meleeHurtbox.Monitorable = enabled;
		_meleeHurtbox.Monitoring = enabled;
		UpdateMeleeHurtboxSize();
	}

	private static bool IsMeleeWeapon(string weapon)
	{
		return weapon == "Fists" || weapon == "Sword";
	}

	private void UpdateMeleeHurtboxSize()
	{
		if (_meleeHurtboxShape == null)
			return;

		if (_meleeHurtboxShape.Shape is SphereShape3D sphere)
		{
			sphere.Radius = Mathf.Max(0.1f, _stats?.AttackRange ?? 1.0f);
			return;
		}

		var fallbackScale = Mathf.Max(0.1f, _stats?.AttackRange ?? 1.0f);
		_meleeHurtboxShape.Scale = new Vector3(fallbackScale, fallbackScale, fallbackScale);
	}

	private void OnShotFired()
	{
		if (_fireAudioPlayer == null)
			return;

		var clip = GetFireClipForCurrentWeapon();
		PlayClip(_fireAudioPlayer, clip, randomize: true);
	}

	private void OnReloadStarted()
	{
		if (_reloadAudioPlayer == null)
			return;

		var clip = GetReloadClipForCurrentWeapon();
		PlayClip(_reloadAudioPlayer, clip, randomize: false);
	}

	private void PlayClip(AudioStreamPlayer3D player, AudioStream clip, bool randomize)
	{
		if (player == null)
			return;

		if (clip != null)
			player.Stream = clip;

		if (player.Stream == null)
			return;

		if (randomize)
		{
			player.PitchScale = _audioRng.RandfRange(0.97f, 1.03f);
			player.VolumeDb = _audioRng.RandfRange(-1.5f, 1.5f);
			var startOffsetSeconds = _audioRng.RandfRange(0.0f, 0.008f);
			player.Play(startOffsetSeconds);
			return;
		}

		player.PitchScale = 1.0f;
		player.VolumeDb = 0.0f;
		player.Play();
	}

	private AudioStream GetFireClipForCurrentWeapon()
	{
		var weapon = _stats?.SelectedWeapon;
		return weapon switch
		{
			"Sniper" => _sniperFireClip ?? _assaultFireClip,
			"Assault" => _assaultFireClip ?? _sniperFireClip,
			_ => _assaultFireClip ?? _sniperFireClip
		};
	}

	private AudioStream GetReloadClipForCurrentWeapon()
	{
		var weapon = _stats?.SelectedWeapon;
		return weapon switch
		{
			"Sniper" => _sniperReloadClip ?? _assaultReloadClip,
			"Assault" => _assaultReloadClip ?? _sniperReloadClip,
			_ => _assaultReloadClip ?? _sniperReloadClip
		};
	}

	private void OnRoundChanged()
	{
		if (Multiplayer.MultiplayerPeer == null || !HasLocalAuthority())
			return;

		var roundManager = _roundManager;
		if (roundManager != null)
		{
			var phase = roundManager.Phase;
			var enteredPlaying = _lastRoundPhase != RoundPhase.Playing && phase == RoundPhase.Playing;
			_lastRoundPhase = phase;

			long peerId = Multiplayer.GetUniqueId();
			bool isAlive = roundManager.IsAlive(peerId);
			SetDeadVisualState(!isAlive);
			if (!isAlive && _stats != null && _stats.CurrentHealth > 0f)
				_stats.TakeDamage(_stats.MaxHealth + 9999f);

			if (isAlive && enteredPlaying && _stats != null)
				_stats.ResetHealth();
		}

		UpdateMouseCaptureForControlState();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!HasLocalAuthority())
			return;

		if (!AreControlsActive())
		{
			return;
		}

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_mouseButtonPressed = mouseButton.Pressed;
			}
		}

		if (@event is InputEventMouseMotion motion)
			_pendingLookDelta += motion.Relative;

		if (CanFreefly && Input.IsActionJustPressed(InputFreefly))
		{
			if (!_freeflying)
				EnableFreefly();
			else
				DisableFreefly();
		}

		_uiVisibilityRefreshAccumulator = 0.0f;
	}

	public override void _PhysicsProcess(double delta)
	{
		float d = (float)delta;

		if (!HasLocalAuthority())
		{
			if (_hasNetTarget)
			{
				float posAlpha = 1f - Mathf.Exp(-RemotePositionSmoothing * d);
				float rotAlpha = 1f - Mathf.Exp(-RemoteRotationSmoothing * d);
				GlobalPosition = GlobalPosition.Lerp(_netTargetPosition, posAlpha);
				Rotation = new Vector3(
					Mathf.LerpAngle(Rotation.X, _netTargetRotation.X, rotAlpha),
					Mathf.LerpAngle(Rotation.Y, _netTargetRotation.Y, rotAlpha),
					Mathf.LerpAngle(Rotation.Z, _netTargetRotation.Z, rotAlpha)
				);
			}
			return;
		}

		if (!AreControlsActive())
		{
			if (HasGravity)
			{
				if (!IsOnFloor())
					Velocity += GetGravity() * d;
			}

			MoveAndSlide();
			SendNetworkTransform(d);
			return;
		}

		if (_pendingLookDelta != Vector2.Zero)
		{
			RotateLook(_pendingLookDelta);
			_pendingLookDelta = Vector2.Zero;
		}

		var wasOnFloor = IsOnFloor();

		if (Input.IsActionJustPressed(InputReload))
		{
			var usedBlink = TryBlinkStep(wasOnFloor);
			var usedGroundPound = TryStartGroundPound(wasOnFloor);
			if (!usedBlink && !usedGroundPound)
				_stats?.TryStartReload();
		}

		if (_mouseButtonPressed)
			TryShoot();

		if (HasGravity && !wasOnFloor)
			ApplyPerkAwareGravity(d);

		if (_dashLockTimeRemaining > 0f)
			_dashLockTimeRemaining = Mathf.Max(0f, _dashLockTimeRemaining - d);

		if (CanFreefly && _freeflying)
		{
			Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
			Vector3 motion = (_head.GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
			motion *= FreeflySpeed * d;
			MoveAndCollide(motion);
			SendNetworkTransform(d);
			return;
		}

		if (!wasOnFloor && Input.IsActionJustPressed(InputSprint))
			TryAirDash();

		if (CanJump)
		{
			if (Input.IsActionJustPressed(InputJump))
			{
				if (wasOnFloor)
				{
					Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
					ApplyMomentumJumpBoost();
					ResetJumpState();
				}
				else if (_remainingAirJumps > 0)
				{
					Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
					_remainingAirJumps--;
				}
				else if (TryWallJump())
				{
					// Wall jump already applied velocity and state.
				}
			}
		}

		var wantsSprint = CanSprint && Input.IsActionPressed(InputSprint) && (_stats?.HasStamina ?? false);
		_stats?.TickStamina(wantsSprint, d);

		if (wantsSprint)
			_moveSpeed = SprintSpeed;
		else
			_moveSpeed = BaseSpeed;

		if (_dashLockTimeRemaining > 0f)
		{
			// Preserve dash momentum for a short burst window.
		}
		else if (CanMove)
		{
			Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
			Vector3 moveDir = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

			if (moveDir != Vector3.Zero)
			{
				Velocity = new Vector3(moveDir.X * _moveSpeed, Velocity.Y, moveDir.Z * _moveSpeed);
			}
			else
			{
				Velocity = new Vector3(
					Mathf.MoveToward(Velocity.X, 0, _moveSpeed),
					Velocity.Y,
					Mathf.MoveToward(Velocity.Z, 0, _moveSpeed)
				);
			}
		}
		else
		{
			Velocity = new Vector3(0, Velocity.Y, 0);
		}

		MoveAndSlide();
		var onFloorAfterMove = IsOnFloor();
		var landedThisFrame = !wasOnFloor && onFloorAfterMove;
		if (landedThisFrame)
			HandleLandingEffects();

		_wasOnFloor = onFloorAfterMove;
		if (_wasOnFloor)
			ResetJumpState();
		SendNetworkTransform(d);
	}

	private void ResetJumpState()
	{
		_remainingAirJumps = Mathf.Max(0, GetAllowedJumps() - 1);
		_remainingAirDashes = Mathf.Max(0, _stats?.airDashCharges ?? 0);
		_remainingWallJumps = Mathf.Max(0, _stats?.wallJumpCount ?? 0);
		_dashLockTimeRemaining = 0f;
		_isGroundPounding = false;
		_fallSpeedBeforeLanding = 0f;
	}

	private int GetAllowedJumps()
	{
		return Mathf.Max(1, _stats?.totalJumps ?? 1);
	}

	private bool TryAirDash()
	{
		if (_stats == null || _remainingAirDashes <= 0 || _isGroundPounding)
			return false;

		var now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastAirDashTime < 0.35f)
			return false;

		Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
		Vector3 dashDir = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
		if (dashDir == Vector3.Zero)
		{
			dashDir = -_head.GlobalTransform.Basis.Z;
			dashDir.Y = 0f;
			dashDir = dashDir.Normalized();
		}

		var dashSpeed = Mathf.Max(BaseSpeed * 1.5f, _stats.airDashSpeed);
		Velocity = new Vector3(dashDir.X * dashSpeed, Mathf.Max(Velocity.Y, 0.5f), dashDir.Z * dashSpeed);
		_remainingAirDashes--;
		_lastAirDashTime = now;
		_dashLockTimeRemaining = 0.12f;
		return true;
	}

	private bool TryWallJump()
	{
		if (_stats == null || _remainingWallJumps <= 0 || !IsOnWall())
			return false;

		var wallNormal = GetWallNormal();
		if (wallNormal == Vector3.Zero)
		{
			wallNormal = Transform.Basis.Z;
			wallNormal.Y = 0f;
			wallNormal = wallNormal.Normalized();
		}

		var push = Mathf.Max(4.0f, _stats.wallJumpPush);
		var pushDirection = wallNormal.Normalized();
		Velocity = new Vector3(
			pushDirection.X * push,
			JumpVelocity,
			pushDirection.Z * push
		);
		_remainingWallJumps--;
		return true;
	}

	private void ApplyMomentumJumpBoost()
	{
		if (_stats == null || _stats.momentumJumpBoost <= 0f)
			return;

		Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
		if (inputDir == Vector2.Zero)
			return;

		var horizontalBoost = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
		var boost = Mathf.Max(0f, _stats.momentumJumpBoost);
		Velocity += new Vector3(horizontalBoost.X * boost, 0f, horizontalBoost.Z * boost);
	}

	private bool TryBlinkStep(bool onFloor)
	{
		if (!onFloor || _stats == null || _stats.blinkDistance <= 0f)
			return false;

		// Sprint + reload prevents accidental blink while normal reloading.
		if (!Input.IsActionPressed(InputSprint))
			return false;

		var cooldown = Mathf.Max(0f, _stats.blinkCooldown);
		var now = Time.GetTicksMsec() / 1000.0;
		if (cooldown > 0f && now - _lastBlinkTime < cooldown)
			return false;

		Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
		Vector3 blinkDir = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
		if (blinkDir == Vector3.Zero)
		{
			blinkDir = -_head.GlobalTransform.Basis.Z;
			blinkDir.Y = 0f;
			blinkDir = blinkDir.Normalized();
		}

		var blinkDistance = Mathf.Max(0f, _stats.blinkDistance);
		var start = GlobalPosition + Vector3.Up * 1.0f;
		var end = start + blinkDir * blinkDistance;
		var targetPosition = GlobalPosition + blinkDir * blinkDistance;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(start, end);
		query.CollideWithBodies = true;
		query.CollideWithAreas = false;
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
		var hit = spaceState.IntersectRay(query);
		if (hit.Count > 0 && hit.TryGetValue("position", out var hitPositionValue))
		{
			var hitPosition = hitPositionValue.AsVector3();
			targetPosition = new Vector3(
				hitPosition.X - blinkDir.X * 0.8f,
				GlobalPosition.Y,
				hitPosition.Z - blinkDir.Z * 0.8f
			);
		}

		GlobalPosition = targetPosition;
		Velocity = new Vector3(Velocity.X * 0.2f, Velocity.Y, Velocity.Z * 0.2f);
		_lastBlinkTime = now;
		return true;
	}

	private bool TryStartGroundPound(bool onFloor)
	{
		if (onFloor || _stats == null || _stats.groundPoundDamage <= 0f || _isGroundPounding)
			return false;

		// Sprint + reload prevents accidental ground pound while trying to reload mid-air.
		if (!Input.IsActionPressed(InputSprint))
			return false;

		_isGroundPounding = true;
		Velocity = new Vector3(Velocity.X * 0.4f, -Mathf.Max(22.0f, JumpVelocity * 3.0f), Velocity.Z * 0.4f);
		return true;
	}

	private void ApplyPerkAwareGravity(float delta)
	{
		var gravity = GetGravity();
		var gravityScale = 1.0f;
		if (_stats != null && !_isGroundPounding && Velocity.Y < 0f && Input.IsActionPressed(InputJump))
			gravityScale = Mathf.Clamp(_stats.glideGravityMultiplier, 0.15f, 1.0f);

		if (_isGroundPounding)
			gravityScale *= 1.75f;

		Velocity += gravity * gravityScale * delta;
		if (Velocity.Y < 0f)
			_fallSpeedBeforeLanding = Mathf.Max(_fallSpeedBeforeLanding, -Velocity.Y);
	}

	private void HandleLandingEffects()
	{
		if (_stats == null)
			return;

		if (_isGroundPounding && _stats.groundPoundDamage > 0f && _stats.groundPoundRadius > 0f)
		{
			TriggerLandingShockwave(_stats.groundPoundDamage, _stats.groundPoundRadius, 1.2f);
		}
		else if (_stats.landingShockwaveDamage > 0f && _stats.landingShockwaveRadius > 0f)
		{
			var minFallSpeed = Mathf.Max(0f, _stats.landingShockwaveMinFallSpeed);
			if (_fallSpeedBeforeLanding >= minFallSpeed)
				TriggerLandingShockwave(_stats.landingShockwaveDamage, _stats.landingShockwaveRadius, 0.8f);
		}

		_isGroundPounding = false;
		_fallSpeedBeforeLanding = 0f;
	}

	private void TriggerLandingShockwave(float damage, float radius, float knockbackScale)
	{
		var clampedDamage = Mathf.Max(0f, damage);
		var clampedRadius = Mathf.Max(0f, radius);
		if (clampedDamage <= 0f || clampedRadius <= 0f)
			return;

		if (HasMultiplayerPeer() && !IsServerSession())
		{
			RpcId(1, nameof(RequestLandingShockwaveRpc), GlobalPosition, clampedDamage, clampedRadius, knockbackScale);
			return;
		}

		ApplyLandingShockwaveDamage(GetAuthorityPeerId(), GlobalPosition, clampedDamage, clampedRadius, knockbackScale);
	}

	public override void _Process(double delta)
	{
		_uiVisibilityRefreshAccumulator = Mathf.Max(0f, _uiVisibilityRefreshAccumulator - (float)delta);
		UpdateMouseCaptureForControlState();
	}

	private void RotateLook(Vector2 rotInput)
	{
		_lookRotation.X -= rotInput.Y * LookSpeed;
		_lookRotation.X = Mathf.Clamp(_lookRotation.X, Mathf.DegToRad(-85), Mathf.DegToRad(85));

		_lookRotation.Y -= rotInput.X * LookSpeed;

		Rotation = new Vector3(0, _lookRotation.Y, 0);
		_head.Rotation = new Vector3(_lookRotation.X, 0, 0);
	}

	private void EnableFreefly()
	{
		_collider.Disabled = true;
		_freeflying = true;
		Velocity = Vector3.Zero;
	}

	private void DisableFreefly()
	{
		_collider.Disabled = false;
		_freeflying = false;
	}

	public void SetControlsEnabled(bool enabled)
	{
		if (_controlsEnabled == enabled)
			return;

		_controlsEnabled = enabled;
		if (!_controlsEnabled)
			ResetControlsState();

		UpdateMouseCaptureForControlState();
	}

	public void SetPauseControlsLocked(bool locked)
	{
		if (_pauseControlsLocked == locked)
			return;

		_pauseControlsLocked = locked;

		if (_pauseControlsLocked)
			_pendingLookDelta = Vector2.Zero;

		UpdateMouseCaptureForControlState();
	}

	private bool AreControlsActive()
	{
		return _controlsEnabled
			&& !_pauseControlsLocked
			&& !_isDead
			&& (_networkManager == null || _networkManager.IsRoundAcceptingPlayerInput());
	}

	private void ResetControlsState()
	{
		_pendingLookDelta = Vector2.Zero;
		_mouseButtonPressed = false;
		_moveSpeed = 0f;
		_dashLockTimeRemaining = 0f;
		_isGroundPounding = false;
		_fallSpeedBeforeLanding = 0f;
		Velocity = Vector3.Zero;
		if (_freeflying)
			DisableFreefly();
	}

	private void UpdateMouseCaptureForControlState()
	{
		if (!HasLocalAuthority())
			return;

		var targetMode = HasVisibleInteractiveUi()
			? Input.MouseModeEnum.Visible
			: Input.MouseModeEnum.Captured;

		if (_appliedMouseMode == targetMode && Input.MouseMode == targetMode)
			return;

		Input.MouseMode = targetMode;
		_appliedMouseMode = targetMode;
	}

	private bool HasVisibleInteractiveUi()
	{
		if (_uiVisibilityRefreshAccumulator > 0f)
			return _hasVisibleInteractiveUiCached;

		_hasVisibleInteractiveUiCached = HasVisibleInteractiveUi(this);
		_uiVisibilityRefreshAccumulator = UiVisibilityRefreshIntervalSeconds;
		return _hasVisibleInteractiveUiCached;
	}

	private static bool HasVisibleInteractiveUi(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is BaseButton button && button.IsVisibleInTree())
				return true;

			if (child is Node node && HasVisibleInteractiveUi(node))
				return true;
		}

		return false;
	}

	public void RefreshAuthorityState()
	{
		UpdateMouseCaptureForControlState();
	}

	private bool HasLocalAuthority()
	{
		return Multiplayer.MultiplayerPeer == null || IsMultiplayerAuthority();
	}

	public void SetNetworkTransform(Vector3 position, Vector3 rotation)
	{
		_netTargetPosition = position;
		_netTargetRotation = rotation;
		_hasNetTarget = true;
	}

	public void SetDisplayName(string name)
	{
		var sanitized = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
		if (_displayName == sanitized)
			return;

		_displayName = sanitized;
		ApplyDisplayName();
	}

	private void ApplyDisplayName()
	{
		if (_nameLabel != null)
			_nameLabel.Text = _displayName;
	}

	public PlayerStats GetStats()
	{
		return _stats;
	}

	public bool IsUsingMeleeWeapon()
	{
		return IsMeleeWeapon(_stats?.SelectedWeapon);
	}

	public float GetAttackCooldownRemaining()
	{
		if (_stats == null)
			return 0f;

		var cooldown = Mathf.Max(0f, _stats.AttackCooldown);
		if (cooldown <= 0f)
			return 0f;

		var now = Time.GetTicksMsec() / 1000.0;
		var elapsed = Mathf.Max(0f, (float)(now - _lastShotTime));
		return Mathf.Max(0f, cooldown - elapsed);
	}

	public Camera3D GetViewCamera()
	{
		return _camera;
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		SetDeadVisualState(currentHealth <= 0);
	}

	private void SetDeadVisualState(bool dead)
	{
		if (_isDead == dead)
			return;

		_isDead = dead;

		if (_mesh != null)
			_mesh.Visible = !dead;

		if (_nameLabel != null)
			_nameLabel.Visible = !dead;

		if (_collider != null)
			_collider.Disabled = dead;

		UpdateMeleeHurtboxState(_stats?.SelectedWeapon);
	}

	private void SendNetworkTransform(double delta)
	{
		_networkSyncAccumulator += delta;
		var syncInterval = NetworkSyncRate <= 0.0f ? 0.0333333333333333 : 1.0 / NetworkSyncRate;
		if (_networkSyncAccumulator < syncInterval)
			return;
		_networkSyncAccumulator = 0.0;

		var world = _networkManager;
		if (world == null)
			return;

		if (!HasMultiplayerPeer())
			return;

		if (IsServerSession())
		{
			world.Rpc(nameof(NetworkManager.UpdatePlayerTransformRpc), Multiplayer.GetUniqueId(), GlobalPosition, Rotation);
			return;
		}

		world.RpcId(1, nameof(NetworkManager.ReportTransformRpc), GlobalPosition, Rotation);
	}

	private void TryShoot()
	{
		if (_stats == null)
			return;

		var cooldown = _stats.AttackCooldown;
		var now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastShotTime < cooldown)
			return;

		if (!_stats.TrySpendAmmo())
			return;

		DebugAttack($"shot requested origin={GetCameraShootOrigin()} direction={GetCameraShootDirection()} cooldown={cooldown:0.00}s");

		if (!HasMultiplayerPeer())
		{
			_lastShotTime = now;
			ProcessShootRequest(0, GetCameraShootOrigin(), GetCameraShootDirection(), false);
			return;
		}

		if (IsServerSession())
		{
			ProcessShootRequest(Multiplayer.GetUniqueId(), GetCameraShootOrigin(), GetCameraShootDirection(), false);
			return;
		}

		_lastShotTime = now;
		RpcId(1, nameof(RequestShootRpc), GetCameraShootOrigin(), GetCameraShootDirection());
	}

	private Vector3 GetCameraShootOrigin()
	{
		return _camera != null ? _camera.GlobalPosition : _head.GlobalPosition;
	}

	private Vector3 GetCameraShootDirection()
	{
		if (_camera != null)
			return -_camera.GlobalTransform.Basis.Z.Normalized();

		return -_head.GlobalTransform.Basis.Z.Normalized();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void RequestShootRpc(Vector3 origin, Vector3 direction)
	{
		if (!IsServerSession())
			return;

		ProcessShootRequest(Multiplayer.GetRemoteSenderId(), origin, direction, true);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void RequestLandingShockwaveRpc(Vector3 center, float damage, float radius, float knockbackScale)
	{
		if (!IsServerSession())
			return;

		var senderId = Multiplayer.GetRemoteSenderId();
		var senderPlayer = _networkManager?.GetPlayer(senderId);
		if (senderPlayer == null || !GodotObject.IsInstanceValid(senderPlayer))
			return;

		senderPlayer.ApplyLandingShockwaveDamage(senderId, center, damage, radius, knockbackScale);
	}

	private void ProcessShootRequest(long shooterPeerId, Vector3 origin, Vector3 direction, bool consumeAmmo)
	{
		if (!HasMultiplayerPeer())
		{
			shooterPeerId = 0;
		}
		else if (!IsServerSession())
			return;

		var shooter = shooterPeerId > 0
			? _networkManager?.GetPlayer(shooterPeerId)
			: this;
		if (shooter == null || !GodotObject.IsInstanceValid(shooter))
		{
			DebugAttack($"shot rejected for peer {shooterPeerId}: shooter not found");
			return;
		}

		var shooterStats = shooter.GetStats();
		if (shooterStats == null)
		{
			DebugAttack($"shot rejected for peer {shooterPeerId}: shooter stats missing");
			return;
		}

		var now = Time.GetTicksMsec() / 1000.0;
		if (now - shooter._lastShotTime < shooterStats.AttackCooldown)
		{
			shooter.DebugAttack($"shot ignored by cooldown for peer {shooterPeerId}");
			return;
		}

		if (consumeAmmo && !shooterStats.TrySpendAmmo())
		{
			shooter.DebugAttack($"shot rejected for peer {shooterPeerId}: out of ammo or reloading");
			return;
		}

		if (direction == Vector3.Zero)
		{
			shooter.DebugAttack($"shot rejected for peer {shooterPeerId}: zero direction");
			return;
		}

		shooter._lastShotTime = now;

		var start = origin;
		var rayDirection = direction.Normalized();
		var end = start + rayDirection * shooterStats.AttackRange;
		var useMeleeHurtbox = IsMeleeWeapon(shooterStats.SelectedWeapon);
		if (useMeleeHurtbox)
		{
			shooter.UpdateMeleeHurtboxSize();
			var meleeTarget = shooter.GetMeleeOverlapTarget();
			if (meleeTarget == null || meleeTarget == shooter)
			{
				shooter.DebugAttack("melee overlap miss");
				return;
			}

			var meleeTargetStats = meleeTarget.GetStats();
			if (meleeTargetStats == null)
			{
				shooter.DebugAttack($"melee hit player {meleeTarget.Name} but stats missing");
				return;
			}

			var meleeDamage = shooterStats.AttackDamage;
			var meleeHealthBefore = meleeTargetStats.CurrentHealth;
			shooter.DebugAttack($"melee hit player={meleeTarget.Name} damage={meleeDamage} targetHealthBefore={meleeHealthBefore}");
			meleeTargetStats.TakeDamage(meleeDamage);
			var meleeDirection = (meleeTarget.GlobalPosition - shooter.GlobalPosition).Normalized();
			shooter.ApplyKnockbackToTarget(meleeTarget, meleeDirection, 1.0f);
			shooter.ApplyLifeSteal(shooterStats, meleeDamage);
			shooter.DebugAttack($"targetHealthAfter={meleeTargetStats.CurrentHealth}");

			var meleeKilled = meleeHealthBefore > 0f && meleeTargetStats.CurrentHealth <= 0f;
			_networkManager?.SendCombatFeedback(shooterPeerId, meleeKilled ? "Elimination" : "Hit", true, meleeKilled, false);
			if (meleeKilled)
				_networkManager?.ReportPlayerEliminated(shooterPeerId, meleeTarget.GetMultiplayerAuthority());
			return;
		}

		rayDirection = ApplyWeaponSpread(rayDirection, shooterStats.SelectedWeapon);
		end = start + rayDirection * shooterStats.AttackRange;
		shooter.DebugAttack($"raycast start={start} end={end} range={shooterStats.AttackRange:0.00}");
		shooter.ProcessRangedHitScan(shooterPeerId, shooterStats, start, rayDirection);
	}

	private void ProcessRangedHitScan(long shooterPeerId, PlayerStats shooterStats, Vector3 start, Vector3 direction)
	{
		if (shooterStats == null)
			return;

		var spaceState = GetWorld3D().DirectSpaceState;
		var remainingRange = Mathf.Max(0f, shooterStats.AttackRange);
		if (remainingRange <= 0f)
			return;

		var currentStart = start;
		var currentDirection = direction.Normalized();
		var ricochetsRemaining = Mathf.Max(0, shooterStats.ricochetCount);
		var piercesRemaining = Mathf.Max(0, shooterStats.pierceCount);
		var traveledDistance = 0f;
		var hitsProcessed = 0;

		var excluded = new Godot.Collections.Array<Rid> { GetRid() };
		if (_meleeHurtbox != null)
			excluded.Add(_meleeHurtbox.GetRid());

		while (remainingRange > 0.05f && hitsProcessed < 8)
		{
			hitsProcessed++;
			var segmentEnd = currentStart + currentDirection * remainingRange;
			var query = PhysicsRayQueryParameters3D.Create(currentStart, segmentEnd);
			query.CollisionMask = uint.MaxValue;
			query.CollideWithAreas = false;
			query.CollideWithBodies = true;
			query.Exclude = excluded;

			var hit = spaceState.IntersectRay(query);
			if (hit.Count == 0)
			{
				DebugAttack("raycast miss");
				return;
			}

			if (!hit.TryGetValue("position", out var hitPosValue))
				return;

			var hitPosition = hitPosValue.AsVector3();
			var segmentDistance = currentStart.DistanceTo(hitPosition);
			traveledDistance += segmentDistance;
			remainingRange -= segmentDistance;

			if (!hit.TryGetValue("collider", out var colliderValue))
				return;

			var collider = colliderValue.AsGodotObject();
			var hitPlayer = ResolveHitPlayer(collider);
			if (hitPlayer != null && hitPlayer != this)
			{
				var hitStats = hitPlayer.GetStats();
				if (hitStats == null)
					return;

				var isHeadshot = IsHeadshotHit(collider);
				var falloffMultiplier = GetDamageFalloffMultiplier(shooterStats.SelectedWeapon, traveledDistance, shooterStats.AttackRange);
				var damage = shooterStats.AttackDamage * falloffMultiplier;
				if (isHeadshot)
					damage *= Mathf.Max(1.0f, shooterStats.headshotMultiplier);

				var healthBefore = hitStats.CurrentHealth;
				DebugAttack($"hit player={hitPlayer.Name} damage={damage:0.00} distance={traveledDistance:0.00} headshot={isHeadshot} targetHealthBefore={healthBefore}");
				hitStats.TakeDamage(damage);
				ApplyKnockbackToTarget(hitPlayer, currentDirection, 1.0f);
				ApplyLifeSteal(shooterStats, damage);
				DebugAttack($"targetHealthAfter={hitStats.CurrentHealth}");

				var killed = healthBefore > 0f && hitStats.CurrentHealth <= 0f;
				var feedbackMessage = killed ? "Elimination" : (isHeadshot ? "Headshot" : "Hit");
				_networkManager?.SendCombatFeedback(shooterPeerId, feedbackMessage, true, killed, isHeadshot);
				if (killed)
					_networkManager?.ReportPlayerEliminated(shooterPeerId, hitPlayer.GetMultiplayerAuthority());

				excluded.Add(hitPlayer.GetRid());
				if (piercesRemaining > 0 && remainingRange > 0.05f)
				{
					piercesRemaining--;
					currentStart = hitPosition + currentDirection * 0.08f;
					continue;
				}

				return;
			}

			DebugAttack($"raycast hit non-player collider={collider?.GetType().Name ?? "null"}");
			if (ricochetsRemaining > 0 && hit.TryGetValue("normal", out var normalValue))
			{
				var normal = normalValue.AsVector3().Normalized();
				currentDirection = currentDirection.Bounce(normal).Normalized();
				currentStart = hitPosition + currentDirection * 0.08f;
				ricochetsRemaining--;
				continue;
			}

			return;
		}
	}

	private void ApplyKnockbackToTarget(PlayerController target, Vector3 direction, float scale)
	{
		if (_stats == null || target == null)
			return;

		var normalizedDirection = direction;
		normalizedDirection.Y = Mathf.Max(0.15f, normalizedDirection.Y);
		if (normalizedDirection == Vector3.Zero)
			normalizedDirection = Vector3.Up;
		normalizedDirection = normalizedDirection.Normalized();

		var force = Mathf.Max(0f, _stats.knockback) * Mathf.Max(0f, scale) * 0.25f;
		if (force <= 0f)
			return;

		target.Velocity += normalizedDirection * force;
	}

	private void ApplyLifeSteal(PlayerStats shooterStats, float damageDealt)
	{
		if (shooterStats == null || damageDealt <= 0f)
			return;

		var lifeSteal = Mathf.Clamp(shooterStats.lifeStealPercent, 0f, 1f);
		if (lifeSteal <= 0f)
			return;

		shooterStats.Heal(damageDealt * lifeSteal);
	}

	private Vector3 ApplyWeaponSpread(Vector3 direction, string weapon)
	{
		var spreadDegrees = weapon switch
		{
			"Assault" => 1.8f,
			"Sniper" => 0.12f,
			_ => 0.0f
		};
		if (spreadDegrees <= 0.001f)
			return direction;

		var axis = direction.Cross(Vector3.Up);
		if (axis.LengthSquared() <= 0.0001f)
			axis = direction.Cross(Vector3.Right);
		axis = axis.Normalized();
		var maxSpreadRad = Mathf.DegToRad(spreadDegrees);
		var yaw = _audioRng.RandfRange(-maxSpreadRad, maxSpreadRad);
		var pitch = _audioRng.RandfRange(-maxSpreadRad, maxSpreadRad);
		var spreadBasis = new Basis(Vector3.Up, yaw) * new Basis(axis, pitch);
		return (spreadBasis * direction).Normalized();
	}

	private static float GetDamageFalloffMultiplier(string weapon, float distance, float maxRange)
	{
		if (maxRange <= 0.01f)
			return 1.0f;

		var normalizedDistance = Mathf.Clamp(distance / maxRange, 0f, 1f);
		return weapon switch
		{
			"Assault" => Mathf.Lerp(1.0f, 0.7f, normalizedDistance),
			"Sniper" => Mathf.Lerp(1.0f, 0.88f, normalizedDistance),
			_ => 1.0f
		};
	}

	private static bool IsHeadshotHit(GodotObject collider)
	{
		if (collider is not Node node)
			return false;

		var current = node;
		while (current != null && current is not PlayerController)
		{
			var name = current.Name.ToString().ToLowerInvariant();
			if (name.Contains("head"))
				return true;
			current = current.GetParent();
		}

		return false;
	}

	private static PlayerController ResolveHitPlayer(GodotObject collider)
	{
		if (collider is PlayerController playerController)
			return playerController;

		if (collider is Node hitNode)
		{
			if (hitNode.GetParent() is PlayerController parentPlayer)
				return parentPlayer;
			if (hitNode.GetParent() is Node parentNode && parentNode.GetParent() is PlayerController grandParentPlayer)
				return grandParentPlayer;
		}

		return null;
	}

	private PlayerController GetMeleeOverlapTarget()
	{
		if (_meleeHurtbox == null)
			return null;

		var areas = _meleeHurtbox.GetOverlappingAreas();
		for (int i = 0; i < areas.Count; i++)
		{
			var player = ResolveHitPlayer(areas[i]);
			if (player != null && player != this)
				return player;
		}

		var bodies = _meleeHurtbox.GetOverlappingBodies();
		for (int i = 0; i < bodies.Count; i++)
		{
			var player = ResolveHitPlayer(bodies[i]);
			if (player != null && player != this)
				return player;
		}

		return null;
	}

	private void ApplyLandingShockwaveDamage(long attackerPeerId, Vector3 center, float damage, float radius, float knockbackScale)
	{
		var clampedDamage = Mathf.Max(0f, damage);
		var clampedRadius = Mathf.Max(0f, radius);
		if (clampedDamage <= 0f || clampedRadius <= 0f)
			return;

		if (_networkManager == null)
			return;

		var playerIds = _networkManager.GetSpawnedPlayerIds();
		for (int i = 0; i < playerIds.Length; i++)
		{
			var target = _networkManager.GetPlayer(playerIds[i]);
			if (target == null || !GodotObject.IsInstanceValid(target) || target == this)
				continue;

			var targetStats = target.GetStats();
			if (targetStats == null || targetStats.CurrentHealth <= 0f)
				continue;

			var toTarget = target.GlobalPosition - center;
			var distance = toTarget.Length();
			if (distance > clampedRadius)
				continue;

			var distanceRatio = clampedRadius <= 0.001f ? 1f : Mathf.Clamp(1f - (distance / clampedRadius), 0f, 1f);
			var shockwaveDamage = clampedDamage * Mathf.Lerp(0.55f, 1.0f, distanceRatio);
			var healthBefore = targetStats.CurrentHealth;
			targetStats.TakeDamage(shockwaveDamage);

			ApplyKnockbackToTarget(target, toTarget == Vector3.Zero ? Vector3.Up : toTarget.Normalized(), knockbackScale);

			var killed = healthBefore > 0f && targetStats.CurrentHealth <= 0f;
			_networkManager.SendCombatFeedback(attackerPeerId, killed ? "Elimination" : "Shockwave Hit", true, killed, false);
			if (killed)
				_networkManager.ReportPlayerEliminated(attackerPeerId, target.GetMultiplayerAuthority());
		}
	}

	private long GetAuthorityPeerId()
	{
		if (HasMultiplayerPeer())
			return Multiplayer.GetUniqueId();
		return 0;
	}

	private bool HasMultiplayerPeer()
	{
		return Multiplayer.MultiplayerPeer != null;
	}

	private bool IsServerSession()
	{
		return HasMultiplayerPeer() && Multiplayer.IsServer();
	}

	private void DebugAttack(string message)
	{
		if (_stats?.debugAttack != true)
			return;

		GD.Print($"[AttackDebug] {Name}: {message}");
	}
}
