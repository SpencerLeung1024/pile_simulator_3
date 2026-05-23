using System;
using System.Collections.Generic;
using System.IO;

public static class NuclideDataLoader
{
	private class RawMassEntry
	{
		public uint Z;
		public uint N;
		public uint A;
		public double BindingEnergyPerNucleon_keV;
	}

	private class RawNubaseEntry
	{
		public uint Z;
		public uint A;
		public uint N;
		public double HalfLife_s;
		public Dictionary<string, double> DecayModes;
		public double IsotopicAbundance;
	}

	public static void Load(string massPath, string nubasePath)
	{
		var massLines = File.ReadAllLines(massPath);
		var nubaseLines = File.ReadAllLines(nubasePath);

		var massEntries = ParseMassFile(massLines);
		var nubaseEntries = ParseNubaseFile(nubaseLines);

		BuildNuclides(massEntries, nubaseEntries);
	}

	private static Dictionary<(uint Z, uint N), RawMassEntry> ParseMassFile(string[] lines)
	{
		var entries = new Dictionary<(uint, uint), RawMassEntry>();
		bool inData = false;

		foreach (var line in lines)
		{
			if (string.IsNullOrEmpty(line))
				continue;

			if (!inData)
			{
				if (line.Contains("1N-Z"))
				{
					inData = true;
				}
				continue;
			}

			if (line.Length < 67)
				continue;

			if (!int.TryParse(line.Substring(4, 5).Trim(), out int ni))
				continue;
			if (!int.TryParse(line.Substring(9, 5).Trim(), out int zi))
				continue;

			uint Z = (uint)zi;
			uint N = (uint)ni;

			if (Z == 0 && N == 0)
				continue;

			uint A = Z + N;
			if (line.Length >= 19)
				if (uint.TryParse(line.Substring(14, 5).Trim(), out uint ai))
					A = ai;

			double BePerA_keV = 0;
			bool hasBindingEnergy = false;
			if (line.Length >= 67)
			{
				string beRaw = line.Substring(54, 13).Trim();
				if (!string.IsNullOrEmpty(beRaw) && !beRaw.Contains("*"))
				{
					beRaw = beRaw.Replace("#", "");
					if (beRaw.Length > 0 && double.TryParse(beRaw, out BePerA_keV))
						hasBindingEnergy = true;
				}
			}

			if (!hasBindingEnergy)
				continue;

			entries[(Z, N)] = new RawMassEntry
			{
				Z = Z,
				N = N,
				A = A,
				BindingEnergyPerNucleon_keV = BePerA_keV
			};
		}

		return entries;
	}

	private static Dictionary<(uint Z, uint N), RawNubaseEntry> ParseNubaseFile(string[] lines)
	{
		var entries = new Dictionary<(uint, uint), RawNubaseEntry>();

		for (int l = 0; l < lines.Length; l++)
		{
			var line = lines[l];
			int lineNum = l + 1;
			if (string.IsNullOrEmpty(line))
				continue;

			if (line.StartsWith("#"))
				continue;

			if (line.Length < 8)
				continue;

			if (!int.TryParse(line.Substring(0, 3).Trim(), out int ai))
				continue;

			string zzzi = line.Substring(4, Math.Min(4, line.Length - 4)).Trim();
			if (zzzi.Length < 3)
				continue;

			if (!int.TryParse(zzzi.Substring(0, 3), out int zi))
				continue;

			uint Z = (uint)zi;
			uint A = (uint)ai;

			if (A < Z)
				continue;

			uint N = A - Z;

			if (entries.ContainsKey((Z, N)))
				continue;

			double halfLife_s;
			if (line.Length >= 80)
			{
				string hlField = line.Substring(69, Math.Min(9, line.Length - 69)).Trim();
				string unitField = line.Length >= 80 ? line.Substring(78, Math.Min(2, line.Length - 78)).Trim() : "";

				try
				{
					halfLife_s = ConvertHalfLifeToSeconds(hlField, unitField);
				}
				catch (Exception ex)
				{
					throw new Exception($"NUBASE line {lineNum}: {ex.Message}");
				}
			}
			else
			{
				halfLife_s = -1;
			}

			string brField = line.Length > 119 ? line.Substring(119, Math.Min(90, line.Length - 119)).Trim() : "";
			var decayModes = new Dictionary<string, double>();
			double isotopicAbundance = -1;

			if (!string.IsNullOrEmpty(brField))
			{
				var tokens = brField.Split(';', StringSplitOptions.RemoveEmptyEntries);
				foreach (var token in tokens)
				{
					string trimmed = token.Trim();
					if (string.IsNullOrEmpty(trimmed))
						continue;

					int eqIdx = trimmed.IndexOf('=');
					string mode;
					string rest;

					if (eqIdx >= 0)
					{
						mode = trimmed.Substring(0, eqIdx).Trim();
						rest = trimmed.Substring(eqIdx + 1).Trim();
					}
					else
					{
						int spaceIdx = trimmed.IndexOf(' ');
						if (spaceIdx >= 0)
						{
							mode = trimmed.Substring(0, spaceIdx).Trim();
							rest = trimmed.Substring(spaceIdx + 1).Trim();
						}
						else
						{
							mode = trimmed;
							rest = "";
						}
					}

					if (string.IsNullOrEmpty(mode))
						continue;

					if (mode == "IS")
					{
						isotopicAbundance = ParseNubaseDouble(rest);
						continue;
					}

					double value;
					if (rest.Length == 0 || rest == "?")
					{
						value = -1;
					}
					else
					{
						string numStr;
						int uncSpace = rest.IndexOf(' ');
						if (uncSpace >= 0)
							numStr = rest.Substring(0, uncSpace).Trim();
						else
							numStr = rest.Trim();

						value = ParseNubaseDouble(numStr);
					}

					decayModes[mode] = value;
				}
			}

			entries[(Z, N)] = new RawNubaseEntry
			{
				Z = Z,
				A = A,
				N = N,
				HalfLife_s = halfLife_s,
				DecayModes = decayModes,
				IsotopicAbundance = isotopicAbundance
			};
		}

		return entries;
	}

