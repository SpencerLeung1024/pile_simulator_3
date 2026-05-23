using Godot;
using System;
using System.Collections.Generic;

public partial class BoxSim : Node3D
{
	// Scene nodes
	[Export] private MultiMeshInstance3D _multiMeshSpeciesPhase;

	[Export] private OptionButton _speciesPhaseDropdown;
	[Export] private LineEdit _amountLineEdit;
	[Export] private LineEdit _temperatureLineEdit;
	[Export] private Button _addSpeciesPhaseButton;

	[Export] private RichTextLabel _thermodynamicsLabel;
	[Export] private RichTextLabel _resourcesLabel;

	[Export] private Button _playButton;
	[Export] private Button _pauseButton;
	[Export] private Button _stepButton;
	[Export] private Button _clearButton;
	[Export] private CheckButton _sparkCheck;

	// Internal state
	private Volume _volume = new Volume(1);
	private bool _isPlaying = false;

	// This really should be moved somewhere else but for a first visualization it's fine
	public static Dictionary<string, string> speciesToHexCode = new Dictionary<string, string>
	{
		{"H2", "bf3f3f"},
		{"C", "3f3f3f"},
		{"O2", "ffffff"},
		{"CH4", "bf7f3f"},
		{"H2O", "7fbfff"},
		{"CO2", "7f7f7f"}
	};

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
