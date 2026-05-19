using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static class NASA9Loader
{
    private class RawEntry
    {
        public string RawName;
        public string Comment;
        public Dictionary<Element, double> Elements;
        public double Charge;
        public int PhaseCode;
        public double MolecularWeight;
        public double Hf;
        public List<(double Tlow, double Thigh, double[] a)> Intervals;
    }

    public static void Load(string path)
    {
        var lines = File.ReadAllLines(path);
        var entries = ParseFile(lines);
        BuildSpecies(entries);
    }

    private static List<RawEntry> ParseFile(string[] lines)
    {
        var entries = new List<RawEntry>();
        int i = 0;

        while (i < lines.Length && lines[i].TrimStart().StartsWith("!"))
            i++;

        if (i < lines.Length && lines[i].Trim() == "thermo")
            i++;

        if (i < lines.Length)
            i++;

        while (i < lines.Length)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }
            if (line.TrimStart().StartsWith("END"))
                break;

            string name = line.Length >= 16 ? line.Substring(0, 16).Trim() : line.Trim();
            string comment = line.Length > 16 ? line.Substring(16).Trim() : "";

            if (name.StartsWith("Inert") || name == "e-")
            {
                i++;
                SkipEntry(lines, ref i);
                continue;
            }

            i++;
            if (i >= lines.Length) break;

            string headerLine = lines[i];
            i++;

            int numIntervals = int.Parse(headerLine.Substring(0, 2).Trim());

            var elements = new Dictionary<Element, double>();
            double charge = 0;
            for (int g = 0; g < 5; g++)
            {
                int offset = 10 + g * 8;
                if (offset + 8 > headerLine.Length) break;
                string field = headerLine.Substring(offset, Math.Min(8, headerLine.Length - offset));
                string symbol = field.Substring(0, Math.Min(2, field.Length)).Trim();
                if (symbol.Length == 0) continue;

                string countStr = field.Length > 2 ? field.Substring(2).Trim() : "";
                if (countStr.Length == 0) continue;

                if (!double.TryParse(countStr, out double count)) continue;
                if (count == 0) continue;

                if (symbol.StartsWith("I") && symbol.Length > 1)
                    symbol = symbol.Substring(1);

                if (symbol == "E")
                {
                    charge = -count;
                    continue;
                }

                if (Elements.symbolToElement.TryGetValue(symbol, out var element))
                    elements[element] = count;
            }

            int phaseCode = 0;
            if (headerLine.Length > 51)
                int.TryParse(headerLine.Substring(51, 1), out phaseCode);

            string afterPhase = headerLine.Length > 52 ? headerLine.Substring(52).Trim() : "";
            string[] rest = afterPhase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length < 2) continue;

            double molWt = double.Parse(rest[0]);
            double hf = double.Parse(rest[rest.Length - 1]);

            var intervals = new List<(double Tlow, double Thigh, double[] a)>();
            for (int j = 0; j < numIntervals; j++)
            {
                if (i >= lines.Length) break;
                string intervalLine = lines[i];
                i++;

                double tLow = double.Parse(intervalLine.Substring(0, 10).Trim());
                double tHigh = double.Parse(intervalLine.Substring(10, 10).Trim());

                if (i >= lines.Length) break;
                string coeffLine1 = lines[i];
                i++;

                if (i >= lines.Length) break;
                string coeffLine2 = lines[i];
                i++;

                var a = new double[9];
                for (int c = 0; c < 5; c++)
                    a[c] = ParseFortranDouble(coeffLine1, c * 16);
                a[5] = ParseFortranDouble(coeffLine2, 0);
                a[6] = ParseFortranDouble(coeffLine2, 16);
                a[7] = ParseFortranDouble(coeffLine2, 48);
                a[8] = ParseFortranDouble(coeffLine2, 64);

                intervals.Add((tLow, tHigh, a));
            }

            entries.Add(new RawEntry
            {
                RawName = name,
                Comment = comment,
                Elements = elements,
                Charge = charge,
                PhaseCode = phaseCode,
                MolecularWeight = molWt,
                Hf = hf,
                Intervals = intervals
            });
        }

        return entries;
    }

    private static void SkipEntry(string[] lines, ref int i)
    {
        if (i >= lines.Length) return;
        string headerLine = lines[i];
        int numIntervals = 0;
        if (headerLine.Length >= 2)
            int.TryParse(headerLine.Substring(0, 2).Trim(), out numIntervals);
        i++;
        for (int j = 0; j < numIntervals * 3; j++)
        {
            if (i >= lines.Length) break;
            i++;
        }
    }

    private static double ParseFortranDouble(string line, int start)
    {
        if (start + 16 > line.Length)
            return 0;
        string s = line.Substring(start, Math.Min(16, line.Length - start)).Trim();
        if (string.IsNullOrEmpty(s))
            return 0;
        return double.Parse(s.Replace('D', 'E'));
    }

    private static void BuildSpecies(List<RawEntry> entries)
    {
        var entriesBySpecies = entries
            .Where(e => e.Elements.Count > 0 && e.Elements.Values.All(v => Math.Abs(v - Math.Round(v)) < 0.01))
            .GroupBy(e => GetBaseName(e.RawName) + "|" + e.Charge.ToString("F1"))
            .ToList();

        foreach (var group in entriesBySpecies)
        {
            var entriesInGroup = group.ToList();
            var first = entriesInGroup[0];

            var formula = new Dictionary<Element, uint>();
            foreach (var kv in first.Elements)
            {
                uint count = (uint)Math.Round(kv.Value);
                if (Math.Abs(count - kv.Value) > 0.01)
                    throw new Exception($"Non-integer element count for {kv.Key.Symbol} in {first.RawName}: {kv.Value}");
                formula[kv.Key] = count;
            }

            string baseName = GetBaseName(first.RawName);
            if (first.Charge != 0)
            {
                int absCharge = (int)Math.Abs(first.Charge);
                if (absCharge == 1)
                    baseName += first.Charge > 0 ? "+" : "-";
                else
                    baseName += (first.Charge > 0 ? "+" : "-") + absCharge;
            }

            var phases = new List<SpeciesPhase>();
            foreach (var entry in entriesInGroup)
            {
                Phase phase = entry.PhaseCode switch
                {
                    0 => Phase.Gas,
                    1 => Phase.Solid,
                    2 => Phase.Liquid,
                    _ => throw new Exception($"Unknown phase code {entry.PhaseCode} for {entry.RawName}")
                };

                var bounds = new List<double>();
                foreach (var interval in entry.Intervals)
                    bounds.Add(interval.Tlow);
                bounds.Add(entry.Intervals.Last().Thigh);

                var allVecA = new List<double[]>();
                foreach (var interval in entry.Intervals)
                    allVecA.Add(interval.a);

                var hcf = new NASA9Function
                {
                    TemperatureBoundaries = bounds.ToArray(),
                    all_vec_a = allVecA
                };

                string phaseName = ExtractPhaseName(entry.Comment, phase);

                var sp = new SpeciesPhase
                {
                    Species = null,
                    Phase = phase,
                    Name = phaseName,
                    HeatCapacityFunction = hcf,
                    EquationOfState = null
                };
                phases.Add(sp);
            }

            var species = new Species(baseName, formula, 0.0, phases);
            foreach (var sp in phases)
                sp.Species = species;

            AllSpecies.List.Add(species);
            AllSpecies.nameToSpecies[baseName] = species;

            foreach (var entry in entriesInGroup)
            {
                var matchingPhase = phases.FirstOrDefault(sp =>
                    (entry.PhaseCode == 0 && sp.Phase == Phase.Gas) ||
                    (entry.PhaseCode == 1 && sp.Phase == Phase.Solid) ||
                    (entry.PhaseCode == 2 && sp.Phase == Phase.Liquid));

                if (matchingPhase != null)
                {
                    AllSpeciesPhases.List.Add(matchingPhase);
                    AllSpeciesPhases.nameToPhase[entry.RawName] = matchingPhase;
                }
            }
        }
    }

    private static string GetBaseName(string rawName)
    {
        return Regex.Replace(rawName, @"\([^)]*\)$", "");
    }

    private static string ExtractPhaseName(string comment, Phase phase)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            int dotIndex = comment.IndexOf('.');
            int colonIndex = comment.IndexOf(':');
            int endIndex = comment.Length;
            if (dotIndex >= 0 && dotIndex < endIndex) endIndex = dotIndex;
            if (colonIndex >= 0 && colonIndex < endIndex) endIndex = colonIndex;

            string firstPart = comment.Substring(0, endIndex).Trim();

            if (firstPart.Length > 0 && firstPart.Length < 30)
            {
                var skipWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Hf", "Ref-Elm", "Ref-Species", "Gurvich", "Cox", "Chase",
                    "TRC", "Moore", "Gordon", "Jacox", "Martin", "Johnson",
                    "Dorofeeva", "Woolley", "Haar", "Keenan", "Stimson",
                    "Hotop", "Ruscic", "Bunker", "Chen", "Curtiss", "Yu",
                    "Shimanouchi", "Zehe", "McBride", "Barin", "CODATA"
                };

                if (!skipWords.Any(w => firstPart.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                    return firstPart;
            }
        }

        return phase switch
        {
            Phase.Gas => "Gas",
            Phase.Liquid => "Liquid",
            Phase.Solid => "Solid",
            _ => "Unknown"
        };
    }
}
