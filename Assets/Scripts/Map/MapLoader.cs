using UnityEngine;
using Unity.Collections;
using ParadoxParser.Bitmap;
using System.IO;
using System.Collections;

public class MapLoader : MonoBehaviour
{
    private MapController mapController;
    private MapSettings settings;

    public Texture2D MapTexture { get; private set; }
    public Material MapMaterial { get; private set; }

    public void Initialize(MapController controller, MapSettings mapSettings)
    {
        mapController = controller;
        settings = mapSettings;
    }

    public IEnumerator LoadMapTexture()
    {
        string mapDataPath = Path.Combine(Application.dataPath, "Data", "map");
        string bmpFilePath = Path.Combine(mapDataPath, settings.bmpFileName);

        if (!File.Exists(bmpFilePath))
        {
            Debug.LogError($"BMP file not found: {bmpFilePath}");
            yield break;
        }

        Debug.Log($"Loading BMP file: {bmpFilePath}");

        var fileBytes = File.ReadAllBytes(bmpFilePath);
        var fileData = new NativeArray<byte>(fileBytes.Length, Allocator.Temp);
        fileData.CopyFrom(fileBytes);

        try
        {
            var bmpHeader = BMPParser.ParseHeader(fileData);

            if (!bmpHeader.IsValid)
            {
                Debug.LogError("Invalid BMP header");
                yield break;
            }

            Debug.Log($"BMP Info: {bmpHeader.Width}x{bmpHeader.Height}, {bmpHeader.BitsPerPixel}bpp");

            var pixelData = BMPParser.GetPixelData(fileData, bmpHeader);

            if (!pixelData.Success)
            {
                Debug.LogError("Failed to get BMP pixel data");
                yield break;
            }

            MapTexture = ConvertBMPToTexture2D(pixelData, bmpHeader);

            if (MapTexture != null)
            {
                CreateMapPlane();
                Debug.Log("BMP successfully loaded and applied to plane");
            }
        }
        finally
        {
            fileData.Dispose();
        }

        yield return null;
    }

    private Texture2D ConvertBMPToTexture2D(BMPParser.BMPPixelData pixelData, BMPParser.BMPHeader header)
    {
        int width = header.Width;
        int height = header.Height;

        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                {
                    int unityX = width - 1 - x;
                    int unityY = y;
                    int pixelIndex = unityY * width + unityX;
                    pixels[pixelIndex] = new Color32(r, g, b, 255);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        return texture;
    }

    private void CreateMapPlane()
    {
        if (mapController.mapPlane == null)
        {
            mapController.mapPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            mapController.mapPlane.name = "BMPMapPlane";
        }

        var renderer = mapController.mapPlane.GetComponent<Renderer>();
        MapMaterial = new Material(Shader.Find("Unlit/Texture"));
        MapMaterial.mainTexture = MapTexture;
        renderer.material = MapMaterial;

        float aspectRatio = (float)MapTexture.width / (float)MapTexture.height;
        Vector3 scale;

        if (aspectRatio > 1)
        {
            scale = new Vector3(settings.mapScale * aspectRatio, 1, settings.mapScale);
        }
        else
        {
            scale = new Vector3(settings.mapScale, 1, settings.mapScale / aspectRatio);
        }

        mapController.mapPlane.transform.localScale = scale;
        mapController.mapPlane.transform.rotation = Quaternion.Euler(0, 0, 0);
        mapController.mapPlane.transform.position = Vector3.zero;

        Debug.Log($"Plane scaled to: {scale}, aspect ratio: {aspectRatio:F2}");
    }

    void OnDestroy()
    {
        if (MapTexture != null)
        {
            DestroyImmediate(MapTexture);
        }
        if (MapMaterial != null)
        {
            DestroyImmediate(MapMaterial);
        }
    }
}