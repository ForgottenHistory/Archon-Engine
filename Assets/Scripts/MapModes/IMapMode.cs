using UnityEngine;
using System.Collections.Generic;

namespace ProvinceSystem.MapModes
{
    /// <summary>
    /// Interface for different map visualization modes
    /// </summary>
    public interface IMapMode
    {
        string ModeName { get; }
        bool IsActive { get; }

        void Initialize(ProvinceManager provinceManager);
        void OnEnterMode();
        void OnExitMode();
        void UpdateProvinceColor(int provinceId);
        void UpdateAllProvinceColors();
        Color GetProvinceColor(int provinceId);
    }

    /// <summary>
    /// Base implementation of map modes
    /// </summary>
    public abstract class BaseMapMode : IMapMode
    {
        protected ProvinceManager provinceManager;
        protected Dictionary<int, Color> provinceColors = new Dictionary<int, Color>();

        public abstract string ModeName { get; }
        public bool IsActive { get; protected set; }

        public virtual void Initialize(ProvinceManager manager)
        {
            provinceManager = manager;
        }

        public virtual void OnEnterMode()
        {
            IsActive = true;
            UpdateAllProvinceColors();
        }

        public virtual void OnExitMode()
        {
            IsActive = false;
        }

        public abstract void UpdateProvinceColor(int provinceId);

        public virtual void UpdateAllProvinceColors()
        {
            var dataService = GetDataService();
            if (dataService == null) return;

            foreach (var province in dataService.GetAllProvinces().Values)
            {
                UpdateProvinceColor(province.id);
            }

            ApplyAllColors();
        }

        public virtual Color GetProvinceColor(int provinceId)
        {
            return provinceColors.ContainsKey(provinceId) ? provinceColors[provinceId] : Color.gray;
        }

        protected void ApplyAllColors()
        {
            var dataService = GetDataService();
            if (dataService == null) return;

            foreach (var kvp in provinceColors)
            {
                var province = dataService.GetProvinceById(kvp.Key);
                if (province?.gameObject != null)
                {
                    var renderer = province.gameObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        if (renderer.material.color != kvp.Value)
                        {
                            Material mat = new Material(renderer.sharedMaterial);
                            mat.color = kvp.Value;
                            renderer.material = mat;
                        }
                    }
                }
            }
        }

        protected ProvinceSystem.Services.ProvinceDataService GetDataService()
        {
            if (provinceManager == null) return null;

            var field = provinceManager.GetType()
                .GetField("dataService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(provinceManager) as ProvinceSystem.Services.ProvinceDataService;
        }
    }
}