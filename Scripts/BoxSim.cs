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

		_volume.T = 300.0;
		_volume.P = Constants.bar;

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
		VolumeDisplayInfo info = _volume.GetInfo();
		UpdateThermodynamicsLabel(info);
		UpdateResourcesLabel(info);
		UpdateMultiMesh(info);
	}

	private void UpdateThermodynamicsLabel(VolumeDisplayInfo info)
	{
		int fps = Engine.GetFramesPerSecond();
		double H = info.U + info.P * info.V;
		double G = H - info.T * info.S;
		double A = info.U - info.T * info.S;

		_thermodynamicsLabel.Text =
			$"FPS: {fps}\n" +
			$"U: {FormatEnergy(info.U)}\n" +
			$"T: {info.T:F0} K\n" +
			$"V: {info.V:F2} m^3\n" +
			$"P: {FormatPressure(info.P)}\n" +
			$"S: {FormatEntropy(info.S)}\n" +
			$"H = U + PV: {FormatEnergy(H)}\n" +
			$"G = H - TS: {FormatEnergy(G)}\n" +
			$"A = U - TS: {FormatEnergy(A)}";
	}

	private void UpdateResourcesLabel(VolumeDisplayInfo info)
	{
		if (info.Resources.Count == 0)
		{
			_resourcesLabel.Text = "";
			return;
		}

		var speciesGroups = info.Resources
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
					$"{entry.SpeciesPhase.Phase.ToString().ToLower()}: {FormatMoles(entry.n)}, {FormatMass(entry.Mass)}, {FormatVolume(entry.PhaseVolume)}");
			}
		}
		_resourcesLabel.Text = sb.ToString().TrimEnd();
	}

	private void UpdateMultiMesh(VolumeDisplayInfo info)
	{
		var visibleResources = info.Resources
			.Where(r => r.n > Constants.n_jMin)
			.ToList();

		int count = visibleResources.Count;
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

		foreach (var entry in visibleResources)
		{
			float v = (float)Math.Max(entry.PhaseVolume / entry.n, minMolarVolume);
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
					new Basis(new Vector3(width, phaseHeight, boxSize)),
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

		if (!double.TryParse(_amountLineEdit.Text, out double amount) || amount <= 0)
		{
			return;
		}

		if (double.TryParse(_temperatureLineEdit.Text, out double temperature) && temperature > 0)
		{
			_volume.T = temperature;
		}

		SpeciesPhaseResource existing = null;
		foreach (var r in _volume.Resources)
		{
			if (r.SpeciesPhase == selectedPhase)
			{
				existing = r;
				break;
			}
		}

		if (existing != null)
		{
			existing.n += amount;
		}
		else
		{
			_volume.Resources.Add(new SpeciesPhaseResource
			{
				SpeciesPhase = selectedPhase,
				n = amount
			});
		}

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
		_volume.Resources.Clear();
		_volume.T = 300.0;
		_volume.P = Constants.bar;
		UpdateUI();
	}

	private void OnSparkToggled(bool toggledOn)
	{
		_volume.spark = toggledOn;
	}

	private static string FormatEnergy(double joules)
	{
		double mj = joules / 1e6;
		if (Math.Abs(mj) >= 100.0)
		{
			return $"{mj:F0} MJ";
		}
		return $"{mj:F1} MJ";
	}

	private static string FormatPressure(double pascals)
	{
		return $"{(pascals / 1000.0):F0} kPa";
	}

	private static string FormatEntropy(double joulesPerKelvin)
	{
		return $"{(joulesPerKelvin / 1000.0):F0} kJ / K";
	}

	private static string FormatMoles(double mol)
	{
		if (mol >= 1000.0)
		{
			return $"{mol / 1000.0:F2} kmol";
		}
		if (mol < 100.0)
		{
			return $"{mol:F1} mol";
		}
		return $"{mol:F0} mol";
	}

	private static string FormatMass(double kg)
	{
		if (kg >= 1.0)
		{
			return $"{kg:F2} kg";
		}
		if (kg >= 0.001)
		{
			return $"{kg * 1000.0:F0} g";
		}
		return $"{(kg * 1e6):F0} mg";
	}

	private static string FormatVolume(double m3)
	{
		double liters = m3 * 1000.0;
		if (liters >= 1000.0)
		{
			return $"{liters / 1000.0:F2} m^3";
		}
		return $"{liters:F0} L";
	}
}
