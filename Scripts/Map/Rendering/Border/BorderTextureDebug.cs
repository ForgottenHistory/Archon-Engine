using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using Core;

namespace Map.Rendering
{
    public static class BorderTextureDebug
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Save DistanceField Texture to PNG")]
        public static void SaveDistanceFieldTexture()
        {
            var textureManager = Object.FindObjectOfType<MapTextureManager>();
            if (textureManager == null)
            {
                Debug.LogError("MapTextureManager not found!");
                return;
            }

            if (textureManager.DistanceFieldTexture == null)
            {
                Debug.LogError("DistanceFieldTexture is null!");
                return;
            }

            string debugDir = "Assets/Archon-Engine/Debug/Screenshots";
            string path = Path.Combine(debugDir, "distance_field_texture.png");
            SaveRenderTextureToFile(textureManager.DistanceFieldTexture, path);
            AssetDatabase.Refresh();
            Debug.Log($"DistanceFieldTexture saved to {path}");
        }

        [MenuItem("Tools/Save DistanceField Texture to PNG", true)]
        private static bool ValidateSaveDistanceFieldTexture()
        {
            return Application.isPlaying;
        }

        private static void SaveRenderTextureToFile(RenderTexture rt, string path)
        {
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] bytes = tex.EncodeToPNG();
            string fullPath = Path.Combine(Application.dataPath, "..", path);

            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, bytes);
            Object.DestroyImmediate(tex);
        }
#endif
    }
}
