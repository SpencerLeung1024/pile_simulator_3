using Godot;
using System;

public partial class World : Node3D
{
	// Scene nodes
	[Export] private Camera3D _camera;
	[Export] private Asteroid _asteroid;

	// Configuration

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
