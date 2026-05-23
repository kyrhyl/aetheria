using Godot;

public partial class PlayerPawn : CharacterBody3D
{
	[ExportGroup("Movement")]
	[Export] public float WalkSpeed = 5.5f;
	[Export] public float BackpedalSpeedMultiplier = 0.6f;
	[Export] public float StrafeSpeedMultiplier = 0.9f;
	[Export] public float RotationSpeedDeg = 180.0f;
	[Export] public float Acceleration = 14.0f;
	[Export] public float Deceleration = 18.0f;
	[Export] public float JumpVelocity = 6.5f;

	[ExportGroup("Camera")]
	[Export] public float MouseSensitivity = 0.0025f;
	[Export] public float PitchMinDeg = -35.0f;
	[Export] public float PitchMaxDeg = 60.0f;

	[ExportGroup("Input")]
	[Export] public StringName MoveForwardAction = "move_forward";
	[Export] public StringName MoveBackwardAction = "move_backward";
	[Export] public StringName MoveLeftAction = "move_left";
	[Export] public StringName MoveRightAction = "move_right";
	[Export] public StringName JumpAction = "move_jump";

	private CharacterVisualManager _visualManager;
	private Node3D _cameraPivot = null!;
	private float _gravity;
	private float _pitch;

	public override void _Ready()
	{
		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_pitch = _cameraPivot.Rotation.X;
		_gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

		_visualManager = GetNodeOrNull<CharacterVisualManager>("MeshRoot");
		if (_visualManager == null)
		{
			GD.PushWarning("[PlayerPawn] CharacterVisualManager is missing on MeshRoot.");
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Right)
		{
			Input.MouseMode = mouseButton.Pressed
				? Input.MouseModeEnum.Captured
				: Input.MouseModeEnum.Visible;
		}

		if (@event is not InputEventMouseMotion mouseMotion)
		{
			return;
		}

		if (!Input.IsMouseButtonPressed(MouseButton.Right))
		{
			return;
		}

		RotateY(-mouseMotion.Relative.X * MouseSensitivity);

		_pitch = Mathf.Clamp(
			_pitch - mouseMotion.Relative.Y * MouseSensitivity,
			Mathf.DegToRad(PitchMinDeg),
			Mathf.DegToRad(PitchMaxDeg));

		Vector3 camRot = _cameraPivot.Rotation;
		camRot.X = _pitch;
		_cameraPivot.Rotation = camRot;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		bool rmbHeld = Input.IsMouseButtonPressed(MouseButton.Right);

		Vector3 velocity = Velocity;
		if (!IsOnFloor())
		{
			velocity.Y -= _gravity * dt;
		}
		else if (velocity.Y < 0f)
		{
			velocity.Y = 0f;
		}

		if (Input.IsActionJustPressed(JumpAction) && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		float forwardInput = Input.GetActionStrength(MoveForwardAction) - Input.GetActionStrength(MoveBackwardAction);
		float turnInput = 0f;
		float lateralInput = 0f;

		bool leftPressed = Input.IsActionPressed(MoveLeftAction);
		bool rightPressed = Input.IsActionPressed(MoveRightAction);

		if (leftPressed)
		{
			if (rmbHeld)
			{
				lateralInput += 1f;
			}
			else
			{
				turnInput += 1f;
			}
		}

		if (rightPressed)
		{
			if (rmbHeld)
			{
				lateralInput -= 1f;
			}
			else
			{
				turnInput -= 1f;
			}
		}

		if (!rmbHeld && Mathf.Abs(turnInput) > 0.001f)
		{
			float yawDelta = Mathf.DegToRad(RotationSpeedDeg) * turnInput * dt;
			RotateY(yawDelta);
		}

		Vector3 forward = -GlobalTransform.Basis.Z;
		Vector3 right = GlobalTransform.Basis.X;
		forward.Y = 0f;
		right.Y = 0f;
		forward = forward.Normalized();
		right = right.Normalized();

		Vector3 moveDir = (forward * forwardInput) + (right * lateralInput);
		if (moveDir.LengthSquared() > 1f)
		{
			moveDir = moveDir.Normalized();
		}

		float targetSpeed = WalkSpeed;
		if (forwardInput < -0.001f)
		{
			targetSpeed *= BackpedalSpeedMultiplier;
		}
		else if (Mathf.Abs(lateralInput) > 0.001f && Mathf.Abs(forwardInput) < 0.001f)
		{
			targetSpeed *= StrafeSpeedMultiplier;
		}

		Vector3 targetHorizontal = moveDir * targetSpeed;
		Vector3 currentHorizontal = new Vector3(velocity.X, 0f, velocity.Z);
		float accelRate = targetHorizontal.LengthSquared() > currentHorizontal.LengthSquared()
			? Acceleration
			: Deceleration;

		currentHorizontal = currentHorizontal.MoveToward(targetHorizontal, accelRate * dt);
		velocity.X = currentHorizontal.X;
		velocity.Z = currentHorizontal.Z;

		Velocity = velocity;
		MoveAndSlide();

		if (_visualManager != null)
		{
			float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
			_visualManager.SetLocomotionState(IsOnFloor(), horizontalSpeed, Velocity.Y);
		}
	}
}
