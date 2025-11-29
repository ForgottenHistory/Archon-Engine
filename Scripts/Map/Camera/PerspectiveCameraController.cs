using UnityEngine;

namespace Map.CameraControllers
{
    /// <summary>
    /// ENGINE LAYER: Perspective (3D) camera for viewing tessellated terrain
    /// Extends BaseCameraController with perspective-specific zoom, rotation, and positioning
    /// Provides Total War / Civ-style camera with distance + angle + rotation
    /// </summary>
    public class PerspectiveCameraController : BaseCameraController
    {
        [Header("Perspective Settings")]
        [Tooltip("Zoom threshold - above this value, camera looks straight down (90 degrees)")]
        public float straightDownThreshold = 3.0f;

        [Tooltip("Pitch angle when fully zoomed in (at minZoom, typical: 10-45)")]
        [Range(0f, 60f)]
        public float maxZoomedInPitchAngle = 30f;

        [Tooltip("Camera distance from ground at max zoom out")]
        public float maxDistance = 100f;

        [Tooltip("Camera distance from ground at max zoom in")]
        public float minDistance = 10f;

        // State
        private float currentDistance;
        private float currentPitchAngle; // Calculated from zoom level

        protected override void SetupCamera()
        {
            base.SetupCamera();

            if (mapCamera != null)
            {
                mapCamera.orthographic = false;

                // Initialize distance based on zoom
                currentDistance = Mathf.Lerp(minDistance, maxDistance, (currentZoom - minZoom) / (maxZoom - minZoom));
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            // For perspective camera, targetPosition is the ground focus point (Y=0)
            // Camera height comes from distance calculation in UpdateCameraTransform
            targetPosition.y = 0f;

            // Position camera at initial distance and angle
            UpdateCameraTransform();
        }

        protected override void ApplySmoothMovement()
        {
            base.ApplySmoothMovement();

            // Calculate pitch based on zoom level
            CalculatePitchFromZoom();

            // Update camera transform (position and rotation)
            UpdateCameraTransform();
        }

        void CalculatePitchFromZoom()
        {
            // Camera X rotation starts at 90 (straight down) and decreases as we zoom in
            // Final camera X rotation = 90 - maxZoomedInPitchAngle
            // Example: 90 - 15 = 75 when fully zoomed in

            if (currentZoom >= straightDownThreshold)
            {
                // Zoomed out past threshold - straight down
                currentPitchAngle = 90f;
            }
            else
            {
                // Between minZoom and threshold - interpolate from (90 - maxZoomedInPitchAngle) to 90
                float t = (currentZoom - minZoom) / (straightDownThreshold - minZoom);
                float minAngle = 90f - maxZoomedInPitchAngle;  // e.g., 90 - 15 = 75
                currentPitchAngle = Mathf.Lerp(minAngle, 90f, t);
            }

            currentPitchAngle = Mathf.Clamp(currentPitchAngle, 0f, 90f);
        }

        void UpdateCameraTransform()
        {
            if (mapCamera == null) return;

            // Camera X rotation is directly currentPitchAngle
            // Ranges from 90 (straight down) to (90 - maxZoomedInPitchAngle) when zoomed in

            Vector3 focusPoint = targetPosition;

            float cameraXRotation = currentPitchAngle;

            // Convert to radians for position calculation
            float angleRad = cameraXRotation * Mathf.Deg2Rad;

            // Position camera: height and distance based on angle
            // At 90: directly above (max height, no Z offset)
            // At lower angles: further back in Z, lower in Y
            Vector3 offset = new Vector3(
                0f,
                Mathf.Sin(angleRad) * currentDistance,  // Height
                -Mathf.Cos(angleRad) * currentDistance  // Distance back (negative = south)
            );

            mapCamera.transform.position = focusPoint + offset;
            mapCamera.transform.rotation = Quaternion.Euler(cameraXRotation, 0f, 0f);

            #if UNITY_EDITOR
            // Debug: Log current pitch angle
            if (Input.GetKeyDown(KeyCode.P))
            {
                ArchonLogger.Log($"Pitch Angle: {currentPitchAngle:F1}, Camera X Rot: {cameraXRotation:F1}, Zoom: {currentZoom:F2}, Distance: {currentDistance:F1}", "map_rendering");
            }
            #endif
        }

        protected override void ApplyZoom(float zoom)
        {
            // Map zoom value (minZoom-maxZoom) to distance (minDistance-maxDistance)
            // Higher zoom value = further away (zoomed out)
            float t = (zoom - minZoom) / (maxZoom - minZoom);
            currentDistance = Mathf.Lerp(minDistance, maxDistance, t);
        }

        protected override float GetCameraViewHeight()
        {
            // Calculate view height at ground plane based on distance and FOV
            float halfFOV = mapCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float viewHeight = 2f * currentDistance * Mathf.Tan(halfFOV);
            return viewHeight;
        }

        protected override float GetCameraViewWidth()
        {
            return GetCameraViewHeight() * mapCamera.aspect;
        }

        protected override Vector3 GetWorldPositionAtMouse(Vector3 mouseScreenPos)
        {
            // Raycast from camera to ground plane (Y = 0)
            Ray ray = mapCamera.ScreenPointToRay(mouseScreenPos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            return mapCamera.transform.position; // Fallback
        }

        protected override Vector3 GetCameraFocusPoint()
        {
            // For perspective, focus point is the target position on the ground
            return targetPosition;
        }

        protected override float GetZoomRatio(float newZoom, float oldZoom)
        {
            // Calculate distance ratio for perspective zoom
            float newDistance = Mathf.Lerp(minDistance, maxDistance, (newZoom - minZoom) / (maxZoom - minZoom));
            float oldDistance = Mathf.Lerp(minDistance, maxDistance, (oldZoom - minZoom) / (maxZoom - minZoom));
            return newDistance / oldDistance;
        }

        protected override Vector3 ScreenToWorldDelta(Vector3 screenDelta)
        {
            // Convert screen delta to world delta at ground plane
            Vector3 screenPos = Input.mousePosition;
            Vector3 worldPosBefore = GetWorldPositionAtMouse(screenPos);
            Vector3 worldPosAfter = GetWorldPositionAtMouse(screenPos + screenDelta);

            return worldPosBefore - worldPosAfter;
        }

        protected override void ClampVerticalPosition()
        {
            // Calculate visible height at ground plane
            float viewHeight = GetCameraViewHeight();
            float maxZ = (actualMapHeight / 2f) - (viewHeight / 2f);
            float minZ = -(actualMapHeight / 2f) + (viewHeight / 2f);

            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

            // Don't clamp actual camera position - it's derived from targetPosition
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Debug gizmos for camera focus point
        /// </summary>
        void OnDrawGizmos()
        {
            if (mapCamera == null || !isInitialized) return;

            // Draw focus point
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetPosition, 1f);

            // Draw camera view frustum
            Gizmos.color = Color.cyan;
            Vector3 camPos = mapCamera.transform.position;
            Gizmos.DrawLine(camPos, targetPosition);
        }
        #endif
    }
}
