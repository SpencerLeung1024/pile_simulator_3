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
		// Initialize data classes
		ulong elementsStartUs = Time.GetTicksUsec();
		Elements.Initialize();
		ulong speciesStartUs = Time.GetTicksUsec();
		AllSpecies.Initialize();
		ulong formulaTableStartUs = Time.GetTicksUsec();
		FormulaTable.Initialize();
		ulong nuclidesStartUs = Time.GetTicksUsec();
		Nuclides.Initialize();
		// Nuclides.table is a Nuclide[,], which doesn't have LINQ methods
		int nuclideCount = 0;
		for (uint Z = 0; Z < Nuclides.table.GetLength(0); Z++)
		{
			for (uint N = 0; N < Nuclides.table.GetLength(1); N++)
			{
				if (Nuclides.table[Z, N] != null)
				{
					nuclideCount++;
				}
			}
		}
		ulong endUs = Time.GetTicksUsec();
		GD.Print($"{Elements.list.Length} Elements: {(speciesStartUs - elementsStartUs)/1000.0f:F2} ms");
		GD.Print($"{AllSpecies.list.Count} Species and {AllSpeciesPhases.list.Count} Species Phases: {(formulaTableStartUs - speciesStartUs)/1000.0f:F2} ms");
		GD.Print($"Formulas: {(nuclidesStartUs - formulaTableStartUs)/1000.0f:F2} ms");
		GD.Print($"{nuclideCount} Nuclides: {(endUs - nuclidesStartUs)/1000.0f:F2} ms");

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
