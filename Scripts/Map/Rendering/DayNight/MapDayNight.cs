using Core;
using UnityEngine;

namespace Map.Rendering.DayNight
{
    /// <summary>
    /// ENGINE: Day/night settings, from the "dayNight" section of the visual style data file.
    /// </summary>
    public sealed class MapDayNightStyle
    {
        /// <summary>Starts enabled (players can still toggle it with ToggleKey).</summary>
        public bool Enabled = true;
        public KeyCode ToggleKey = KeyCode.N;

        /// <summary>How much of the night look applies on the dark side (0..1).</summary>
        public float Opacity = 0.85f;
        /// <summary>Night brightness multiplier.</summary>
        public float Darkness = 0.45f;
        /// <summary>Desaturation towards the night grey (0..1).</summary>
        public float Desaturation = 0.8f;
        /// <summary>Tint of the night grey.</summary>
        public Color Tint = new Color(0.2f, 0.7f, 1.2f, 1f);
        /// <summary>Terminator softness (globe dot product units; ~0.06 = soft dusk band).</summary>
        public float Feather = 0.06f;

        /// <summary>Map pixel column of longitude 0 (Greenwich). Negative = map centre.</summary>
        public float GreenwichX = -1f;
        /// <summary>Latitude of the map's south and north edges, fractions of pole to pole (0 = south pole, 1 = north pole).</summary>
        public Vector2 LatitudeRange = new Vector2(0f, 1f);
        /// <summary>Tilt the sun with the date (polar night in winter).</summary>
        public bool Seasons = true;
        /// <summary>Seconds to fade the night in or out when toggled.</summary>
        public float FadeSeconds = 0.4f;
    }

    public static class MapDayNightStyles
    {
        private static MapDayNightStyle current = new MapDayNightStyle();
        public static MapDayNightStyle Current => current;
        public static int Version { get; private set; }

        public static void Set(MapDayNightStyle style)
        {
            current = style ?? new MapDayNightStyle();
            Version++;
        }
    }

    /// <summary>
    /// ENGINE: Drives day/night from Archon time. Publishes the shader globals read by
    /// Shaders/Includes/DayNight.hlsl (map pipeline night stage, border/river lines, atmosphere,
    /// labels). The sun follows the game hour (smoothed between ticks) and, with Seasons, the date.
    ///
    /// Toggle at runtime with Enabled / Toggle() or the style's ToggleKey; the style's Enabled is
    /// the starting state.
    /// </summary>
    public class MapDayNight : MonoBehaviour
    {
        private static readonly int SunId = Shader.PropertyToID("_ArchonSun");
        private static readonly int DayNightId = Shader.PropertyToID("_ArchonDayNight");
        private static readonly int GlobeId = Shader.PropertyToID("_ArchonGlobe");
        private static readonly int TintId = Shader.PropertyToID("_ArchonNightTint");

        private const float SmoothSeconds = 0.35f;  // sun catch-up time between hourly ticks
        private const double SnapHours = 48.0;      // bigger jumps (loads, date changes) snap

        private static MapDayNight instance;

        private GameState gameState;
        private int mapWidth;
        private int appliedStyleVersion = -1;
        private bool enabledState;
        private float opacity;
        private double displayedHours = double.NaN;

        /// <summary>Runtime on/off (fades). Null-safe when no controller exists.</summary>
        public static bool Enabled
        {
            get => instance != null && instance.enabledState;
            set { if (instance != null) instance.enabledState = value; }
        }

        public static void Toggle() => Enabled = !Enabled;

        public void Initialize(MapTextureManager textureManager, GameState state)
        {
            instance = this;
            gameState = state;
            mapWidth = textureManager != null ? Mathf.Max(1, textureManager.MapWidth) : 1;
            ApplyStyle();
            opacity = enabledState ? MapDayNightStyles.Current.Opacity : 0f;
            Publish();
        }

        private void ApplyStyle()
        {
            bool first = appliedStyleVersion < 0;
            var previous = first ? null : lastStyle;
            appliedStyleVersion = MapDayNightStyles.Version;
            var style = MapDayNightStyles.Current;
            // The style sets the starting state; a reload only changes it if the file's value changed
            if (first || previous == null || previous.Enabled != style.Enabled)
                enabledState = style.Enabled;
            lastStyle = style;
        }

        private MapDayNightStyle lastStyle;

        private void Update()
        {
            if (appliedStyleVersion != MapDayNightStyles.Version)
                ApplyStyle();

            var style = MapDayNightStyles.Current;
            if (style.ToggleKey != KeyCode.None && Input.GetKeyDown(style.ToggleKey))
            {
                enabledState = !enabledState;
                ArchonLogger.Log($"MapDayNight: {(enabledState ? "on" : "off")}", "map_rendering");
            }

            float target = enabledState ? Mathf.Clamp01(style.Opacity) : 0f;
            float step = style.FadeSeconds > 0 ? Time.unscaledDeltaTime / style.FadeSeconds * Mathf.Max(Mathf.Clamp01(style.Opacity), 0.01f) : 1f;
            opacity = Mathf.MoveTowards(opacity, target, step);

            Publish();
        }

        private void Publish()
        {
            var style = MapDayNightStyles.Current;
            var time = gameState != null ? gameState.Time : null;

            // Continuous game hours, smoothed so the sun glides between hourly ticks
            double hours = 12.0;
            int dayOfYear = 172; // midsummer if there's no clock
            if (time != null && time.IsInitialized)
            {
                dayOfYear = DayOfYear(time.CurrentMonth, time.CurrentDay);
                hours = (time.CurrentYear * 365.0 + dayOfYear) * 24.0 + time.CurrentHour;
            }
            if (double.IsNaN(displayedHours) || System.Math.Abs(hours - displayedHours) > SnapHours)
                displayedHours = hours;
            else
                displayedHours += (hours - displayedHours) * (1.0 - System.Math.Exp(-Time.unscaledDeltaTime / SmoothSeconds));

            double hourOfDay = ((displayedHours % 24.0) + 24.0) % 24.0;
            float declination = style.Seasons
                ? -23.44f * Mathf.Deg2Rad * Mathf.Cos(2f * Mathf.PI * (dayOfYear + 10) / 365f)
                : 0f;
            // Sun over longitude 0 at noon (see ArchonGlobeNormal: lon = u - greenwich + hour / 24)
            var sun = new Vector3(0f, Mathf.Sin(declination), -Mathf.Cos(declination));

            float greenwich = style.GreenwichX >= 0 ? style.GreenwichX / mapWidth : 0.5f;
            Shader.SetGlobalVector(SunId, new Vector4((float)(hourOfDay / 24.0), sun.x, sun.y, sun.z));
            Shader.SetGlobalVector(DayNightId, new Vector4(opacity, style.Darkness, style.Feather, Mathf.Clamp01(style.Desaturation)));
            Shader.SetGlobalVector(GlobeId, new Vector4(greenwich, style.LatitudeRange.x, style.LatitudeRange.y, 0));
            Shader.SetGlobalColor(TintId, style.Tint);
        }

        private static int DayOfYear(int month, int day)
        {
            int[] start = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };
            return start[Mathf.Clamp(month, 1, 12) - 1] + Mathf.Clamp(day, 1, 31);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                Shader.SetGlobalVector(DayNightId, Vector4.zero); // night off for anything still drawing
            }
        }
    }
}
