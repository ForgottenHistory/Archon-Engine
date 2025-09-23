using UnityEngine;
using Unity.Collections;
using System.IO;
using System.Collections.Generic;
using ParadoxParser.CSV;
using ParadoxParser.Utilities;

[System.Serializable]
public class ProvinceDefinition
{
    public int id;
    public int red;
    public int green;
    public int blue;
    public string name;
    public Color color;
    public int packedRGB;
}

public class ProvinceDefinitionLoader : MonoBehaviour
{
    [Header("Definition Settings")]
    public string definitionFile = "definition.csv";

    [Header("Debug")]
    public bool showDebugInfo = false;

    public Dictionary<int, ProvinceDefinition> ProvinceByID { get; private set; }
    public Dictionary<int, ProvinceDefinition> ProvinceByPackedRGB { get; private set; }
    public bool IsLoaded { get; private set; }

    public bool LoadDefinition()
    {
        string mapDataPath = Path.Combine(Application.dataPath, "Data", "map");
        string csvFilePath = Path.Combine(mapDataPath, definitionFile);

        if (!File.Exists(csvFilePath))
        {
            Debug.LogError($"Definition file not found: {csvFilePath}");
            return false;
        }

        if (showDebugInfo)
            Debug.Log($"Loading province definition: {csvFilePath}");

        try
        {
            byte[] fileBytes = File.ReadAllBytes(csvFilePath);
            var fileData = new NativeArray<byte>(fileBytes, Allocator.Temp);

            try
            {
                return ParseDefinition(fileData);
            }
            finally
            {
                fileData.Dispose();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to read definition file: {e.Message}");
            return false;
        }
    }

    private bool ParseDefinition(NativeArray<byte> csvData)
    {
        var csvResult = CSVParser.Parse(csvData, Allocator.Temp, hasHeader: true);
        if (!csvResult.Success)
        {
            Debug.LogError("Failed to parse definition CSV");
            return false;
        }

        try
        {
            ProvinceByID = new Dictionary<int, ProvinceDefinition>();
            ProvinceByPackedRGB = new Dictionary<int, ProvinceDefinition>();

            // Find column indices
            var provinceHash = FastHasher.HashFNV1a32("province");
            var redHash = FastHasher.HashFNV1a32("red");
            var greenHash = FastHasher.HashFNV1a32("green");
            var blueHash = FastHasher.HashFNV1a32("blue");
            var nameHash = FastHasher.HashFNV1a32("name");

            int provinceCol = CSVParser.FindColumnIndex(csvResult.HeaderHashes, provinceHash);
            int redCol = CSVParser.FindColumnIndex(csvResult.HeaderHashes, redHash);
            int greenCol = CSVParser.FindColumnIndex(csvResult.HeaderHashes, greenHash);
            int blueCol = CSVParser.FindColumnIndex(csvResult.HeaderHashes, blueHash);
            int nameCol = CSVParser.FindColumnIndex(csvResult.HeaderHashes, nameHash);

            if (provinceCol < 0 || redCol < 0 || greenCol < 0 || blueCol < 0)
            {
                Debug.LogError("Missing required columns in definition CSV (province, red, green, blue)");
                return false;
            }

            // Process each row
            int processedCount = 0;
            for (int i = 0; i < csvResult.RowCount; i++)
            {
                var row = csvResult.Rows[i];

                if (CSVParser.TryGetInt(row, provinceCol, out int provinceID) &&
                    CSVParser.TryGetInt(row, redCol, out int red) &&
                    CSVParser.TryGetInt(row, greenCol, out int green) &&
                    CSVParser.TryGetInt(row, blueCol, out int blue))
                {
                    // Validate RGB ranges
                    if (red >= 0 && red <= 255 && green >= 0 && green <= 255 && blue >= 0 && blue <= 255)
                    {
                        string name = "Unknown";
                        if (nameCol >= 0)
                        {
                            var nameField = CSVParser.GetField(row, nameCol);
                            if (nameField.Length > 0)
                            {
                                name = System.Text.Encoding.UTF8.GetString(nameField.ToArray());
                            }
                        }

                        var provinceDef = new ProvinceDefinition
                        {
                            id = provinceID,
                            red = red,
                            green = green,
                            blue = blue,
                            name = name ?? $"Province_{provinceID}",
                            color = new Color(red / 255f, green / 255f, blue / 255f, 1f),
                            packedRGB = (red << 16) | (green << 8) | blue
                        };

                        ProvinceByID[provinceID] = provinceDef;
                        ProvinceByPackedRGB[provinceDef.packedRGB] = provinceDef;
                        processedCount++;
                    }
                }
            }

            IsLoaded = true;

            if (showDebugInfo)
            {
                Debug.Log($"Loaded {processedCount} province definitions from definition.csv");
            }

            return true;
        }
        finally
        {
            csvResult.Dispose();
        }
    }

    public ProvinceDefinition GetProvinceByID(int id)
    {
        return ProvinceByID?.ContainsKey(id) == true ? ProvinceByID[id] : null;
    }

    public ProvinceDefinition GetProvinceByColor(Color color)
    {
        int packedRGB = ColorToPackedRGB(color);
        return ProvinceByPackedRGB?.ContainsKey(packedRGB) == true ? ProvinceByPackedRGB[packedRGB] : null;
    }

    public ProvinceDefinition GetProvinceByRGB(int red, int green, int blue)
    {
        int packedRGB = (red << 16) | (green << 8) | blue;
        return ProvinceByPackedRGB?.ContainsKey(packedRGB) == true ? ProvinceByPackedRGB[packedRGB] : null;
    }

    public static int ColorToPackedRGB(Color color)
    {
        int r = Mathf.RoundToInt(color.r * 255f);
        int g = Mathf.RoundToInt(color.g * 255f);
        int b = Mathf.RoundToInt(color.b * 255f);
        return (r << 16) | (g << 8) | b;
    }

    public static Color PackedRGBToColor(int packedRGB)
    {
        int r = (packedRGB >> 16) & 0xFF;
        int g = (packedRGB >> 8) & 0xFF;
        int b = packedRGB & 0xFF;
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    [ContextMenu("Load Definition")]
    public void LoadDefinitionManual()
    {
        LoadDefinition();
    }

    [ContextMenu("Log Province Statistics")]
    public void LogStatistics()
    {
        if (!IsLoaded)
        {
            Debug.Log("No definition loaded");
            return;
        }

        Debug.Log($"Province Definition Statistics:");
        Debug.Log($"- Total Provinces: {ProvinceByID.Count}");

        if (ProvinceByID.Count > 0)
        {
            int minID = int.MaxValue;
            int maxID = int.MinValue;

            foreach (int id in ProvinceByID.Keys)
            {
                minID = Mathf.Min(minID, id);
                maxID = Mathf.Max(maxID, id);
            }

            Debug.Log($"- ID Range: {minID} to {maxID}");
        }
    }
}