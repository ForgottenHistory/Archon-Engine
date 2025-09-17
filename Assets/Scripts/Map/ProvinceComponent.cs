using UnityEngine;

namespace ProvinceSystem
{
    /// <summary>
    /// Component attached to individual province GameObjects
    /// </summary>
    public class ProvinceComponent : MonoBehaviour
    {
        public int provinceId;
        public Color provinceColor;
        public string provinceName;

        private Renderer provinceRenderer;
        private Material originalMaterial;

        void Awake()
        {
            provinceRenderer = GetComponent<Renderer>();
            if (provinceRenderer != null)
            {
                originalMaterial = provinceRenderer.sharedMaterial;
            }
        }

        public void SetHighlight(Color highlightColor)
        {
            if (provinceRenderer != null)
            {
                Material mat = new Material(originalMaterial);
                mat.color = highlightColor;
                provinceRenderer.material = mat;
            }
        }

        public void ResetHighlight()
        {
            if (provinceRenderer != null && originalMaterial != null)
            {
                provinceRenderer.material = originalMaterial;
            }
        }

        public void UpdateMaterial(Material newMaterial)
        {
            if (provinceRenderer != null)
            {
                originalMaterial = newMaterial;
                provinceRenderer.sharedMaterial = newMaterial;
            }
        }
    }
}