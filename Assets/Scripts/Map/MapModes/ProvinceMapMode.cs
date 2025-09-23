using UnityEngine;

namespace MapModes
{
    public class ProvinceMapMode : BaseMapMode
    {
        public override string ModeName => "Province";

        public override void UpdateProvinceColor(int provinceId)
        {
            // Use the actual province identifier color, not random colors
            if (mapController?.ProvinceComponents != null &&
                mapController.ProvinceComponents.ContainsKey(provinceId))
            {
                var provinceComponent = mapController.ProvinceComponents[provinceId];
                // Use the provinceColor (unique identifier color) not displayColor
                provinceColors[provinceId] = provinceComponent.provinceColor;
            }
            else
            {
                // Fallback to a deterministic color based on province ID if component not found
                Random.InitState(provinceId);
                provinceColors[provinceId] = new Color(
                    Random.Range(0.3f, 1f),
                    Random.Range(0.3f, 1f),
                    Random.Range(0.3f, 1f),
                    1f
                );
            }
        }

        public override void OnExitMode()
        {
            base.OnExitMode();
            RestoreOriginalColors();
        }
    }
}