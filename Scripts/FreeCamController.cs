using Godot;
using System;
using System.Collections.Generic;

public partial class FreeCamController : Camera3D
{
	// Movement settings
	[Export] public float BaseSpeed { get; set; } = 50.0f;
	[Export] public float MinSpeed { get; set; } = 10.0f;
	[Export] public float MaxSpeed { get; set; } = 500.0f;
	[Export] public float SpeedScrollMultiplier { get; set; } = 1.1f;
	[Export] public float SprintMultiplier { get; set; } = 2.0f;
	[Export] public float MouseSensitivity { get; set; } = 0.003f;

	// Current state
	private float _currentSpeed;
	private bool _isSprinting = false;
	private bool _mouseCaptured = false;
	private Vector2 _mouseDelta = Vector2.Zero;

	// Singletons
	private Settings _settings = Settings.GetSettings();

	private static FreeCamController singleton = null;

	public static FreeCamController GetFreeCamController()
	{
		if (singleton == null)
		{
			throw new Exception("FreeCamController singleton not initialized yet");
		}
		return singleton;
	}

	private void PushDebugInfo()
	{
		Dictionary<string, string> debugInfo = _settings.DebugInfo;
		debugInfo["CameraPos"] = $"({GlobalPosition.X:F2}, {GlobalPosition.Y:F2}, {GlobalPosition.Z:F2})";
		debugInfo["CameraDist"] = $"{GlobalPosition.Length():F2} m";
	}

	public override void _Ready()
	{
		singleton = this;

		_currentSpeed = BaseSpeed;

		// Look toward origin initially
		LookAt(Vector3.Zero, Vector3.Up);

		PushDebugInfo();
	}

	public override void _Input(InputEvent @event)
	{
		// Toggle mouse capture with ESC
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (_mouseCaptured)
			{
				ReleaseMouse();
			}
		}

		// Right mouse button to capture mouse and look around
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				if (mouseButton.Pressed)
				{
					CaptureMouse();
				}
				else
				{
					ReleaseMouse();
				}
			}

			// Mouse wheel for speed adjustment
			if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
			{
				_currentSpeed *= SpeedScrollMultiplier;
				_currentSpeed = Mathf.Min(_currentSpeed, MaxSpeed);
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
			{
				_currentSpeed /= SpeedScrollMultiplier;
				_currentSpeed = Mathf.Max(_currentSpeed, MinSpeed);
			}
		}

		// Mouse motion for look
		if (@event is InputEventMouseMotion mouseMotion && _mouseCaptured)
		{
			_mouseDelta = mouseMotion.Relative;
		}
	}

	public override void _Process(double delta)
	{
		// Handle rotation from mouse input
		if (_mouseCaptured && _mouseDelta != Vector2.Zero)
		{
			RotateCamera(_mouseDelta);
			_mouseDelta = Vector2.Zero;
		}

		// Handle movement
		HandleMovement(delta);

		// Update debug display
		PushDebugInfo();
	}

	private void CaptureMouse()
	{
		_mouseCaptured = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void ReleaseMouse()
	{
		_mouseCaptured = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void RotateCamera(Vector2 delta)
	{
		// Rotate around Y axis (yaw) - left/right
		RotateY(-delta.X * MouseSensitivity);

		// Rotate around local X axis (pitch) - up/down
		// We need to clamp pitch to avoid flipping
		RotateObjectLocal(Vector3.Right, -delta.Y * MouseSensitivity);

		// Clamp pitch to prevent camera flip
		var transform = Transform;
		var basis = transform.Basis;
		float currentPitch = Mathf.Asin(basis.X.Y);
		float maxPitch = Mathf.Pi / 2.0f - 0.01f;

		if (currentPitch > maxPitch || currentPitch < -maxPitch)
		{
			// Revert the pitch rotation if we went too far
			RotateObjectLocal(Vector3.Right, delta.Y * MouseSensitivity);
		}
	}

	private void HandleMovement(double delta)
	{
		Vector3 velocity = Vector3.Zero;

		// Get input directions using WASD keys
		float forward = 0.0f;
		float right = 0.0f;

		// W/S for forward/backward
		if (Input.IsKeyPressed(Key.W))
		{
			forward += 1.0f;
		}
		if (Input.IsKeyPressed(Key.S))
		{
			forward -= 1.0f;
		}

		// A/D for left/right
		if (Input.IsKeyPressed(Key.D))
		{
			right += 1.0f;
		}
		if (Input.IsKeyPressed(Key.A))
		{
			right -= 1.0f;
		}

		// Q/E for vertical movement
		float up = 0.0f;
		if (Input.IsKeyPressed(Key.E))
		{
			up += 1.0f;
		}
		if (Input.IsKeyPressed(Key.Q))
		{
			up -= 1.0f;
		}

		// Sprint check
		bool isShiftPressed = Input.IsKeyPressed(Key.Shift);
		float speed = _currentSpeed * (isShiftPressed ? SprintMultiplier : 1.0f);

		// Calculate movement relative to camera orientation
		Basis basis = Transform.Basis;

		// Forward/back is negative Z in Godot's coordinate system
		if (!Mathf.IsZeroApprox(forward))
		{
			velocity -= basis.Z * forward;
		}

		// Left/right
		if (!Mathf.IsZeroApprox(right))
		{
			velocity += basis.X * right;
		}

		// Up/down (world space)
		if (!Mathf.IsZeroApprox(up))
		{
			velocity += Vector3.Up * up;
		}

		// Normalize and apply speed
		if (velocity.LengthSquared() > 0.001f)
		{
			velocity = velocity.Normalized() * speed;
			Translate(velocity * (float)delta);
		}
	}

	public override void _ExitTree()
	{
		// Ensure mouse is released when scene changes
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}
}
