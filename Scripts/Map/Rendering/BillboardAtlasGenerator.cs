using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Optional utility for generating texture atlases at runtime.
    /// Can generate numeric atlases (0-99) or custom character sets.
    ///
    /// Use Cases:
    /// - Unit count badges (grand strategy games)
    /// - Score displays
    /// - Numeric indicators on map
    /// - Any instanced text rendering
    ///
    /// Architecture:
    /// - Generates texture at runtime (no asset files needed)
    /// - Procedural bitmap font for digits
    /// - Point filtering for crisp text at any distance
    /// - Configurable colors and resolution
    ///
    /// NOTE: This is an optional feature. Games don't need to use this
    /// if they don't need numeric badges or have their own text rendering.
    /// </summary>
    public class BillboardAtlasGenerator : MonoBehaviour
    {
        [Header("Atlas Configuration")]
        [SerializeField] private int atlasResolution = 512; // 512x512 texture
        [SerializeField] private int gridSize = 10; // 10x10 grid for 0-99
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color backgroundColor = Color.clear;

        private Texture2D atlasTexture;

        /// <summary>
        /// Get the generated atlas texture. Generates on first access.
        /// </summary>
        public Texture2D AtlasTexture
        {
            get
            {
                if (atlasTexture == null)
                {
                    GenerateNumericAtlas();
                }
                return atlasTexture;
            }
        }

        /// <summary>
        /// Get atlas texture with explicit generation.
        /// Useful for forcing regeneration with new settings.
        /// </summary>
        public Texture2D GetOrGenerateAtlas(bool forceRegenerate = false)
        {
            if (forceRegenerate && atlasTexture != null)
            {
                Destroy(atlasTexture);
                atlasTexture = null;
            }

            return AtlasTexture;
        }

        private void Awake()
        {
            // Pre-generate atlas on startup
            GenerateNumericAtlas();
        }

        /// <summary>
        /// Generate a 10x10 atlas texture with numbers 0-99.
        /// Uses procedural bitmap font for MVP.
        /// </summary>
        private void GenerateNumericAtlas()
        {
            // Create texture
            atlasTexture = new Texture2D(atlasResolution, atlasResolution, TextureFormat.RGBA32, false);
            atlasTexture.filterMode = FilterMode.Point; // Critical: No interpolation
            atlasTexture.wrapMode = TextureWrapMode.Clamp;
            atlasTexture.name = "NumericAtlas";

            // Fill with background color
            Color[] pixels = new Color[atlasResolution * atlasResolution];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            atlasTexture.SetPixels(pixels);

            // Render each number (0-99) into its grid cell
            int cellSize = atlasResolution / gridSize;

            for (int number = 0; number < gridSize * gridSize; number++)
            {
                int row = number / gridSize;
                int col = number % gridSize;

                // Calculate cell position (top-left corner)
                int cellX = col * cellSize;
                int cellY = (gridSize - 1 - row) * cellSize; // Flip Y (Unity texture coords are bottom-up)

                // Render number into this cell
                RenderNumberToAtlas(number, cellX, cellY, cellSize);
            }

            // Apply changes
            atlasTexture.Apply(false);
        }

        /// <summary>
        /// Render a single number into the atlas at the specified cell position.
        /// Uses simple pixel-based font rendering.
        /// </summary>
        private void RenderNumberToAtlas(int number, int cellX, int cellY, int cellSize)
        {
            // Create temporary texture for this cell
            Texture2D tempTexture = new Texture2D(cellSize, cellSize, TextureFormat.RGBA32, false);

            // Render text pixels
            string numberText = number.ToString();
            RenderTextPixels(tempTexture, numberText, textColor, backgroundColor);

            // Copy from temp texture to atlas
            Color[] cellPixels = tempTexture.GetPixels();
            atlasTexture.SetPixels(cellX, cellY, cellSize, cellSize, cellPixels);

            // Cleanup
            Destroy(tempTexture);
        }

        /// <summary>
        /// Render text using a simple procedural pixel font.
        /// Uses basic 5x7 bitmap font for digits.
        /// </summary>
        private void RenderTextPixels(Texture2D texture, string text, Color textColor, Color bgColor)
        {
            int width = texture.width;
            int height = texture.height;

            // Fill background
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = bgColor;
            }

            // Render each character
            int charWidth = 5;
            int charHeight = 7;
            int spacing = 1;
            int totalWidth = text.Length * (charWidth + spacing) - spacing;

            // Center the text
            int startX = (width - totalWidth) / 2;
            int startY = (height - charHeight) / 2;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int charX = startX + i * (charWidth + spacing);

                // Get bitmap for this character
                bool[,] bitmap = GetCharacterBitmap(c);

                // Draw bitmap
                for (int y = 0; y < charHeight; y++)
                {
                    for (int x = 0; x < charWidth; x++)
                    {
                        if (bitmap[y, x])
                        {
                            int pixelX = charX + x;
                            int pixelY = startY + y;

                            if (pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height)
                            {
                                int index = pixelY * width + pixelX;
                                pixels[index] = textColor;
                            }
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
        }

        /// <summary>
        /// Get a 5x7 bitmap for a digit character (0-9).
        /// Simple procedural font for MVP.
        /// </summary>
        private bool[,] GetCharacterBitmap(char c)
        {
            // 5x7 bitmap font for digits 0-9
            // true = pixel on, false = pixel off

            switch (c)
            {
                case '0':
                    return new bool[,]
                    {
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { true, false, false, true, true },
                        { true, false, true, false, true },
                        { true, true, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false }
                    };

                case '1':
                    return new bool[,]
                    {
                        { false, false, true, false, false },
                        { false, true, true, false, false },
                        { false, false, true, false, false },
                        { false, false, true, false, false },
                        { false, false, true, false, false },
                        { false, false, true, false, false },
                        { false, true, true, true, false }
                    };

                case '2':
                    return new bool[,]
                    {
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { false, false, false, false, true },
                        { false, false, false, true, false },
                        { false, false, true, false, false },
                        { false, true, false, false, false },
                        { true, true, true, true, true }
                    };

                case '3':
                    return new bool[,]
                    {
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { false, false, false, false, true },
                        { false, false, true, true, false },
                        { false, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false }
                    };

                case '4':
                    return new bool[,]
                    {
                        { false, false, false, true, false },
                        { false, false, true, true, false },
                        { false, true, false, true, false },
                        { true, false, false, true, false },
                        { true, true, true, true, true },
                        { false, false, false, true, false },
                        { false, false, false, true, false }
                    };

                case '5':
                    return new bool[,]
                    {
                        { true, true, true, true, true },
                        { true, false, false, false, false },
                        { true, true, true, true, false },
                        { false, false, false, false, true },
                        { false, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false }
                    };

                case '6':
                    return new bool[,]
                    {
                        { false, false, true, true, false },
                        { false, true, false, false, false },
                        { true, false, false, false, false },
                        { true, true, true, true, false },
                        { true, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false }
                    };

                case '7':
                    return new bool[,]
                    {
                        { true, true, true, true, true },
                        { false, false, false, false, true },
                        { false, false, false, true, false },
                        { false, false, true, false, false },
                        { false, true, false, false, false },
                        { false, true, false, false, false },
                        { false, true, false, false, false }
                    };

                case '8':
                    return new bool[,]
                    {
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, false }
                    };

                case '9':
                    return new bool[,]
                    {
                        { false, true, true, true, false },
                        { true, false, false, false, true },
                        { true, false, false, false, true },
                        { false, true, true, true, true },
                        { false, false, false, false, true },
                        { false, false, false, true, false },
                        { false, true, true, false, false }
                    };

                default:
                    // Unknown character, return empty bitmap
                    return new bool[7, 5];
            }
        }

        private void OnDestroy()
        {
            // Clean up texture
            if (atlasTexture != null)
            {
                Destroy(atlasTexture);
            }
        }
    }
}
