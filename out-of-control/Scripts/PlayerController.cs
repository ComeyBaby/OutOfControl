using Godot;

public partial class PlayerController : CharacterBody3D
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string RoundManagerNodeName = "RoundManager";

	[Export] private Node3D _head;
	[Export] private MeshInstance3D _mesh;
	[Export] private Label3D _nameLabel;
	[Export] private CollisionShape3D _collider;
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
	private Vector3 _lastSentPosition;
	private Vector3 _lastSentRotation;
	private bool _hasLastSentState = false;

	private string _displayName = "";
	private double _lastShotTime = -999.0;
	private bool _controlsEnabled = true;
	private bool _pauseControlsLocked = false;
	private bool _isDead = false;
	private RoundPhase _lastRoundPhase = RoundPhase.Lobby;
	private Input.MouseModeEnum? _appliedMouseMode;

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
		_lastSentPosition = GlobalPosition;
		_lastSentRotation = Rotation;
		_hasLastSentState = true;
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
		}
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

		if (_mouseButtonPressed)
			TryShoot();

		if (HasGravity)
		{
			if (!IsOnFloor())
				Velocity += GetGravity() * d;
		}

		if (CanFreefly && _freeflying)
		{
			Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
			Vector3 motion = (_head.GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
			motion *= FreeflySpeed * d;
			MoveAndCollide(motion);
			SendNetworkTransform(d);
			return;
		}

		if (CanJump)
		{
			if (Input.IsActionJustPressed(InputJump) && IsOnFloor())
				Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
		}

		var wantsSprint = CanSprint && Input.IsActionPressed(InputSprint) && (_stats?.HasStamina ?? false);
		_stats?.TickStamina(wantsSprint, d);

		if (wantsSprint)
			_moveSpeed = SprintSpeed;
		else
			_moveSpeed = BaseSpeed;

		if (CanMove)
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
		SendNetworkTransform(d);
	}

	public override void _Process(double delta)
	{
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
		return _controlsEnabled && !_pauseControlsLocked && (_networkManager == null || _networkManager.IsRoundAcceptingPlayerInput());
	}

	private void ResetControlsState()
	{
		_pendingLookDelta = Vector2.Zero;
		_mouseButtonPressed = false;
		_moveSpeed = 0f;
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
		return HasVisibleInteractiveUi(this);
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

		_lastSentPosition = GlobalPosition;
		_lastSentRotation = Rotation;
		_hasLastSentState = true;

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
		shooter.DebugAttack($"raycast start={start} end={end} range={shooterStats.AttackRange:0.00}");
		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(start, end);
		query.CollisionMask = uint.MaxValue;
		query.Exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() };

		var hit = spaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			shooter.DebugAttack("raycast miss");
			return;
		}

		if (!hit.TryGetValue("collider", out var colliderValue))
		{
			shooter.DebugAttack("raycast hit had no collider");
			return;
		}

		var collider = colliderValue.AsGodotObject();
		var hitPlayer = collider as PlayerController;
		if (hitPlayer == null)
		{
			if (collider is Node hitNode)
				hitPlayer = hitNode.GetParent() as PlayerController;
		}

		if (hitPlayer == null || hitPlayer == shooter)
		{
			shooter.DebugAttack($"raycast hit non-player collider={collider?.GetType().Name ?? "null"}");
			return;
		}

		var hitStats = hitPlayer.GetStats();
		if (hitStats == null)
		{
			shooter.DebugAttack($"hit player {hitPlayer.Name} but stats missing");
			return;
		}

		var damage = shooterStats.AttackDamage;
		var healthBefore = hitStats.CurrentHealth;
		shooter.DebugAttack($"hit player={hitPlayer.Name} damage={damage} targetHealthBefore={healthBefore}");
		hitStats.TakeDamage(damage);
		shooter.DebugAttack($"targetHealthAfter={hitStats.CurrentHealth}");

		var killed = healthBefore > 0f && hitStats.CurrentHealth <= 0f;
		_networkManager?.SendCombatFeedback(shooterPeerId, killed ? "Elimination" : "Hit", true, killed);
		if (killed)
			_networkManager?.ReportPlayerEliminated(shooterPeerId, hitPlayer.GetMultiplayerAuthority());
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
