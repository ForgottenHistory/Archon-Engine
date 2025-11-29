using UnityEngine;

namespace Map.CameraControllers
{
    /// <summary>
    /// ENGINE LAYER: Orthographic (2D top-down) camera for classic grand strategy look
    /// Extends BaseCameraController with orthographic-specific zoom and positioning
    /// </summary>
    public class OrthographicCameraController : BaseCameraController
    {
        protected override void SetupCamera()
        {
            base.SetupCamera();

            if (mapCamera != null)
            {
                mapCamera.orthographic = true;
                mapCamera.orthographicSize = currentZoom;
            }
        }

        protected override void ApplyZoom(float zoom)
        {
            if (mapCamera != null)
            {
                mapCamera.orthographicSize = zoom;
            }
        }

        protected override float GetCameraViewHeight()
        {
            return mapCamera.orthographicSize * 2f;
        }

        protected override float GetCameraViewWidth()
        {
            return GetCameraViewHeight() * mapCamera.aspect;
        }

        protected override Vector3 GetWorldPositionAtMouse(Vector3 mouseScreenPos)
        {
            return mapCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, mapCamera.nearClipPlane));
        }

        protected override Vector3 GetCameraFocusPoint()
        {
            return mapCamera.transform.position;
        }

        protected override float GetZoomRatio(float newZoom, float oldZoom)
        {
            return newZoom / oldZoom;
        }

        protected override Vector3 ScreenToWorldDelta(Vector3 screenDelta)
        {
            float orthoSize = mapCamera.orthographicSize;
            float screenToWorldRatio = (orthoSize * 2f) / Screen.height;

            return new Vector3(
                -screenDelta.x * screenToWorldRatio * mapCamera.aspect,
                0,
                -screenDelta.y * screenToWorldRatio
            );
        }

        protected override void ClampVerticalPosition()
        {
            float orthoHeight = mapCamera.orthographicSize;
            float maxZ = (actualMapHeight / 2f) - orthoHeight;
            float minZ = -(actualMapHeight / 2f) + orthoHeight;

            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

            Vector3 camPos = mapCamera.transform.position;
            camPos.z = Mathf.Clamp(camPos.z, minZ, maxZ);
            mapCamera.transform.position = camPos;
        }
    }
}
