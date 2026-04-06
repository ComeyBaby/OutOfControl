using Godot;

public partial class PlayerController : CharacterBody3D
{
	private bool _mouseCaptured = false;
	private Vector2 _lookRotation;
	private Vector2 _pendingLookDelta;
	private float _moveSpeed = 0f;
	private bool _freeflying = false;

	private Vector3 _netTargetPosition;
	private Vector3 _netTargetRotation;
	private bool _hasNetTarget = false;
	private double _networkSyncAccumulator = 0.0;
	private Vector3 _lastSentPosition;
	private Vector3 _lastSentRotation;
	private bool _hasLastSentState = false;

	private Node3D _head;
	private MeshInstance3D _mesh;
	private Label3D _nameLabel;
	private CollisionShape3D _collider;
	private PlayerStats _stats;
	private NetworkManager _networkManager;
	private string _displayName = "";
	private double _lastShotTime = -999.0;
	private bool _controlsEnabled = true;
	private bool _isDead = false;

	private bool CanMove => _stats?.canMove ?? true;
	private bool HasGravity => _stats?.hasGravity ?? true;
	private bool CanJump => _stats?.canJump ?? true;
	private bool CanSprint => _stats?.canSprint ?? false;
	private bool CanFreefly => _stats?.canFreefly ?? false;

	private float LookSpeed => _stats?.lookSpeed ?? 0.002f;
	private float BaseSpeed => _stats?.baseSpeed ?? 7.0f;
	private float JumpVelocity => _stats?.jumpVelocity ?? 4.5f;
	private float SprintSpeed => _stats?.sprintSpeed ?? 10.0f;
	private float FreeflySpeed => _stats?.freeflySpeed ?? 25.0f;

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
		_head = GetNode<Node3D>("Head");
		_mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
		_nameLabel = GetNodeOrNull<Label3D>("Head/NameLabel");
		_collider = GetNode<CollisionShape3D>("Collider");
		_stats = GetNodeOrNull<PlayerStats>("PlayerStats");
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>("NetworkManager");

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
			OnHealthChanged(_stats.CurrentHealth, _stats.MaxHealth);
		}
		RefreshAuthorityState();
	}

	public override void _ExitTree()
	{
		if (_stats != null)
		{
			var healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
			if (_stats.IsConnected(nameof(PlayerStats.HealthChanged), healthChangedCallable))
				_stats.Disconnect(nameof(PlayerStats.HealthChanged), healthChangedCallable);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!HasLocalAuthority())
			return;

		if (!_controlsEnabled)
			return;

		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				CaptureMouse();
				TryShoot();
			}
		}

		if (_mouseCaptured && @event is InputEventMouseMotion motion)
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

		if (!_controlsEnabled)
		{
			SendNetworkTransform(d);
			return;
		}

		if (_pendingLookDelta != Vector2.Zero)
		{
			RotateLook(_pendingLookDelta);
			_pendingLookDelta = Vector2.Zero;
		}

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
		{
			_pendingLookDelta = Vector2.Zero;
			_moveSpeed = 0f;
			Velocity = Vector3.Zero;
			if (_freeflying)
				DisableFreefly();
		}
	}

	private void CaptureMouse()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		_mouseCaptured = true;
	}

	private void ReleaseMouse()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_mouseCaptured = false;
	}

	public void RefreshAuthorityState()
	{
		if (HasLocalAuthority())
			CaptureMouse();
		else
			ReleaseMouse();
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
		return GetNodeOrNull<Camera3D>("Head/Camera3D");
	}

	private void OnHealthChanged(int currentHealth, int maxHealth)
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

		var world = _networkManager
			?? GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>("NetworkManager");
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

		var cooldown = _stats.GunCooldown;
		var now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastShotTime < cooldown)
			return;

		DebugGun($"shot requested origin={GetCameraShootOrigin()} direction={GetCameraShootDirection()} cooldown={cooldown:0.00}s");

		if (!HasMultiplayerPeer())
		{
			_lastShotTime = now;
			ProcessShootRequest(0, GetCameraShootOrigin(), GetCameraShootDirection());
			return;
		}

		if (IsServerSession())
		{
			ProcessShootRequest(Multiplayer.GetUniqueId(), GetCameraShootOrigin(), GetCameraShootDirection());
			return;
		}

		_lastShotTime = now;
		RpcId(1, nameof(RequestShootRpc), GetCameraShootOrigin(), GetCameraShootDirection());
	}

	private Vector3 GetCameraShootOrigin()
	{
		var camera = GetNodeOrNull<Camera3D>("Head/Camera3D");
		return camera != null ? camera.GlobalPosition : _head.GlobalPosition;
	}

	private Vector3 GetCameraShootDirection()
	{
		var camera = GetNodeOrNull<Camera3D>("Head/Camera3D");
		if (camera != null)
			return -camera.GlobalTransform.Basis.Z.Normalized();

		return -_head.GlobalTransform.Basis.Z.Normalized();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void RequestShootRpc(Vector3 origin, Vector3 direction)
	{
		if (!IsServerSession())
			return;

		ProcessShootRequest(Multiplayer.GetRemoteSenderId(), origin, direction);
	}

	private void ProcessShootRequest(long shooterPeerId, Vector3 origin, Vector3 direction)
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
			DebugGun($"shot rejected for peer {shooterPeerId}: shooter not found");
			return;
		}

		var shooterStats = shooter.GetStats();
		if (shooterStats == null)
		{
			DebugGun($"shot rejected for peer {shooterPeerId}: shooter stats missing");
			return;
		}

		var now = Time.GetTicksMsec() / 1000.0;
		if (now - shooter._lastShotTime < shooterStats.GunCooldown)
		{
			shooter.DebugGun($"shot ignored by cooldown for peer {shooterPeerId}");
			return;
		}

		if (direction == Vector3.Zero)
		{
			shooter.DebugGun($"shot rejected for peer {shooterPeerId}: zero direction");
			return;
		}

		shooter._lastShotTime = now;

		var start = origin;
		var rayDirection = direction.Normalized();
		var end = start + rayDirection * shooterStats.GunRange;
		shooter.DebugGun($"raycast start={start} end={end} range={shooterStats.GunRange:0.00}");
		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(start, end);
		query.CollisionMask = uint.MaxValue;
		query.Exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() };

		var hit = spaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			shooter.DebugGun("raycast miss");
			return;
		}

		if (!hit.TryGetValue("collider", out var colliderValue))
		{
			shooter.DebugGun("raycast hit had no collider");
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
			shooter.DebugGun($"raycast hit non-player collider={collider?.GetType().Name ?? "null"}");
			return;
		}

		var hitStats = hitPlayer.GetStats();
		if (hitStats == null)
		{
			shooter.DebugGun($"hit player {hitPlayer.Name} but stats missing");
			return;
		}

		var damage = shooterStats.GunDamage;
		shooter.DebugGun($"hit player={hitPlayer.Name} damage={damage} targetHealthBefore={hitStats.CurrentHealth}");
		hitStats.TakeDamage(damage);
		shooter.DebugGun($"targetHealthAfter={hitStats.CurrentHealth}");
	}

	private bool HasMultiplayerPeer()
	{
		return Multiplayer.MultiplayerPeer != null;
	}

	private bool IsServerSession()
	{
		return HasMultiplayerPeer() && Multiplayer.IsServer();
	}

	private void DebugGun(string message)
	{
		if (_stats?.debugGun != true)
			return;

		GD.Print($"[GunDebug] {Name}: {message}");
	}
}
