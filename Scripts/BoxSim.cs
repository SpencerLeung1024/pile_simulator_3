using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BoxSim : Node3D
{
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

	private Volume _volume = new Volume(1);
	private bool _isPlaying = false;

	public static Dictionary<string, string> speciesToHexCode = new Dictionary<string, string>
	{
		{"H2", "bf3f3f"},
		{"C", "3f3f3f"},
		{"O2", "ffffff"},
		{"CH4", "bf7f3f"},
		{"H2O", "7fbfff"},
		{"CO2", "7f7f7f"}
	};

	public override void _Ready()
	{
		foreach (SpeciesPhase sp in AllSpeciesPhases.list)
		{
			_speciesPhaseDropdown.AddItem($"{sp.Species.Name} ({sp.Name})");
		}
		_speciesPhaseDropdown.Selected = 0;

		_addSpeciesPhaseButton.Pressed += OnAddSpeciesPhase;
		_playButton.Pressed += OnPlay;
		_pauseButton.Pressed += OnPause;
		_stepButton.Pressed += OnStep;
		_clearButton.Pressed += OnClear;
		_sparkCheck.Toggled += OnSparkToggled;

		UpdateUI();
	}

	public override void _Process(double delta)
	{
		if (_isPlaying)
		{
			_volume.Solve();
		}
		UpdateUI();
	}

	private void UpdateUI()
	{
		List<ResourceDisplayEntry> info = _volume.GetInfo();
		UpdateThermodynamicsLabel(info);
		UpdateResourcesLabel(info);
		UpdateMultiMesh(info);
	}

	private void UpdateThermodynamicsLabel(List<ResourceDisplayEntry> info)
	{
		int fps = (int)Engine.GetFramesPerSecond();
		double H = _volume.U + _volume.P * _volume.Volume;
		double G = H - _volume.T * _volume.S;
		double A = _volume.U - _volume.T * _volume.S;

		_thermodynamicsLabel.Text =
			$"FPS: {fps}\n" +
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
			string hex = speciesToHexCode.GetValueOrDefault(group.Key.Name, "ffffff");
			sb.AppendLine($"[color=#{hex}]{group.Key.Name}[/color]");

			foreach (var entry in group.OrderBy(e => (int)e.SpeciesPhase.Phase))
			{
				sb.AppendLine(
					$"{entry.SpeciesPhase.Phase.ToString().ToLower()}: {Constants.FormatUnit(entry.n, 3, "mol")}, {Constants.FormatUnit(entry.Mass, 3, "kg")}, {Constants.FormatUnit(entry.ResourceVolume * 1e3, 3, "L")}");
			}
		}
		_resourcesLabel.Text = sb.ToString().TrimEnd();
	}

	private void UpdateMultiMesh(List<ResourceDisplayEntry> info)
	{
		int count = info.Count;
		_multiMeshSpeciesPhase.Multimesh.InstanceCount = count;

		if (count == 0)
		{
			return;
		}

		const float minMolarVolume = 1e-4f;
		const float boxSize = 1.0f;
		const float boxHalf = boxSize / 2.0f;

		var phaseVolumes = new double[3];
		var displayVolumes = new List<(ResourceDisplayEntry entry, float vol)>();
		double totalVol = 0.0;

		foreach (var entry in info)
		{
			float v = (float)Math.Max(entry.ResourceVolume / entry.n, minMolarVolume);
			float vol = v * (float)entry.n;
			int p = (int)entry.SpeciesPhase.Phase;
			phaseVolumes[p] += vol;
			totalVol += vol;
			displayVolumes.Add((entry, vol));
		}

		if (totalVol <= 0.0)
		{
			totalVol = 1.0;
			for (int m = 0; m < 3; m++)
			{
				phaseVolumes[m] = 1.0 / 3.0;
			}
		}

		var displayVolumesWithIndex = displayVolumes
			.Select((dv, idx) => (
				entry: dv.entry,
				vol: dv.vol,
				idx: idx,
				phase: dv.entry.SpeciesPhase.Phase
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
			foreach (var (entry, vol, instanceIndex, _) in phaseEntries)
			{
				float width = (float)(vol / phaseVolumes[m]) * boxSize;
				if (width < 0.001f)
				{
					continue;
				}
				float xCenter = xLeft + width / 2.0f;

				var transform = new Transform3D(
					new Basis().Scaled(new Vector3(width, phaseHeight, boxSize)),
					new Vector3(xCenter, yCenter, 0)
				);

				_multiMeshSpeciesPhase.Multimesh.SetInstanceTransform(instanceIndex, transform);

				string hex = speciesToHexCode.GetValueOrDefault(entry.SpeciesPhase.Species.Name, "ffffff");
				var color = new Color($"#{hex}");
				if (entry.SpeciesPhase.Phase == Phase.Liquid)
				{
					color = color.Darkened(0.25f);
				}
				else if (entry.SpeciesPhase.Phase == Phase.Solid)
				{
					color = color.Darkened(0.5f);
				}
				_multiMeshSpeciesPhase.Multimesh.SetInstanceColor(instanceIndex, color);

				xLeft += width;
			}

			yTop -= phaseHeight;
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

		UpdateUI();
	}

	private void OnPlay()
	{
		_isPlaying = true;
	}

	private void OnPause()
	{
		_isPlaying = false;
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
}
