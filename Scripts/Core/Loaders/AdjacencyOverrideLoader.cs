using System.Collections.Generic;
using System.IO;
using Core.Systems;

namespace Core.Loaders
{
    /// <summary>
    /// ENGINE: reads map/adjacencies.csv, the optional adjacency overrides (AdjacencySystem.ApplyOverrides).
    /// Semicolon separated with a header line, one override a line: From;To;Type;Through;Comment. Type
    /// "impassable" cuts the shared border between From and To; any other type adds a link between them (the type
    /// is the game's: "sea" for a strait, say), Through is the province it passes through (-1 or 0 for none).
    /// Lines starting with # and lines with a From or To that isn't a positive number are skipped.
    /// </summary>
    public static class AdjacencyOverrideLoader
    {
        public const string RelativePath = "map/adjacencies.csv";

        /// <summary>The overrides in the data directory's file; empty when there is none.</summary>
        public static List<AdjacencyOverride> Load(string dataDirectory)
        {
            var result = new List<AdjacencyOverride>();
            if (string.IsNullOrEmpty(dataDirectory)) return result;
            string path = Path.Combine(dataDirectory, RelativePath);
            if (!File.Exists(path)) return result;
            bool header = true;
            foreach (string raw in File.ReadLines(path))
            {
                if (header) { header = false; continue; }
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(';');
                if (f.Length < 3 || !ushort.TryParse(f[0], out ushort from) || !ushort.TryParse(f[1], out ushort to)
                    || from == 0 || to == 0) continue;
                ushort through = 0;
                if (f.Length > 3 && int.TryParse(f[3], out int t) && t > 0 && t <= ushort.MaxValue) through = (ushort)t;
                string type = f[2].Trim();
                result.Add(new AdjacencyOverride
                {
                    From = from, To = to, Through = through, Type = type,
                    Remove = type == "impassable",
                });
            }
            ArchonLogger.Log($"AdjacencyOverrideLoader: {result.Count} overrides from {RelativePath}", "core_data_loading");
            return result;
        }
    }
}
