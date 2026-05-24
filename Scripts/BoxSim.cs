using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

public partial class BoxSim : Node3D
{
	const bool VERBOSE = false;

	[Export] private MultiMeshInstance3D _multiMeshSpeciesPhase;

	[Export] private OptionButton _speciesPhaseDropdown;
	[Export] private LineEdit _amountLineEdit;
	[Export] private LineEdit _temperatureLineEdit;
	[Export] private Button _addSpeciesPhaseButton;

	[Export] private RichTextLabel _thermodynamicsLabel;
	[Export] private RichTextLabel _resourcesLabel;

	[Export] private LineEdit _FPSLineEdit;
	[Export] private Button _playButton;
	[Export] private Button _pauseButton;
	[Export] private Button _stepButton;
	[Export] private Button _clearButton;
	[Export] private CheckButton _sparkCheck;

	[Export] private Label _FPSLabel;
	[Export] private Button _dumpDebugButton;

	private Volume _volume = new Volume(1.0);
	private bool _isPlaying = false;
	private double accumulatedTime = 0.0;
	private double FPSTarget = 1.0; // Start really low

	public static Dictionary<string, string> speciesToHex = new Dictionary<string, string>
	{
		{"H2", "#bf3f3f"},
		{"C", "#1f1f1f"},
		{"O2", "#ffffff"},
		{"Fe", "#5f5f5f"},
		{"CH4", "#ff5f5f"},
		{"H2O", "#7fbfff"},
		{"CO2", "#7f7f7f"},
		{"C2H5OH", "#7f5f3f"},
		{"Fe2O3", "#ff9f7f"}
	};

	private void DumpDebug()
	{
		GD.Print("Thermodynamics:");
		GD.Print(_thermodynamicsLabel.Text);
		GD.Print("Resources:");
		GD.Print(_resourcesLabel.Text);
	}

	private void UpdateThermodynamicsLabel(List<ResourceDisplayEntry> info)
	{
		double H = _volume.U + _volume.P * _volume.Volume;
		double G = H - _volume.T * _volume.S;
		double A = _volume.U - _volume.T * _volume.S;

		_thermodynamicsLabel.Text =
			$"Mass: {Constants.FormatUnit(_volume.Mass, 3, "kg")}\n" +
			$"U: {Constants.FormatUnit(_volume.U, 3, "J")}\n" +
			$"T: {Math.Round(_volume.T)} K\n" + // Always show as Kelvin with no SI prefix
			$"V: {Constants.FormatUnit(_volume.Volume * 1e3, 3, "L")}\n" + // The SI unit is m^3 but "mm^3" is actually 1e-9 m^3, so show L as the unit
			$"P: {Constants.FormatUnit(_volume.P, 3, "Pa")}\n" +
			$"S: {Constants.FormatUnit(_volume.S, 3, "J/K")}\n" +
			$"H = U + PV: {Constants.FormatUnit(H, 3, "J")}\n" +
			$"G = H - TS: {Constants.FormatUnit(G, 3, "J")}\n" +
			$"A = U - TS: {Constants.FormatUnit(A, 3, "J")}";
	}

	private void UpdateResourcesLabel(List<ResourceDisplayEntry> info)
	{
		if (info.Count == 0)
		{
			_resourcesLabel.Text = "";
			return;
		}

		var speciesGroups = info
			.GroupBy(r => r.SpeciesPhase.Species)
			.OrderBy(g => g.Key.Name);

		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (var group in speciesGroups)
		{
			string hex = speciesToHex.GetValueOrDefault(group.Key.Name, "#ffffff");
			sb.AppendLine($"[color={hex}]{group.Key.Name}[/color]");

			foreach (var entry in group.OrderBy(e => (int)e.SpeciesPhase.Phase))
			{
				sb.AppendLine(
					$"{entry.SpeciesPhase.Phase.ToString().ToLower()}: {Constants.FormatUnit(entry.n, 3, "mol")}, {Constants.FormatUnit(entry.ResourceMass, 3, "kg")}, {Constants.FormatUnit(entry.ResourceVolume * 1e3, 3, "L")}");
			}
		}
		_resourcesLabel.Text = sb.ToString().Replace("\r\n", "\n").TrimEnd();
	}

