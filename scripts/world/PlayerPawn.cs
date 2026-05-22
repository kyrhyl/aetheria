using Godot;

public partial class PlayerPawn : CharacterBody3D
{
	[Export] public float MoveSpeed = 7.5f;
	[Export] public float JumpVelocity = 5.0f;
	[Export] public float MouseSensitivity = 0.003f;

	private Node3D _cameraPivot = null!;
	private float _gravity;
	private float _pitch;

	public override void _Ready()
	{
		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Right)
		{
			Input.MouseMode = mouseButton.Pressed
				? Input.MouseModeEnum.Captured
				: Input.MouseModeEnum.Visible;
		}

		if (@event is not InputEventMouseMotion motion)
		{
			return;
		}

		if (!Input.IsMouseButtonPressed(MouseButton.Right))
		{
			return;
		}

		RotateY(-motion.Relative.X * MouseSensitivity);
		_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -1.2f, 0.75f);
		_cameraPivot.Rotation = new Vector3(_pitch, 0.0f, 0.0f);
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		if (!IsOnFloor())
		{
			velocity.Y -= _gravity * (float)delta;
		}

		if (Input.IsActionJustPressed("move_jump") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		Vector2 inputVector = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
		Vector3 direction = (Transform.Basis.X * inputVector.X + Transform.Basis.Z * inputVector.Y).Normalized();

		velocity.X = direction.X * MoveSpeed;
		velocity.Z = direction.Z * MoveSpeed;

		Velocity = velocity;
		MoveAndSlide();
	}
}
