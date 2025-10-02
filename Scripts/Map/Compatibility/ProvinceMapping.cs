using UnityEngine;
using System.Collections.Generic;

namespace Map.Rendering
{
    /// <summary>
    /// Compatibility shim for legacy ProvinceMapping
    /// Bridges the gap between old system expectations and new ProvinceMapProcessor
    /// </summary>
    public class ProvinceMapping
    {
        private Dictionary<ushort, ProvinceInfo> provinces = new Dictionary<ushort, ProvinceInfo>();
        private Dictionary<Color32, ushort> colorToID = new Dictionary<Color32, ushort>();

        public int ProvinceCount => provinces.Count;

        public void AddProvince(ushort id, Color32 identifierColor)
        {
            provinces[id] = new ProvinceInfo
            {
                ID = id,
                IdentifierColor = identifierColor,
                Pixels = new List<Vector2Int>()
            };

            colorToID[identifierColor] = id;
        }

        public void AddPixelToProvince(ushort provinceID, int x, int y)
        {
            if (provinces.TryGetValue(provinceID, out var province))
            {
                province.Pixels.Add(new Vector2Int(x, y));
            }
        }

        public bool HasProvince(ushort provinceID)
        {
            return provinces.ContainsKey(provinceID);
        }

        public List<Vector2Int> GetProvincePixels(ushort provinceID)
        {
            return provinces.TryGetValue(provinceID, out var province) ? province.Pixels : new List<Vector2Int>();
        }

        public Color32 GetProvinceIdentifierColor(ushort provinceID)
        {
            return provinces.TryGetValue(provinceID, out var province) ? province.IdentifierColor : Color.black;
        }

        public ushort GetProvinceByColor(Color32 color)
        {
            return colorToID.TryGetValue(color, out ushort id) ? id : (ushort)0;
        }

        public Dictionary<ushort, ProvinceInfo> GetAllProvinces()
        {
            return new Dictionary<ushort, ProvinceInfo>(provinces);
        }

        [System.Serializable]
        public class ProvinceInfo
        {
            public ushort ID;
            public Color32 IdentifierColor;
            public List<Vector2Int> Pixels;
            public int PixelCount => Pixels?.Count ?? 0;
        }
    }
}