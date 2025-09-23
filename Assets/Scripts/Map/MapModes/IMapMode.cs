using UnityEngine;
using System.Collections.Generic;

namespace MapModes
{
    public interface IMapMode
    {
        string ModeName { get; }
        bool IsActive { get; }

        void Initialize(MapController mapController);
        void OnEnterMode();
        void OnExitMode();
        void UpdateProvinceColor(int provinceId);
        void UpdateAllProvinceColors();
        Color GetProvinceColor(int provinceId);
    }

    public abstract class BaseMapMode : IMapMode
    {
        protected MapController mapController;
        protected Dictionary<int, Color> provinceColors = new Dictionary<int, Color>();

        public abstract string ModeName { get; }
        public bool IsActive { get; protected set; }

        public virtual void Initialize(MapController controller)
        {
            mapController = controller;
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
            if (mapController?.ProvinceComponents == null) return;

            foreach (var kvp in mapController.ProvinceComponents)
            {
                UpdateProvinceColor(kvp.Key);
            }

            ApplyAllColors();
        }

        public virtual Color GetProvinceColor(int provinceId)
        {
            return provinceColors.ContainsKey(provinceId) ? provinceColors[provinceId] : Color.gray;
        }

        protected void ApplyAllColors()
        {
            if (mapController?.ProvinceComponents == null) return;

            foreach (var kvp in provinceColors)
            {
                if (mapController.ProvinceComponents.ContainsKey(kvp.Key))
                {
                    var provinceComponent = mapController.ProvinceComponents[kvp.Key];
                    var renderer = provinceComponent.GetComponent<MeshRenderer>();
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

        protected void RestoreOriginalColors()
        {
            if (mapController?.ProvinceComponents == null) return;

            foreach (var kvp in mapController.ProvinceComponents)
            {
                var provinceComponent = kvp.Value;
                var renderer = provinceComponent.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material.color = provinceComponent.displayColor;
                }
            }
        }
    }
}