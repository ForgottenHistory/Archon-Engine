using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

namespace Map.Rendering
{
    public static class BorderTextureDebug
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Save BorderTexture to PNG")]
        public static void SaveBorderTexture()
        {
            var textureManager = Object.FindObjectOfType<MapTextureManager>();
            if (textureManager == null)
            {
                Debug.LogError("MapTextureManager not found!");
                return;
            }

            if (textureManager.BorderTexture == null)
            {
                Debug.LogError("BorderTexture is null!");
                return;
            }

            string path = "Assets/Game/Debug/Screenshots/border_texture.png";
            SaveRenderTextureToFile(textureManager.BorderTexture, path);
            AssetDatabase.Refresh();
            Debug.Log($"BorderTexture saved to {path}");
        }

        [MenuItem("Tools/Save BorderTexture to PNG", true)]
        private static bool ValidateSaveBorderTexture()
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
