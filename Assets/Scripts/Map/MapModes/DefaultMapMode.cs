using UnityEngine;

namespace MapModes
{
    public class DefaultMapMode : BaseMapMode
    {
        public override string ModeName => "Default";

        public override void UpdateProvinceColor(int provinceId)
        {
            // Use the actual display color (original rendered color)
            if (mapController?.ProvinceComponents != null &&
                mapController.ProvinceComponents.ContainsKey(provinceId))
            {
                var provinceComponent = mapController.ProvinceComponents[provinceId];
                // Use the displayColor (the original rendered color)
                provinceColors[provinceId] = provinceComponent.displayColor;
            }
            else
            {
                // Fallback to gray if component not found
                provinceColors[provinceId] = Color.gray;
            }
        }

        public override void OnExitMode()
        {
            base.OnExitMode();
            // No need to restore colors since this mode shows the original colors
        }
    }
}