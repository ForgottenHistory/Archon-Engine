namespace Map.Rendering
{
    /// <summary>
    /// Border mode - what borders to show
    /// </summary>
    public enum BorderMode
    {
        Province,      // Show all province borders
        Country,       // Show only country/owner borders
        Thick,         // Show thick province borders
        Dual,          // Show BOTH country AND province borders (recommended)
        None           // No borders
    }

    /// <summary>
    /// Border rendering mode - how to render borders
    /// </summary>
    public enum BorderRenderingMode
    {
        None,                   // No border rendering at all
        ShaderDistanceField,    // Shader-based using JFA distance field (smooth, 3D tessellation compatible)
        MeshGeometry,           // CPU triangle strip geometry (resolution-independent, runtime style updates)
        ShaderPixelPerfect,     // Shader-based using 1-pixel BorderMask (retro aesthetic, planned)

        // Legacy/deprecated modes (kept for backwards compatibility)
        [System.Obsolete("Use ShaderDistanceField instead")]
        SDF = ShaderDistanceField,
        [System.Obsolete("Use ShaderDistanceField instead")]
        DistanceField = ShaderDistanceField,
        [System.Obsolete("Use ShaderPixelPerfect instead")]
        Rasterization = ShaderPixelPerfect,
        [System.Obsolete("Use MeshGeometry instead")]
        Mesh = MeshGeometry
    }
}
