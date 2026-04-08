using Godot;
using System;

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

	// Cross-section cut state
	private bool _crossSectionEnabled = false;

	// UI reference
	private RichTextLabel _debugLabel;
	private CheckButton _crossSectionCheck;

	// Asteroid reference for toggling cross-section
	private Asteroid _asteroid;

	public override void _Ready()
	{
		_currentSpeed = BaseSpeed;

		// Look toward origin initially
		LookAt(Vector3.Zero, Vector3.Up);

		// Find the UI debug label and cross-section toggle
		var world = GetParent();
		if (world != null)
		{
			var ui = world.GetNodeOrNull<Control>("UI");
			if (ui != null)
			{
				_debugLabel = ui.GetNodeOrNull<RichTextLabel>("RichTextLabel");
				_crossSectionCheck = ui.GetNodeOrNull<CheckButton>("CrossSectionCheck");
			}

			// Get asteroid reference
			_asteroid = world.GetNodeOrNull<Asteroid>("Asteroid");
		}

		UpdateDebugDisplay();
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

		// C key to toggle cross-section cut
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.C)
			{
				ToggleCrossSectionCut();
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
		UpdateDebugDisplay();
	}

	private void ToggleCrossSectionCut()
	{
		_crossSectionEnabled = !_crossSectionEnabled;

		if (_asteroid != null)
		{
			_asteroid.SetCrossSectionCut(_crossSectionEnabled);
		}

		// Update UI checkbox to match
		if (_crossSectionCheck != null)
		{
			_crossSectionCheck.ButtonPressed = _crossSectionEnabled;
		}

		GD.Print($"Cross-section cut toggled: {(_crossSectionEnabled ? "enabled" : "disabled")}");
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

		// Get input directions
		float forward = Input.GetActionStrength("ui_down") - Input.GetActionStrength("ui_up"); // W/S
		float right = Input.GetActionStrength("ui_right") - Input.GetActionStrength("ui_left"); // D/A

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

	private void UpdateDebugDisplay()
	{
		if (_debugLabel == null) return;

		Vector3 pos = GlobalPosition;
		float distance = pos.DistanceTo(Vector3.Zero);

		string text = $"FPS: {Engine.GetFramesPerSecond()}\n";
		text += $"({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})\n";
		text += $"Distance: {distance:F1} m\n";
		text += $"Speed: {_currentSpeed:F1} m/s\n";
		text += $"Mouse: {(_mouseCaptured ? "Captured" : "Free")}\n";
		text += $"Cut: {(_crossSectionEnabled ? "ON" : "OFF")} (Press C)\n";
		text += "---\n";
		text += "MultiMesh: 0\n";
		text += "Static: 0\n";
		text += "Rigid: 0\n";
		text += "---\n";
		text += "Rock: 0\n";
		text += "Ice: 0\n";
		text += "Metal: 0";

		_debugLabel.Text = text;
	}

	public override void _ExitTree()
	{
		// Ensure mouse is released when scene changes
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}
}
