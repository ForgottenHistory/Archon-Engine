using UnityEngine;
using Unity.Collections;
using ParadoxParser.Bitmap;
using System.IO;

public class SimpleBMPMapViewer : MonoBehaviour
{
    [Header("Map Settings")]
    public string bmpFileName = "provinces.bmp";
    public float mapScale = 10f;

    [Header("Components")]
    public Camera mapCamera;
    public GameObject mapPlane;
    public ParadoxStyleCameraController cameraController;
    public ProvinceMeshGenerator provinceMeshGenerator;

    [Header("3D Province Settings")]
    public bool generate3DProvinces = true;
    public float provinceHeight = 0.1f;
    public Material provinceMaterial;

    private Texture2D mapTexture;
    private Material mapMaterial;

    void Start()
    {
        LoadBMPAndCreatePlane();
        SetupCamera();
        SetupCameraController();

        if (generate3DProvinces)
        {
            Setup3DProvinces();
        }
    }

    void LoadBMPAndCreatePlane()
    {
        string mapDataPath = Path.Combine(Application.dataPath, "Data", "map");
        string bmpFilePath = Path.Combine(mapDataPath, bmpFileName);

        if (!File.Exists(bmpFilePath))
        {
            Debug.LogError($"BMP file not found: {bmpFilePath}");
            return;
        }

        Debug.Log($"Loading BMP file: {bmpFilePath}");

        // Load file data into NativeArray
        var fileBytes = File.ReadAllBytes(bmpFilePath);
        var fileData = new NativeArray<byte>(fileBytes.Length, Allocator.Temp);
        fileData.CopyFrom(fileBytes);

        try
        {
            // Parse BMP header using ParadoxParser
            var bmpHeader = BMPParser.ParseHeader(fileData);

            if (!bmpHeader.IsValid)
            {
                Debug.LogError("Invalid BMP header");
                return;
            }

            Debug.Log($"BMP Info: {bmpHeader.Width}x{bmpHeader.Height}, {bmpHeader.BitsPerPixel}bpp");

            // Get pixel data
            var pixelData = BMPParser.GetPixelData(fileData, bmpHeader);

            if (!pixelData.Success)
            {
                Debug.LogError("Failed to get BMP pixel data");
                return;
            }

            // Convert to Unity Texture2D
            mapTexture = ConvertBMPToTexture2D(pixelData, bmpHeader);

            if (mapTexture != null)
            {
                CreateMapPlane();
                Debug.Log("BMP successfully loaded and applied to plane");
            }
        }
        finally
        {
            fileData.Dispose();
        }
    }

    Texture2D ConvertBMPToTexture2D(BMPParser.BMPPixelData pixelData, BMPParser.BMPHeader header)
    {
        int width = header.Width;
        int height = header.Height;

        // Create Unity Texture2D
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        var pixels = new Color32[width * height];

        // Convert BMP pixels to Unity format
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                {
                    // Fix X mirroring and handle BMP Y orientation properly
                    int unityX = width - 1 - x; // Flip X axis to fix horizontal mirroring
                    int unityY = y; // Keep Y as-is since BMPParser already handles bottom-up conversion
                    int pixelIndex = unityY * width + unityX;
                    pixels[pixelIndex] = new Color32(r, g, b, 255);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        // Set texture properties for crisp map display
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        return texture;
    }

    void CreateMapPlane()
    {
        if (mapPlane == null)
        {
            mapPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            mapPlane.name = "BMPMapPlane";
        }

        // Setup material
        var renderer = mapPlane.GetComponent<Renderer>();
        mapMaterial = new Material(Shader.Find("Unlit/Texture"));
        mapMaterial.mainTexture = mapTexture;
        renderer.material = mapMaterial;

        // Scale plane based on texture aspect ratio
        float aspectRatio = (float)mapTexture.width / (float)mapTexture.height;
        Vector3 scale;

        if (aspectRatio > 1) // Wider than tall
        {
            scale = new Vector3(mapScale * aspectRatio, 1, mapScale);
        }
        else // Taller than wide
        {
            scale = new Vector3(mapScale, 1, mapScale / aspectRatio);
        }

        mapPlane.transform.localScale = scale;
        mapPlane.transform.rotation = Quaternion.Euler(0, 0, 0);
        mapPlane.transform.position = Vector3.zero;

        Debug.Log($"Plane scaled to: {scale}, aspect ratio: {aspectRatio:F2}");
    }

    void SetupCamera()
    {
        if (mapCamera == null)
        {
            mapCamera = Camera.main;
            if (mapCamera == null)
            {
                Debug.LogWarning("No camera found, creating one");
                var cameraGO = new GameObject("Map Camera");
                mapCamera = cameraGO.AddComponent<Camera>();
            }
        }

        // Position camera to view the map from above
        mapCamera.transform.position = new Vector3(0, 50, 0);
        mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

        Debug.Log("Camera positioned for map viewing");
    }

    void SetupCameraController()
    {
        // Get or create ParadoxStyleCameraController
        if (cameraController == null)
        {
            cameraController = mapCamera.GetComponent<ParadoxStyleCameraController>();
        }

        if (cameraController == null)
        {
            cameraController = mapCamera.gameObject.AddComponent<ParadoxStyleCameraController>();
            Debug.Log("Created ParadoxStyleCameraController component");
        }

        // Configure the camera controller
        cameraController.mapPlane = mapPlane;
        cameraController.mapCamera = mapCamera;

        // Initialize the controller (this is required!)
        cameraController.Initialize();

        Debug.Log("ParadoxStyleCameraController configured and initialized");
    }

    void Setup3DProvinces()
    {
        // Get or create ProvinceMeshGenerator
        if (provinceMeshGenerator == null)
        {
            provinceMeshGenerator = GetComponent<ProvinceMeshGenerator>();
        }

        if (provinceMeshGenerator == null)
        {
            provinceMeshGenerator = gameObject.AddComponent<ProvinceMeshGenerator>();
            Debug.Log("Created ProvinceMeshGenerator component");
        }

        // Configure the mesh generator
        provinceMeshGenerator.provinceMap = mapTexture;
        provinceMeshGenerator.mapPlane = mapPlane;
        provinceMeshGenerator.provinceHeight = provinceHeight;
        provinceMeshGenerator.useProvinceMapColors = true;

        // Set up materials if provided
        if (provinceMaterial != null)
        {
            provinceMeshGenerator.provinceMaterial = provinceMaterial;
        }

        // Configure for optimal performance with large maps
        provinceMeshGenerator.meshMethod = ProvinceMeshGenerator.MeshMethod.MergedRectangles;
        provinceMeshGenerator.combineSmallProvinces = true;
        provinceMeshGenerator.minPixelsForProvince = 10;
        provinceMeshGenerator.generateBorders = false; // Disable borders for better performance

        // Generate the 3D provinces
        provinceMeshGenerator.GenerateProvinces();

        Debug.Log("3D province generation completed!");
    }

    void OnDestroy()
    {
        if (mapTexture != null)
        {
            DestroyImmediate(mapTexture);
        }
        if (mapMaterial != null)
        {
            DestroyImmediate(mapMaterial);
        }
    }
}