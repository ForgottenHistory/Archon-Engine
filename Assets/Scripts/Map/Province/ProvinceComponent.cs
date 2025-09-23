using UnityEngine;
using MapModes;

public class ProvinceComponent : MonoBehaviour
{
    public int provinceId;
    public string provinceName;
    public Color provinceColor; // The unique province identifier color
    public Color displayColor;  // The actual displayed color (country/political)
    public int pixelCount;

    private static ProvinceComponent lastHovered;
    private MeshRenderer meshRenderer;
    private Color originalColor;
    private MapModeManager mapModeManager;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            originalColor = meshRenderer.material.color;
        }

        // Find the map mode manager
        mapModeManager = FindObjectOfType<MapModeManager>();
    }

    void OnMouseEnter()
    {
        if (lastHovered != this)
        {
            lastHovered = this;
            //Debug.Log($"Mouse entered province: {provinceName} (ID: {provinceId}, Pixels: {pixelCount})");

            // Highlight on hover - use current color, not original
            if (meshRenderer != null && meshRenderer.material != null)
            {
                Color currentColor = GetCurrentMapModeColor();
                meshRenderer.material.color = currentColor * 1.2f; // Brighten
            }
        }
    }

    void OnMouseExit()
    {
        if (lastHovered == this)
        {
            lastHovered = null;

            // Restore current map mode color, not original
            if (meshRenderer != null && meshRenderer.material != null)
            {
                Color currentColor = GetCurrentMapModeColor();
                meshRenderer.material.color = currentColor;
            }
        }
    }

    void OnMouseDown()
    {
        Debug.Log($"Clicked province: {provinceName} (ID: {provinceId}, Province Color: {provinceColor}, Display Color: {displayColor})");
    }

    private Color GetCurrentMapModeColor()
    {
        // If we have a map mode manager and it's active, get color from current mode
        if (mapModeManager != null && mapModeManager.CurrentMode != null)
        {
            return mapModeManager.CurrentMode.GetProvinceColor(provinceId);
        }

        // Fallback to original color if no map mode is active
        return originalColor;
    }

    // Method to update the stored original color when map modes change
    public void UpdateOriginalColor(Color newColor)
    {
        originalColor = newColor;
    }
}