	private static double ConvertHalfLifeToSeconds(string hlField, string unitField)
	{
		if (string.IsNullOrEmpty(hlField))
			return -1;

		if (hlField == "stbl")
			return double.PositiveInfinity;

		if (hlField == "p-unst")
			return -2;

		if (string.IsNullOrEmpty(unitField))
			throw new Exception($"Unknown half-life unit: '' (hlField='{hlField}')");

		double factor = unitField switch
		{
			"ys" => 1e-24,
			"zs" => 1e-21,
			"as" => 1e-18,
			"fs" => 1e-15,
			"ps" => 1e-12,
			"ns" => 1e-9,
			"us" => 1e-6,
			"ms" => 1e-3,
			"s" => 1.0,
			"m" => 60.0,
			"h" => 3600.0,
			"d" => 86400.0,
			"y" => Constants.year,
			"ky" => Constants.year * 1e3,
			"My" => Constants.year * 1e6,
			"Gy" => Constants.year * 1e9,
			"Ty" => Constants.year * 1e12,
			"Py" => Constants.year * 1e15,
			"Ey" => Constants.year * 1e18,
			"Zy" => Constants.year * 1e21,
			"Yy" => Constants.year * 1e24,
			_ => throw new Exception($"Unknown half-life unit: '{unitField}' (hlField='{hlField}')")
		};

		hlField = hlField.Replace(">", "").Replace("<", "").Replace("~", "").Replace("#", "");
		if (string.IsNullOrEmpty(hlField))
			return -1;

		if (!double.TryParse(hlField, out double value))
			throw new Exception($"Could not parse half-life value '{hlField}'");

		return value * factor;
	}

	private static double ParseNubaseDouble(string s)
	{
		s = s.Replace(">", "").Replace("<", "").Replace("~", "").Replace("#", "");
		if (string.IsNullOrEmpty(s))
			return -1;
		if (!double.TryParse(s, out double value))
			return -1;
		return value;
	}

	private static void BuildNuclides(
		Dictionary<(uint Z, uint N), RawMassEntry> massEntries,
		Dictionary<(uint Z, uint N), RawNubaseEntry> nubaseEntries)
	{
		foreach (var kv in nubaseEntries)
		{
			var (Z, N) = kv.Key;
			var nubase = kv.Value;

			if (nubase.HalfLife_s == -2)
				continue;

			double BePerA_eV;
			if (massEntries.TryGetValue((Z, N), out var mass))
			{
				BePerA_eV = mass.BindingEnergyPerNucleon_keV * 1000.0;
			}
			else
			{
				double beMeV = Nuclides.SemiEmpiricalMassFormula(Z, N);
				BePerA_eV = beMeV * 1e6 / (Z + N);
			}

			var nuclide = new Nuclide(Z, N, BePerA_eV, nubase.HalfLife_s, nubase.DecayModes);
			Nuclides.table[Z, N] = nuclide;

			if (nubase.IsotopicAbundance >= 0)
			{
				var element = Elements.ByZ(Z);
				if (!Nuclides.abundances.ContainsKey(element))
					Nuclides.abundances[element] = new Dictionary<Nuclide, double>();
				Nuclides.abundances[element][nuclide] = nubase.IsotopicAbundance;
			}
		}

		foreach (var kv in massEntries)
		{
			var (Z, N) = kv.Key;
			if (Nuclides.table[Z, N] != null)
				continue;

			var mass = kv.Value;

			var nuclide = new Nuclide(
				Z, N,
				mass.BindingEnergyPerNucleon_keV * 1000.0,
				double.PositiveInfinity,
				new Dictionary<string, double>()
			);
			Nuclides.table[Z, N] = nuclide;
		}
	}
}
