using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: CPU copy of the province ID texture, one ushort per map pixel in the pixel space the
    /// mesh border and label systems use (row 0 = top of the map, x right). Synchronous GPU readback:
    /// call at load / rare rebuilds, not per frame.
    /// </summary>
    public static class ProvinceIdReadback
    {
        public static ushort[] Read(MapTextureManager textureManager)
        {
            var source = textureManager != null ? textureManager.ProvinceIDTexture : null;
            if (source == null) return null;
            int width = textureManager.MapWidth, height = textureManager.MapHeight;

            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var temp = new Texture2D(width, height, TextureFormat.ARGB32, false);
            temp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            temp.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = temp.GetPixels32();
            Object.Destroy(temp);

            var ids = new ushort[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                ids[i] = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[i]);
            return ids;
        }
    }
}
