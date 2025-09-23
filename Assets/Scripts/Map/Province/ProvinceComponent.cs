using UnityEngine;

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

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            originalColor = meshRenderer.material.color;
        }
    }

    void OnMouseEnter()
    {
        if (lastHovered != this)
        {
            lastHovered = this;
            //Debug.Log($"Mouse entered province: {provinceName} (ID: {provinceId}, Pixels: {pixelCount})");

            // Highlight on hover
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = originalColor * 1.2f; // Brighten
            }
        }
    }

    void OnMouseExit()
    {
        if (lastHovered == this)
        {
            lastHovered = null;

            // Restore original color
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = originalColor;
            }
        }
    }

    void OnMouseDown()
    {
        Debug.Log($"Clicked province: {provinceName} (ID: {provinceId}, Province Color: {provinceColor}, Display Color: {displayColor})");
    }
}