	private void UpdateMultiMesh(List<ResourceDisplayEntry> info)
	{
		int count = info.Count;
		_multiMeshSpeciesPhase.Multimesh.InstanceCount = count;

		if (count == 0)
		{
			return;
		}

		const float boxSize = 1.0f;
		const float boxHalf = boxSize / 2.0f;

		Vector<double> phaseVolumes = _volume.all_vec_V;
		double totalVol = phaseVolumes.Sum();

		var displayVolumesWithIndex = info
			.Select((entry, idx) => (
				species: entry.SpeciesPhase.Species,
				vol: entry.ResourceVolume,
				instanceIndex: idx,
				phase: entry.SpeciesPhase.Phase
			))
			.ToList();

		float yTop = boxHalf;
		for (int m = 0; m < 3; m++)
		{
			float phaseHeight = (float)(phaseVolumes[m] / totalVol) * boxSize;
			if (phaseHeight < 0.001f)
			{
				continue;
			}
			float yCenter = yTop - phaseHeight / 2.0f;

			Phase phase = (Phase)m;
			var phaseEntries = displayVolumesWithIndex
				.Where(x => x.phase == phase)
				.ToList();

			float xLeft = -boxHalf;
			foreach (var (species, vol, instanceIndex, _) in phaseEntries)
			{
				float width = (float)(vol / phaseVolumes[m]) * boxSize;
				if (width < 0.001f)
				{
					continue;
				}
				float xCenter = xLeft + width / 2.0f;

				var transform = new Transform3D(
					Basis.FromScale(new Vector3(width, phaseHeight, boxSize)),
					new Vector3(xCenter, yCenter, 0)
				);

				_multiMeshSpeciesPhase.Multimesh.SetInstanceTransform(instanceIndex, transform);

				string hex = speciesToHex.GetValueOrDefault(species.Name, "#ffffff");
				var color = new Color(hex);
				if (phase == Phase.Liquid)
				{
					color = color.Lerp(new Color("#000000"), 0.25f);
				}
				else if (phase == Phase.Solid)
				{
					color = color.Lerp(new Color("#000000"), 0.5f);
				}
				_multiMeshSpeciesPhase.Multimesh.SetInstanceColor(instanceIndex, color);

				xLeft += width;
			}

			yTop -= phaseHeight;
		}
	}

	private void UpdateUI()
	{
		List<ResourceDisplayEntry> info = _volume.GetInfo();
		UpdateThermodynamicsLabel(info);
		UpdateResourcesLabel(info);
		UpdateMultiMesh(info);
		if (VERBOSE)
		{
			DumpDebug();
		}
	}

	private void OnAddSpeciesPhase()
	{
		int index = _speciesPhaseDropdown.Selected;
		if (index < 0 || index >= AllSpeciesPhases.list.Count)
		{
			return;
		}
		SpeciesPhase selectedPhase = AllSpeciesPhases.list[index];

		if (!double.TryParse(_amountLineEdit.Text, out double addedn) || addedn <= 0)
		{
			return;
		}

		if (!double.TryParse(_temperatureLineEdit.Text, out double addedT) || addedT <= 0)
		{
			return;
		}

		SpeciesPhaseResource resource = new SpeciesPhaseResource
		{
			SpeciesPhase = selectedPhase,
			n = addedn
		};

		bool success = _volume.MaybeAdd(resource);
		if (!success)
		{
			throw new Exception("Failed to add resource to volume");
		}
		// We also need to increase the target internal energy
		// Otherwise we would be putting in matter at absolute zero and the box would gradually get colder
		double addedU = resource.n * resource.SpeciesPhase.EquationOfState.GetU(addedT, resource.SpeciesPhase.EquationOfState.Getv(addedT, _volume.P));
		_volume.UTarget += addedU;

		_volume.Solve();
		UpdateUI();
	}

	private void OnFPSLineEditChanged(string text)
	{
		if (!double.TryParse(text, out double fps) || fps <= 0.5) // Prohibit 0.0 so we don't divide by zero
		{
			return;
		}
		FPSTarget = fps;
	}

	private void OnPlay()
	{
		_isPlaying = true;
		_playButton.Disabled = true;
		_pauseButton.Disabled = false;
	}

	private void OnPause()
	{
		_isPlaying = false;
		_playButton.Disabled = false;
		_pauseButton.Disabled = true;
	}

	private void OnStep()
	{
		if (_isPlaying)
		{
			return;
		}
		_volume.Solve();
		UpdateUI();
	}

	private void OnClear()
	{
		_volume.Clear();
		UpdateUI();
	}

	private void OnSparkToggled(bool toggledOn)
	{
		_volume.spark = toggledOn;
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_playButton.Disabled = _isPlaying;
		_pauseButton.Disabled = !_isPlaying;

		foreach (SpeciesPhase sp in AllSpeciesPhases.list)
		{
			_speciesPhaseDropdown.AddItem($"{sp.Species.Name} ({sp.Name})");
		}
		_speciesPhaseDropdown.Selected = 0;

		_addSpeciesPhaseButton.Pressed += OnAddSpeciesPhase;
		_FPSLineEdit.TextChanged += OnFPSLineEditChanged;
		_playButton.Pressed += OnPlay;
		_pauseButton.Pressed += OnPause;
		_stepButton.Pressed += OnStep;
		_clearButton.Pressed += OnClear;
		_sparkCheck.Toggled += OnSparkToggled;

		_dumpDebugButton.Pressed += DumpDebug;

		// Initial condition
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["CH4"],
			n = 200
		});
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["O2"],
			n = 100
		});
		/*
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["C2H5OH"],
			n = 50
		});
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["Fe2O3(cr)"],
			n = 50
		});
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["H2O(L)"],
			n = 1000
		});
		_volume.MaybeAdd(new SpeciesPhaseResource
		{
			SpeciesPhase = AllSpeciesPhases.nameToPhase["C(gr)"],
			n = 25
		});
		*/
		_volume.AssignUAtTP(
			Constants.NISTNormalTemperature,
			Constants.bar
		);

		UpdateUI();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		int fps = (int)Engine.GetFramesPerSecond();
		_FPSLabel.Text = $"FPS: {fps}";
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isPlaying)
		{
			accumulatedTime += delta;
			while (accumulatedTime >= 1.0 / FPSTarget)
			{
				accumulatedTime -= 1.0 / FPSTarget;
				_volume.Solve();
			}
			UpdateUI();
		}
	}
}
