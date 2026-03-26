using Godot;
using System.Collections.Generic;

public partial class PlayerController : CharacterBody3D
{
	[Export] public bool CanMove = true;
	[Export] public bool HasGravity = true;
	[Export] public bool CanJump = true;
	[Export] public bool CanSprint = false;
	[Export] public bool CanFreefly = false;

	[ExportGroup("Speeds")]
	[Export] public float LookSpeed = 0.002f;
	[Export] public float BaseSpeed = 7.0f;
	[Export] public float JumpVelocity = 4.5f;
	[Export] public float SprintSpeed = 10.0f;
	[Export] public float FreeflySpeed = 25.0f;

	[ExportGroup("Input Actions")]
	[Export] public string InputLeft = "ui_left";
	[Export] public string InputRight = "ui_right";
	[Export] public string InputForward = "ui_up";
	[Export] public string InputBack = "ui_down";
	[Export] public string InputJump = "ui_accept";
	[Export] public string InputSprint = "sprint";
	[Export] public string InputFreefly = "freefly";

	private bool _mouseCaptured = false;
	private Vector2 _lookRotation;
	private float _moveSpeed = 0f;
	private bool _freeflying = false;

	private Node3D _head;
	private CollisionShape3D _collider;

	public override void _Ready()
	{
		_head = GetNode<Node3D>("Head");
		_collider = GetNode<CollisionShape3D>("Collider");

		GameSettings.LookSensitivityChanged += OnLookSensitivityChanged;
		OnLookSensitivityChanged(GameSettings.LookSensitivity);
		GameSettings.ApplyMasterVolume();

		_lookRotation.Y = Rotation.Y;
		_lookRotation.X = _head.Rotation.X;
		CaptureMouse();
	}

	public override void _ExitTree()
	{
		GameSettings.LookSensitivityChanged -= OnLookSensitivityChanged;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsMouseButtonPressed(MouseButton.Left))
			CaptureMouse();

		if (Input.IsKeyPressed(Key.Escape))
			ReleaseMouse();

		if (_mouseCaptured && @event is InputEventMouseMotion motion)
			RotateLook(motion.Relative);

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

		if (CanFreefly && _freeflying)
		{
			Vector2 inputDir = Input.GetVector(InputLeft, InputRight, InputForward, InputBack);
			Vector3 motion = (_head.GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
			motion *= FreeflySpeed * d;
			MoveAndCollide(motion);
			return;
		}

		if (HasGravity)
		{
			if (!IsOnFloor())
				Velocity += GetGravity() * d;
		}

		if (CanJump)
		{
			if (Input.IsActionJustPressed(InputJump) && IsOnFloor())
				Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
		}

		if (CanSprint && Input.IsActionPressed(InputSprint))
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
	}

	private void RotateLook(Vector2 rotInput)
	{
		_lookRotation.X -= rotInput.Y * LookSpeed;
		_lookRotation.X = Mathf.Clamp(_lookRotation.X, Mathf.DegToRad(-85), Mathf.DegToRad(85));

		_lookRotation.Y -= rotInput.X * LookSpeed;

		// Apply rotations directly instead of rebuilding transforms
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

	private void OnLookSensitivityChanged(float sensitivity)
	{
		LookSpeed = sensitivity;
	}
}
