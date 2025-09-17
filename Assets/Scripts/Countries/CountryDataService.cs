using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace ProvinceSystem.Countries
{
    /// <summary>
    /// Service for managing country data and province ownership
    /// </summary>
    public class CountryDataService
    {
        private Dictionary<int, Country> countries = new Dictionary<int, Country>();
        private Dictionary<int, int> provinceOwnership = new Dictionary<int, int>(); // provinceId -> countryId

        public class Country
        {
            public int id;
            public string name;
            public Color color;
            public HashSet<int> provinces = new HashSet<int>();
            public int capitalProvinceId = -1;
            public CountryStats stats = new CountryStats();
        }

        public class CountryStats
        {
            public int provinceCount;
            public int totalPixels;
            public float totalArea;
            public int borderProvinces;
        }

        public Country CreateCountry(int id, string name, Color color, int capitalProvinceId = -1)
        {
            var country = new Country
            {
                id = id,
                name = name,
                color = color,
                capitalProvinceId = capitalProvinceId
            };

            countries[id] = country;

            if (capitalProvinceId >= 0)
            {
                AssignProvinceToCountry(capitalProvinceId, id);
            }

            return country;
        }

        public void AssignProvinceToCountry(int provinceId, int countryId)
        {
            // Remove from previous owner if exists
            if (provinceOwnership.ContainsKey(provinceId))
            {
                int previousOwnerId = provinceOwnership[provinceId];
                if (countries.ContainsKey(previousOwnerId))
                {
                    countries[previousOwnerId].provinces.Remove(provinceId);
                }
            }

            // Assign to new owner
            provinceOwnership[provinceId] = countryId;

            if (countries.ContainsKey(countryId))
            {
                countries[countryId].provinces.Add(provinceId);
            }
        }

        public void RemoveProvinceFromCountry(int provinceId)
        {
            if (provinceOwnership.ContainsKey(provinceId))
            {
                int countryId = provinceOwnership[provinceId];
                if (countries.ContainsKey(countryId))
                {
                    countries[countryId].provinces.Remove(provinceId);
                }
                provinceOwnership.Remove(provinceId);
            }
        }

        public Country GetCountry(int countryId)
        {
            return countries.ContainsKey(countryId) ? countries[countryId] : null;
        }

        public Country GetProvinceOwner(int provinceId)
        {
            if (provinceOwnership.ContainsKey(provinceId))
            {
                int countryId = provinceOwnership[provinceId];
                return GetCountry(countryId);
            }
            return null;
        }

        public int GetProvinceOwnerId(int provinceId)
        {
            return provinceOwnership.ContainsKey(provinceId) ? provinceOwnership[provinceId] : -1;
        }

        public Dictionary<int, Country> GetAllCountries()
        {
            return new Dictionary<int, Country>(countries);
        }

        public List<int> GetUnassignedProvinces(ProvinceManager provinceManager)
        {
            var allProvinceIds = new HashSet<int>();
            var dataService = GetDataService(provinceManager);

            if (dataService != null)
            {
                foreach (var province in dataService.GetAllProvinces().Values)
                {
                    allProvinceIds.Add(province.id);
                }
            }

            return allProvinceIds.Where(id => !provinceOwnership.ContainsKey(id)).ToList();
        }

        public void UpdateCountryStatistics(ProvinceManager provinceManager)
        {
            var dataService = GetDataService(provinceManager);
            if (dataService == null) return;

            foreach (var country in countries.Values)
            {
                country.stats = new CountryStats();
                country.stats.provinceCount = country.provinces.Count;

                int borderProvinces = 0;
                int totalPixels = 0;

                foreach (int provinceId in country.provinces)
                {
                    var province = dataService.GetProvinceById(provinceId);
                    if (province != null)
                    {
                        totalPixels += province.pixels.Count;

                        // Check if this is a border province
                        var neighbors = provinceManager.GetNeighbors(provinceId);
                        bool isBorder = false;

                        foreach (int neighborId in neighbors)
                        {
                            if (GetProvinceOwnerId(neighborId) != country.id)
                            {
                                isBorder = true;
                                break;
                            }
                        }

                        if (isBorder)
                            borderProvinces++;
                    }
                }

                country.stats.totalPixels = totalPixels;
                country.stats.borderProvinces = borderProvinces;
                country.stats.totalArea = totalPixels * 0.01f; // Arbitrary scale
            }
        }

        public List<int> GetCountryBorderProvinces(int countryId, ProvinceManager provinceManager)
        {
            var borderProvinces = new List<int>();
            var country = GetCountry(countryId);

            if (country == null) return borderProvinces;

            foreach (int provinceId in country.provinces)
            {
                var neighbors = provinceManager.GetNeighbors(provinceId);
                bool isBorder = false;

                foreach (int neighborId in neighbors)
                {
                    if (GetProvinceOwnerId(neighborId) != countryId)
                    {
                        isBorder = true;
                        break;
                    }
                }

                if (isBorder)
                    borderProvinces.Add(provinceId);
            }

            return borderProvinces;
        }

        public HashSet<int> GetNeighboringCountries(int countryId, ProvinceManager provinceManager)
        {
            var neighboringCountries = new HashSet<int>();
            var country = GetCountry(countryId);

            if (country == null) return neighboringCountries;

            foreach (int provinceId in country.provinces)
            {
                var neighbors = provinceManager.GetNeighbors(provinceId);

                foreach (int neighborId in neighbors)
                {
                    int neighborOwnerId = GetProvinceOwnerId(neighborId);
                    if (neighborOwnerId != countryId && neighborOwnerId != -1)
                    {
                        neighboringCountries.Add(neighborOwnerId);
                    }
                }
            }

            return neighboringCountries;
        }

        public void Clear()
        {
            countries.Clear();
            provinceOwnership.Clear();
        }

        private Services.ProvinceDataService GetDataService(ProvinceManager provinceManager)
        {
            if (provinceManager == null) return null;

            var field = provinceManager.GetType()
                .GetField("dataService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(provinceManager) as Services.ProvinceDataService;
        }
    }
}