using Godot;
using System;

public partial class MainMenu : Node3D
{
	// Scene nodes
	[Export] private Button _worldButton;
	[Export] private Button _solarSystemButton;
	[Export] private Button _shopButton;
	[Export] private Button _boxSimButton;

	// Handle signals
	private void OnWorldButtonPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/World.tscn");
	}

	private void OnSolarSystemButtonPressed()
	{
		// TODO
	}

	private void OnShopButtonPressed()
	{
		// TODO
	}

	private void OnBoxSimButtonPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/BoxSim.tscn");
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Connect signals
		_worldButton.Pressed += OnWorldButtonPressed;
		_solarSystemButton.Pressed += OnSolarSystemButtonPressed;
		_shopButton.Pressed += OnShopButtonPressed;
		_boxSimButton.Pressed += OnBoxSimButtonPressed;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
