using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Specialized parser for RGB color values in Paradox files
    /// Handles formats like "rgb { 255 128 64 }" and "color = { 1.0 0.5 0.25 }"
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class ColorParser
    {
        /// <summary>
        /// Parsed color result
        /// </summary>
        public struct ColorResult
        {
            public bool Success;
            public ColorFormat Format;
            public float Red;
            public float Green;
            public float Blue;
            public float Alpha;
            public int BytesConsumed;

            public static ColorResult Failed => new ColorResult { Success = false };

            public static ColorResult RGB(float r, float g, float b, int bytesConsumed, ColorFormat format = ColorFormat.RGB)
            {
                return new ColorResult
                {
                    Success = true,
                    Format = format,
                    Red = r,
                    Green = g,
                    Blue = b,
                    Alpha = 1.0f,
                    BytesConsumed = bytesConsumed
                };
            }

            public static ColorResult RGBA(float r, float g, float b, float a, int bytesConsumed, ColorFormat format = ColorFormat.RGBA)
            {
                return new ColorResult
                {
                    Success = true,
                    Format = format,
                    Red = r,
                    Green = g,
                    Blue = b,
                    Alpha = a,
                    BytesConsumed = bytesConsumed
                };
            }

            /// <summary>
            /// Convert to Unity Color32 (0-255 values)
            /// </summary>
            public Color32 ToColor32()
            {
                return new Color32(
                    (byte)(Red * 255f),
                    (byte)(Green * 255f),
                    (byte)(Blue * 255f),
                    (byte)(Alpha * 255f)
                );
            }

            /// <summary>
            /// Convert to normalized values (0-1 range)
            /// </summary>
            public (float r, float g, float b, float a) ToNormalized()
            {
                if (Format == ColorFormat.RGB255 || Format == ColorFormat.RGBA255)
                {
                    return (Red / 255f, Green / 255f, Blue / 255f, Alpha / 255f);
                }
                return (Red, Green, Blue, Alpha);
            }
        }

        /// <summary>
        /// Color representation for interoperability
        /// </summary>
        public struct Color32
        {
            public byte R, G, B, A;

            public Color32(byte r, byte g, byte b, byte a = 255)
            {
                R = r; G = g; B = b; A = a;
            }
        }

        /// <summary>
        /// Color formats supported by Paradox files
        /// </summary>
        public enum ColorFormat : byte
        {
            RGB = 0,        // rgb { 1.0 0.5 0.25 } (normalized)
            RGBA,           // rgba { 1.0 0.5 0.25 0.8 } (normalized)
            RGB255,         // rgb { 255 128 64 } (0-255 values)
            RGBA255,        // rgba { 255 128 64 128 } (0-255 values)
            HSV,            // hsv { 0.5 1.0 0.8 } (hue, saturation, value)
            Named,          // red, blue, etc.
            Hex             // #FF8040 or 0xFF8040
        }

        /// <summary>
        /// Parse color from tokens starting at a given index
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseColor(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return ColorResult.Failed;

            var token = tokens[startIndex];

            // Check for different color formats
            switch (token.Type)
            {
                case TokenType.Identifier:
                    return ParseNamedOrHexColor(token, sourceData);

                case TokenType.LeftBrace:
                    return ParseRGBBlock(tokens, startIndex, sourceData);

                case TokenType.Number:
                    return ParseInlineRGB(tokens, startIndex, sourceData);

                default:
                    return ColorResult.Failed;
            }
        }

        /// <summary>
        /// Parse "rgb { r g b }" or "rgba { r g b a }" format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseRGBBlock(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex + 5 >= tokens.Length) // Need at least: { r g b }
                return ColorResult.Failed;

            // Expect opening brace
            if (tokens[startIndex].Type != TokenType.LeftBrace)
                return ColorResult.Failed;

            int tokenIndex = startIndex + 1;
            var colorValues = new NativeList<float>(4, Allocator.Temp);

            try
            {
                // Parse color component values
                while (tokenIndex < tokens.Length && colorValues.Length < 4)
                {
                    var token = tokens[tokenIndex];

                    switch (token.Type)
                    {
                        case TokenType.RightBrace:
                            // End of color block
                            if (colorValues.Length >= 3)
                            {
                                return CreateColorResult(colorValues, tokenIndex - startIndex + 1);
                            }
                            return ColorResult.Failed;

                        case TokenType.Number:
                            if (!ParseColorComponent(token, sourceData, out var value))
                                return ColorResult.Failed;
                            colorValues.Add(value);
                            tokenIndex++;
                            break;

                        case TokenType.Whitespace:
                        case TokenType.Newline:
                            tokenIndex++;
                            break;

                        default:
                            return ColorResult.Failed;
                    }
                }

                return ColorResult.Failed;
            }
            finally
            {
                colorValues.Dispose();
            }
        }

        /// <summary>
        /// Parse inline RGB values (space-separated numbers)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseInlineRGB(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex + 2 >= tokens.Length)
                return ColorResult.Failed;

            var colorValues = new NativeList<float>(4, Allocator.Temp);

            try
            {
                int tokenIndex = startIndex;

                // Parse up to 4 consecutive numbers
                while (tokenIndex < tokens.Length && colorValues.Length < 4)
                {
                    var token = tokens[tokenIndex];

                    if (token.Type == TokenType.Number)
                    {
                        if (!ParseColorComponent(token, sourceData, out var value))
                            return ColorResult.Failed;
                        colorValues.Add(value);
                        tokenIndex++;
                    }
                    else if (token.Type == TokenType.Whitespace)
                    {
                        tokenIndex++;
                    }
                    else
                    {
                        break; // End of color sequence
                    }
                }

                if (colorValues.Length >= 3)
                {
                    return CreateColorResult(colorValues, tokenIndex - startIndex);
                }

                return ColorResult.Failed;
            }
            finally
            {
                colorValues.Dispose();
            }
        }

        /// <summary>
        /// Parse named color or hex color
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseNamedOrHexColor(Token token, NativeSlice<byte> sourceData)
        {
            var tokenData = sourceData.Slice(token.StartPosition, token.Length);

            // Check for hex format
            if (tokenData.Length > 0 && tokenData[0] == (byte)'#')
            {
                return ParseHexColor(tokenData);
            }

            // Check for 0x hex format
            if (tokenData.Length > 2 && tokenData[0] == (byte)'0' && tokenData[1] == (byte)'x')
            {
                return ParseHexColor(tokenData);
            }

            // Check for named colors
            return ParseNamedColor(tokenData);
        }

        /// <summary>
        /// Parse hexadecimal color (#RGB, #RRGGBB, #RRGGBBAA)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseHexColor(NativeSlice<byte> data)
        {
            int startOffset = data[0] == (byte)'#' ? 1 : 2; // Skip # or 0x
            int hexLength = data.Length - startOffset;

            if (hexLength != 3 && hexLength != 6 && hexLength != 8)
                return ColorResult.Failed;

            var hexResult = FastNumberParser.ParseHex(data.Slice(startOffset));
            if (!hexResult.Success)
                return ColorResult.Failed;

            uint colorValue = hexResult.Value;

            switch (hexLength)
            {
                case 3: // #RGB -> #RRGGBB
                    {
                        uint r = (colorValue >> 8) & 0xF;
                        uint g = (colorValue >> 4) & 0xF;
                        uint b = colorValue & 0xF;
                        return ColorResult.RGB(
                            (r * 17) / 255f,
                            (g * 17) / 255f,
                            (b * 17) / 255f,
                            data.Length, ColorFormat.Hex);
                    }

                case 6: // #RRGGBB
                    {
                        uint r = (colorValue >> 16) & 0xFF;
                        uint g = (colorValue >> 8) & 0xFF;
                        uint b = colorValue & 0xFF;
                        return ColorResult.RGB(
                            r / 255f,
                            g / 255f,
                            b / 255f,
                            data.Length, ColorFormat.Hex);
                    }

                case 8: // #RRGGBBAA
                    {
                        uint r = (colorValue >> 24) & 0xFF;
                        uint g = (colorValue >> 16) & 0xFF;
                        uint b = (colorValue >> 8) & 0xFF;
                        uint a = colorValue & 0xFF;
                        return ColorResult.RGBA(
                            r / 255f,
                            g / 255f,
                            b / 255f,
                            a / 255f,
                            data.Length, ColorFormat.Hex);
                    }

                default:
                    return ColorResult.Failed;
            }
        }

        /// <summary>
        /// Parse named color keywords
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorResult ParseNamedColor(NativeSlice<byte> data)
        {
            // Common Paradox named colors
            if (IsColorName(data, "red"))
                return ColorResult.RGB(1.0f, 0.0f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "green"))
                return ColorResult.RGB(0.0f, 1.0f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "blue"))
                return ColorResult.RGB(0.0f, 0.0f, 1.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "white"))
                return ColorResult.RGB(1.0f, 1.0f, 1.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "black"))
                return ColorResult.RGB(0.0f, 0.0f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "yellow"))
                return ColorResult.RGB(1.0f, 1.0f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "cyan"))
                return ColorResult.RGB(0.0f, 1.0f, 1.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "magenta"))
                return ColorResult.RGB(1.0f, 0.0f, 1.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "orange"))
                return ColorResult.RGB(1.0f, 0.5f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "purple"))
                return ColorResult.RGB(0.5f, 0.0f, 0.5f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "brown"))
                return ColorResult.RGB(0.6f, 0.3f, 0.0f, data.Length, ColorFormat.Named);
            if (IsColorName(data, "grey") || IsColorName(data, "gray"))
                return ColorResult.RGB(0.5f, 0.5f, 0.5f, data.Length, ColorFormat.Named);

            return ColorResult.Failed;
        }

        /// <summary>
        /// Parse a single color component (0-1 or 0-255)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseColorComponent(Token token, NativeSlice<byte> sourceData, out float value)
        {
            value = 0f;

            var tokenData = sourceData.Slice(token.StartPosition, token.Length);
            var parseResult = FastNumberParser.ParseFloat(tokenData);

            if (!parseResult.Success)
                return false;

            value = parseResult.Value;

            // Auto-detect format based on value range
            if (value > 1.0f && value <= 255.0f)
            {
                // Assume 0-255 format, normalize to 0-1
                value /= 255.0f;
            }

            // Clamp to valid range
            if (value < 0.0f) value = 0.0f;
            if (value > 1.0f) value = 1.0f;

            return true;
        }

        /// <summary>
        /// Create color result from parsed values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ColorResult CreateColorResult(NativeList<float> values, int bytesConsumed)
        {
            if (values.Length < 3)
                return ColorResult.Failed;

            // Determine format based on value ranges
            bool is255Format = false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 1.0f)
                {
                    is255Format = true;
                    break;
                }
            }

            var format = values.Length == 4 ?
                (is255Format ? ColorFormat.RGBA255 : ColorFormat.RGBA) :
                (is255Format ? ColorFormat.RGB255 : ColorFormat.RGB);

            if (values.Length == 4)
            {
                return ColorResult.RGBA(values[0], values[1], values[2], values[3], bytesConsumed, format);
            }
            else
            {
                return ColorResult.RGB(values[0], values[1], values[2], bytesConsumed, format);
            }
        }

        /// <summary>
        /// Check if data matches a color name
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsColorName(NativeSlice<byte> data, string colorName)
        {
            if (data.Length != colorName.Length)
                return false;

            for (int i = 0; i < colorName.Length; i++)
            {
                // Case-insensitive comparison
                byte dataByte = data[i];
                byte nameByte = (byte)colorName[i];

                if (dataByte >= (byte)'A' && dataByte <= (byte)'Z')
                    dataByte += 32; // Convert to lowercase

                if (nameByte >= (byte)'A' && nameByte <= (byte)'Z')
                    nameByte += 32; // Convert to lowercase

                if (dataByte != nameByte)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Validate that a color is within acceptable ranges
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidColor(ColorResult color)
        {
            if (!color.Success)
                return false;

            return color.Red >= 0.0f && color.Red <= 1.0f &&
                   color.Green >= 0.0f && color.Green <= 1.0f &&
                   color.Blue >= 0.0f && color.Blue <= 1.0f &&
                   color.Alpha >= 0.0f && color.Alpha <= 1.0f;
        }

        /// <summary>
        /// Convert color to string representation
        /// </summary>
        public static string ColorToString(ColorResult color)
        {
            if (!color.Success)
                return "invalid";

            return color.Format switch
            {
                ColorFormat.RGB => $"rgb({color.Red:F3}, {color.Green:F3}, {color.Blue:F3})",
                ColorFormat.RGBA => $"rgba({color.Red:F3}, {color.Green:F3}, {color.Blue:F3}, {color.Alpha:F3})",
                ColorFormat.RGB255 => $"rgb({(int)(color.Red * 255)}, {(int)(color.Green * 255)}, {(int)(color.Blue * 255)})",
                ColorFormat.RGBA255 => $"rgba({(int)(color.Red * 255)}, {(int)(color.Green * 255)}, {(int)(color.Blue * 255)}, {(int)(color.Alpha * 255)})",
                ColorFormat.Hex => $"#{(int)(color.Red * 255):X2}{(int)(color.Green * 255):X2}{(int)(color.Blue * 255):X2}",
                ColorFormat.Named => "named_color",
                _ => "unknown"
            };
        }
    }
}