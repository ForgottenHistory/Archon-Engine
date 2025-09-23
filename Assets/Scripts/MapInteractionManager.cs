using UnityEngine;
using System.Collections.Generic;
using ProvinceSystem;

public class MapInteractionManager : MonoBehaviour
{
    private MapController mapController;
    private int selectedProvinceId = -1;
    private HashSet<int> highlightedNeighbors = new HashSet<int>();

    public int SelectedProvinceId => selectedProvinceId;

    public void Initialize(MapController controller)
    {
        mapController = controller;
        Debug.Log("MapInteractionManager initialized");
    }

    void Update()
    {
        if (mapController != null && mapController.IsInitialized)
        {
            HandleInput();
        }
    }

    private void HandleInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            DetectProvinceAtMousePosition();
        }

        if (Input.GetKeyDown(KeyCode.N) && selectedProvinceId > 0)
        {
            ShowNeighborsOfProvince(selectedProvinceId);
        }

        if (Input.GetKeyDown(KeyCode.Space) && selectedProvinceId > 0)
        {
            CenterCameraOnProvince(selectedProvinceId);
        }
    }

    private void DetectProvinceAtMousePosition()
    {
        if (mapController.mapCamera == null || mapController.MapTexture == null) return;

        Ray ray = mapController.mapCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            ProvinceComponent provinceComp = hit.collider.GetComponent<ProvinceComponent>();
            if (provinceComp != null)
            {
                selectedProvinceId = provinceComp.provinceId;
                Debug.Log($"Selected Province {selectedProvinceId}: {provinceComp.provinceName}");
                HighlightProvinceAndNeighbors(selectedProvinceId);
                return;
            }

            Vector2 textureCoord = hit.textureCoord;
            if (textureCoord.x >= 0 && textureCoord.x <= 1 && textureCoord.y >= 0 && textureCoord.y <= 1)
            {
                int pixelX = Mathf.FloorToInt(textureCoord.x * mapController.MapTexture.width);
                int pixelY = Mathf.FloorToInt(textureCoord.y * mapController.MapTexture.height);

                pixelX = Mathf.Clamp(pixelX, 0, mapController.MapTexture.width - 1);
                pixelY = Mathf.Clamp(pixelY, 0, mapController.MapTexture.height - 1);

                Color pixelColor = mapController.MapTexture.GetPixel(pixelX, pixelY);

                if (mapController.ColorToProvinceId.ContainsKey(pixelColor))
                {
                    selectedProvinceId = mapController.ColorToProvinceId[pixelColor];
                    Debug.Log($"Selected Province {selectedProvinceId} from map texture at ({pixelX}, {pixelY})");
                    HighlightProvinceAndNeighbors(selectedProvinceId);
                }
                else
                {
                    Debug.Log($"Clicked on unknown color: {pixelColor} at ({pixelX}, {pixelY})");
                }
            }
        }
    }

    private void ShowNeighborsOfProvince(int provinceId)
    {
        if (mapController.adjacencyScanner != null && mapController.adjacencyScanner.IdAdjacencies != null)
        {
            var neighbors = mapController.adjacencyScanner.GetNeighborsForId(provinceId);
            if (neighbors != null && neighbors.Count > 0)
            {
                string neighborList = string.Join(", ", neighbors);
                Debug.Log($"Province {provinceId} has {neighbors.Count} neighbors: {neighborList}");
            }
            else
            {
                Debug.Log($"Province {provinceId} has no neighbors (island or isolated)");
            }
        }
        else
        {
            Debug.LogWarning("Adjacency scanner not available or not initialized");
        }
    }

    private void CenterCameraOnProvince(int provinceId)
    {
        if (mapController.provinceMeshGenerator == null) return;

        ProvinceData provinceData = null;
        foreach (var kvp in mapController.provinceMeshGenerator.GetAllProvinces().Values)
        {
            if (kvp.id == provinceId)
            {
                provinceData = kvp;
                break;
            }
        }

        if (provinceData != null && mapController.cameraController != null)
        {
            Vector3 targetPos = new Vector3(
                provinceData.bounds.center.x,
                mapController.mapCamera.transform.position.y,
                provinceData.bounds.center.z);
            mapController.mapCamera.transform.position = targetPos;
            Debug.Log($"Centered camera on Province {provinceId}");
        }
    }

    private void HighlightProvinceAndNeighbors(int provinceId)
    {
        ClearHighlights();

        ShowNeighborsOfProvince(provinceId);

        if (mapController.ProvinceComponents.ContainsKey(provinceId))
        {
            SetProvinceColor(mapController.ProvinceComponents[provinceId], Color.red);
        }

        if (mapController.adjacencyScanner != null && mapController.adjacencyScanner.IdAdjacencies != null)
        {
            var neighbors = mapController.adjacencyScanner.GetNeighborsForId(provinceId);
            if (neighbors != null)
            {
                foreach (int neighborId in neighbors)
                {
                    if (mapController.ProvinceComponents.ContainsKey(neighborId))
                    {
                        SetProvinceColor(mapController.ProvinceComponents[neighborId], Color.yellow);
                        highlightedNeighbors.Add(neighborId);
                    }
                }
            }
        }
    }

    private void ClearHighlights()
    {
        if (selectedProvinceId > 0 && mapController.ProvinceComponents.ContainsKey(selectedProvinceId))
        {
            RestoreProvinceColor(mapController.ProvinceComponents[selectedProvinceId]);
        }

        foreach (int neighborId in highlightedNeighbors)
        {
            if (mapController.ProvinceComponents.ContainsKey(neighborId))
            {
                RestoreProvinceColor(mapController.ProvinceComponents[neighborId]);
            }
        }
        highlightedNeighbors.Clear();
    }

    private void SetProvinceColor(ProvinceComponent province, Color color)
    {
        var renderer = province.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.color = color;
        }
    }

    private void RestoreProvinceColor(ProvinceComponent province)
    {
        var renderer = province.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.color = province.displayColor;
        }
    }

    public void SelectProvince(int provinceId)
    {
        selectedProvinceId = provinceId;
        HighlightProvinceAndNeighbors(provinceId);
    }

    public void ClearSelection()
    {
        ClearHighlights();
        selectedProvinceId = -1;
    }
}