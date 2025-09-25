using System.Collections.Generic;
using UnityEngine;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Service for managing province data - texture-based rendering system
    /// </summary>
    public class ProvinceDataService
    {
        private Dictionary<Color, ProvinceData> provinces = new Dictionary<Color, ProvinceData>();
        private Dictionary<int, ProvinceData> provinceById = new Dictionary<int, ProvinceData>();

        public void RegisterProvince(ProvinceData province)
        {
            provinces[province.color] = province;
            provinceById[province.id] = province;
        }

        public ProvinceData GetProvinceByColor(Color color)
        {
            return provinces.ContainsKey(color) ? provinces[color] : null;
        }

        public ProvinceData GetProvinceById(int id)
        {
            return provinceById.ContainsKey(id) ? provinceById[id] : null;
        }

        // GameObject methods removed - using texture-based rendering

        public Dictionary<Color, ProvinceData> GetAllProvinces()
        {
            return new Dictionary<Color, ProvinceData>(provinces);
        }

        public int GetProvinceCount()
        {
            return provinces.Count;
        }

        public void Clear()
        {
            provinces.Clear();
            provinceGameObjects.Clear();
            provinceById.Clear();
        }

        public ProvinceStatistics GetStatistics()
        {
            var stats = new ProvinceStatistics();

            if (provinces.Count == 0)
                return stats;

            stats.totalProvinces = provinces.Count;
            stats.minPixels = int.MaxValue;

            foreach (var province in provinces.Values)
            {
                int pixelCount = province.pixels.Count;
                stats.totalPixels += pixelCount;
                stats.minPixels = Mathf.Min(stats.minPixels, pixelCount);
                stats.maxPixels = Mathf.Max(stats.maxPixels, pixelCount);
            }

            stats.averagePixels = stats.totalPixels / stats.totalProvinces;

            return stats;
        }

        public class ProvinceStatistics
        {
            public int totalProvinces;
            public int totalPixels;
            public int averagePixels;
            public int minPixels;
            public int maxPixels;
        }
    }
}