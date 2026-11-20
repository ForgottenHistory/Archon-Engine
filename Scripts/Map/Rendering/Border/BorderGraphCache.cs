using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Core.Modding;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Disk cache for the traced + smoothed border graph (BorderGraph.Build), stored next to
    /// the province image as "provinces.png.borders".
    ///
    /// Keyed on a hash of the province ID readback (not the file timestamp), so it stays valid
    /// exactly as long as the province pixels do, whatever produced them (base data, mods,
    /// overrides). Also keyed on map size and smoothing settings. Any mismatch or read error just
    /// means a miss: the caller traces and saves.
    /// </summary>
    public static class BorderGraphCache
    {
        private const uint Magic = 0x43524742; // "BGRC"
        private const uint Version = 1;

        /// <summary>Cache file path next to the province image, or null when data paths are unavailable.</summary>
        public static string GetCachePath()
        {
            if (!DataFileResolver.IsInitialized) return null;
            string png = DataFileResolver.Resolve("map/provinces.png");
            if (File.Exists(png)) return png + ".borders";
            string bmp = DataFileResolver.Resolve("map/provinces.bmp");
            return File.Exists(bmp) ? bmp + ".borders" : null;
        }

        /// <summary>64-bit content hash of the province IDs (FNV-1a style over 8-byte words, then mixed).</summary>
        public static ulong HashIds(ushort[] ids)
        {
            ulong h = 1469598103934665603UL ^ (ulong)ids.Length;
            ReadOnlySpan<ulong> words = MemoryMarshal.Cast<ushort, ulong>(ids);
            for (int i = 0; i < words.Length; i++)
                h = (h ^ words[i]) * 1099511628211UL;
            for (int i = words.Length * 4; i < ids.Length; i++)
                h = (h ^ ids[i]) * 1099511628211UL;
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33;
            return h;
        }

        public static List<BorderArc> TryLoad(string path, ulong idHash, int width, int height, BorderSmoothingSettings s)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var r = new BinaryReader(File.OpenRead(path));
                if (r.ReadUInt32() != Magic || r.ReadUInt32() != Version) return null;
                if (r.ReadUInt64() != idHash || r.ReadInt32() != width || r.ReadInt32() != height) return null;
                if (r.ReadSingle() != s.PreSimplifyTolerance || r.ReadInt32() != s.ChaikinIterations ||
                    r.ReadSingle() != s.PostSimplifyTolerance) return null;

                int count = r.ReadInt32();
                var arcs = new List<BorderArc>(count);
                for (int a = 0; a < count; a++)
                {
                    var arc = new BorderArc
                    {
                        ProvinceA = r.ReadUInt16(),
                        ProvinceB = r.ReadUInt16(),
                        Closed = r.ReadBoolean()
                    };
                    int n = r.ReadInt32();
                    byte[] raw = r.ReadBytes(n * 8);
                    if (raw.Length != n * 8) return null;
                    arc.Points = new Vector2[n];
                    MemoryMarshal.Cast<byte, Vector2>(raw).CopyTo(arc.Points);
                    arcs.Add(arc);
                }
                return arcs;
            }
            catch (Exception e)
            {
                ArchonLogger.LogWarning($"BorderGraphCache: could not read {path} ({e.Message}), retracing", "map_initialization");
                return null;
            }
        }

        public static void Save(string path, ulong idHash, int width, int height, BorderSmoothingSettings s, List<BorderArc> arcs)
        {
            if (string.IsNullOrEmpty(path)) return;
            string tmp = path + ".tmp";
            try
            {
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(Magic); w.Write(Version);
                    w.Write(idHash); w.Write(width); w.Write(height);
                    w.Write(s.PreSimplifyTolerance); w.Write(s.ChaikinIterations); w.Write(s.PostSimplifyTolerance);
                    w.Write(arcs.Count);
                    foreach (var arc in arcs)
                    {
                        w.Write(arc.ProvinceA); w.Write(arc.ProvinceB); w.Write(arc.Closed);
                        w.Write(arc.Points.Length);
                        w.Write(MemoryMarshal.Cast<Vector2, byte>(arc.Points.AsSpan()));
                    }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                ArchonLogger.LogWarning($"BorderGraphCache: could not write {path} ({e.Message})", "map_initialization");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
