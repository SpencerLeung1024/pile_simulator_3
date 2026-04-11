using Godot;
using System;

// Most of the UI boilerplate has been moved to UIController
// World may have other uses in the future
public partial class World : Node3D
{
	// Scene nodes
	[Export] private UIController _uiController;
	[Export] private WorldEnvironment _worldEnvironment;
	[Export] private DirectionalLight3D _directionalLight;
	[Export] private FreeCamController _camera;
	[Export] private Asteroid _asteroid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
