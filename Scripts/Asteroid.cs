using Godot;
using System;

public partial class Asteroid : Node3D
{
	// Scene nodes
	[Export] private PackedScene _rigidRockScene; // Real rocks that have been dislodged. MeshInstance3D.SurfaceMaterialOverride[0].AlbedoColor is red
	[Export] private PackedScene _staticRockScene; // Real rocks that are attached to the voxel grid. MeshInstance3D.SurfaceMaterialOverride[0].AlbedoColor is green
	[Export] private MultiMeshInstance3D _multiMeshRock; // Far away approximations of octree nodes. Can be scaled freely. Supports vertex color. No collisions or raycasts

	// Configuration
	[Export] private float _radius;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
