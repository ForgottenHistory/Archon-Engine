using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Core.Localization
{
    /// <summary>
    /// Colored text markup system for rich text localization
    /// Supports Paradox-style color codes and Unity rich text conversion
    /// </summary>
    public static class ColoredTextMarkup
    {
        /// <summary>
        /// Text segment with color information
        /// </summary>
        public struct ColoredTextSegment
        {
            public FixedString128Bytes Text;
            public Color32 Color;
            public bool HasColor;
            public TextStyle Style;
        }

        /// <summary>
        /// Text styling options
        /// </summary>
        [Flags]
        public enum TextStyle : byte
        {
            None = 0,
            Bold = 1,
            Italic = 2,
            Underline = 4,
            Strikethrough = 8
        }

        /// <summary>
        /// Result of markup processing
        /// </summary>
        public struct MarkupResult
        {
            public NativeList<ColoredTextSegment> Segments;
            public FixedString512Bytes UnityRichText;
            public FixedString512Bytes PlainText;
            public bool IsSuccess;

            public void Dispose()
            {
                if (Segments.IsCreated)
                    Segments.Dispose();
            }
        }

        /// <summary>
        /// Predefined Paradox color palette
        /// </summary>
        private static readonly Color32[] ParadoxColors = new Color32[]
        {
            new Color32(255, 255, 255, 255), // white (default)
            new Color32(255, 0, 0, 255),     // red
            new Color32(0, 255, 0, 255),     // green
            new Color32(0, 0, 255, 255),     // blue
            new Color32(255, 255, 0, 255),   // yellow
            new Color32(255, 0, 255, 255),   // magenta
            new Color32(0, 255, 255, 255),   // cyan
            new Color32(128, 128, 128, 255), // gray
            new Color32(255, 165, 0, 255),   // orange
            new Color32(128, 0, 128, 255),   // purple
            new Color32(0, 128, 0, 255),     // dark green
            new Color32(139, 69, 19, 255),   // brown
            new Color32(255, 192, 203, 255), // pink
            new Color32(64, 64, 64, 255),    // dark gray
            new Color32(192, 192, 192, 255), // light gray
            new Color32(0, 0, 0, 255)        // black
        };

        /// <summary>
        /// Parse colored text markup from localization string
        /// Supports formats: §c, §rgb, §[colorname], §!, etc.
        /// </summary>
        public static MarkupResult ParseMarkup(
            FixedString512Bytes input,
            Allocator allocator)
        {
            var result = new MarkupResult
            {
                Segments = new NativeList<ColoredTextSegment>(16, allocator),
                IsSuccess = false
            };

            if (input.Length == 0)
            {
                result.IsSuccess = true;
                return result;
            }

            try
            {
                // Convert to working buffer
                var workingBuffer = new NativeArray<byte>(input.Length, Allocator.Temp);
                var plainTextBuffer = new NativeArray<byte>(input.Length, Allocator.Temp);
                var richTextBuffer = new NativeArray<byte>(input.Length * 2, Allocator.Temp); // Extra space for markup

                int workingLength = CopyStringToBuffer(input, workingBuffer);

                // Process markup
                var parseResult = ProcessMarkupBuffer(
                    workingBuffer, workingLength,
                    plainTextBuffer, richTextBuffer,
                    result.Segments);

                // Convert results back to FixedStrings
                result.PlainText = BufferToFixedString(plainTextBuffer, parseResult.PlainTextLength);
                result.UnityRichText = BufferToFixedString(richTextBuffer, parseResult.RichTextLength);
                result.IsSuccess = true;

                workingBuffer.Dispose();
                plainTextBuffer.Dispose();
                richTextBuffer.Dispose();
            }
            catch (Exception)
            {
                result.Dispose();
                result.IsSuccess = false;
            }

            return result;
        }

        /// <summary>
        /// Convert markup to Unity Rich Text format
        /// </summary>
        public static FixedString512Bytes ToUnityRichText(FixedString512Bytes input)
        {
            var result = ParseMarkup(input, Allocator.Temp);
            try
            {
                return result.IsSuccess ? result.UnityRichText : input;
            }
            finally
            {
                result.Dispose();
            }
        }

        /// <summary>
        /// Strip all markup and return plain text
        /// </summary>
        public static FixedString512Bytes ToPlainText(FixedString512Bytes input)
        {
            var result = ParseMarkup(input, Allocator.Temp);
            try
            {
                return result.IsSuccess ? result.PlainText : input;
            }
            finally
            {
                result.Dispose();
            }
        }

        /// <summary>
        /// Core markup processing function
        /// </summary>
        private static (int PlainTextLength, int RichTextLength) ProcessMarkupBuffer(
            NativeArray<byte> input, int inputLength,
            NativeArray<byte> plainOutput, NativeArray<byte> richOutput,
            NativeList<ColoredTextSegment> segments)
        {
            int inputPos = 0;
            int plainPos = 0;
            int richPos = 0;

            Color32 currentColor = ParadoxColors[0]; // Default white
            TextStyle currentStyle = TextStyle.None;
            bool hasActiveColor = false;

            var currentSegment = new ColoredTextSegment
            {
                Text = new FixedString128Bytes(),
                Color = currentColor,
                HasColor = false,
                Style = TextStyle.None
            };

            while (inputPos < inputLength)
            {
                byte currentByte = input[inputPos];

                // Check for markup start (§ symbol or custom markers)
                if (currentByte == 0xC2 && inputPos + 1 < inputLength && input[inputPos + 1] == 0xA7) // UTF-8 §
                {
                    // Save current segment if it has content
                    if (currentSegment.Text.Length > 0)
                    {
                        segments.Add(currentSegment);
                        currentSegment = new ColoredTextSegment();
                    }

                    inputPos += 2; // Skip §
                    var markupResult = ProcessMarkupCode(input, inputPos, inputLength, out int consumed);

                    if (markupResult.IsReset)
                    {
                        // Reset to default
                        if (hasActiveColor)
                        {
                            AppendToBuffer(richOutput, ref richPos, "</color>");
                            hasActiveColor = false;
                        }
                        currentColor = ParadoxColors[0];
                        currentStyle = TextStyle.None;
                    }
                    else if (markupResult.HasColor)
                    {
                        // Close previous color
                        if (hasActiveColor)
                        {
                            AppendToBuffer(richOutput, ref richPos, "</color>");
                        }

                        // Open new color
                        currentColor = markupResult.Color;
                        hasActiveColor = true;
                        AppendColorTag(richOutput, ref richPos, currentColor);
                    }

                    if (markupResult.HasStyle)
                    {
                        currentStyle = markupResult.Style;
                        ApplyStyleTags(richOutput, ref richPos, currentStyle, true);
                    }

                    // Start new segment
                    currentSegment.Color = currentColor;
                    currentSegment.HasColor = hasActiveColor;
                    currentSegment.Style = currentStyle;

                    inputPos += consumed;
                }
                else
                {
                    // Regular character - copy to both outputs
                    plainOutput[plainPos++] = currentByte;
                    richOutput[richPos++] = currentByte;
                    currentSegment.Text.Append((char)currentByte);
                    inputPos++;
                }
            }

            // Save final segment
            if (currentSegment.Text.Length > 0)
            {
                segments.Add(currentSegment);
            }

            // Close any open tags
            if (hasActiveColor)
            {
                AppendToBuffer(richOutput, ref richPos, "</color>");
            }

            return (plainPos, richPos);
        }

        /// <summary>
        /// Process individual markup code
        /// </summary>
        private static (bool HasColor, Color32 Color, bool HasStyle, TextStyle Style, bool IsReset) ProcessMarkupCode(
            NativeArray<byte> input, int startPos, int inputLength, out int consumed)
        {
            consumed = 0;

            if (startPos >= inputLength)
                return (false, default, false, TextStyle.None, false);

            byte code = input[startPos];
            consumed = 1;

            // Single character color codes
            switch (code)
            {
                case (byte)'!': // Reset
                    return (false, default, false, TextStyle.None, true);

                case (byte)'r': // Red
                    return (true, ParadoxColors[1], false, TextStyle.None, false);

                case (byte)'g': // Green
                    return (true, ParadoxColors[2], false, TextStyle.None, false);

                case (byte)'b': // Blue
                    return (true, ParadoxColors[3], false, TextStyle.None, false);

                case (byte)'y': // Yellow
                    return (true, ParadoxColors[4], false, TextStyle.None, false);

                case (byte)'w': // White
                    return (true, ParadoxColors[0], false, TextStyle.None, false);

                case (byte)'B': // Bold
                    return (false, default, true, TextStyle.Bold, false);

                case (byte)'I': // Italic
                    return (false, default, true, TextStyle.Italic, false);

                // Hex color codes (e.g., §#FF0000)
                case (byte)'#':
                    return ProcessHexColor(input, startPos + 1, inputLength, out consumed);

                // Extended color names [colorname]
                case (byte)'[':
                    return ProcessNamedColor(input, startPos, inputLength, out consumed);

                default:
                    // Single digit color index
                    if (code >= '0' && code <= '9')
                    {
                        int colorIndex = code - '0';
                        if (colorIndex < ParadoxColors.Length)
                        {
                            return (true, ParadoxColors[colorIndex], false, TextStyle.None, false);
                        }
                    }
                    break;
            }

            return (false, default, false, TextStyle.None, false);
        }

        /// <summary>
        /// Process hex color code (#RRGGBB or #RGB)
        /// </summary>
        private static (bool HasColor, Color32 Color, bool HasStyle, TextStyle Style, bool IsReset) ProcessHexColor(
            NativeArray<byte> input, int startPos, int inputLength, out int consumed)
        {
            consumed = 0;

            // Need at least 3 characters for #RGB
            if (startPos + 3 > inputLength)
                return (false, default, false, TextStyle.None, false);

            // Try 6-character hex first (#RRGGBB)
            if (startPos + 6 <= inputLength)
            {
                if (TryParseHex(input, startPos, 6, out Color32 color6))
                {
                    consumed = 6;
                    return (true, color6, false, TextStyle.None, false);
                }
            }

            // Try 3-character hex (#RGB)
            if (TryParseHex(input, startPos, 3, out Color32 color3))
            {
                consumed = 3;
                return (true, color3, false, TextStyle.None, false);
            }

            return (false, default, false, TextStyle.None, false);
        }

        /// <summary>
        /// Process named color [colorname]
        /// </summary>
        private static (bool HasColor, Color32 Color, bool HasStyle, TextStyle Style, bool IsReset) ProcessNamedColor(
            NativeArray<byte> input, int startPos, int inputLength, out int consumed)
        {
            consumed = 0;

            // Find closing bracket
            int endPos = FindClosingBracket(input, startPos + 1, inputLength);
            if (endPos == -1)
                return (false, default, false, TextStyle.None, false);

            // Extract color name
            var colorName = ExtractColorName(input, startPos + 1, endPos);
            consumed = endPos - startPos + 1;

            // Look up color by name
            if (TryGetNamedColor(colorName, out Color32 color))
            {
                return (true, color, false, TextStyle.None, false);
            }

            return (false, default, false, TextStyle.None, false);
        }

        /// <summary>
        /// Helper functions
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendToBuffer(NativeArray<byte> buffer, ref int pos, string text)
        {
            for (int i = 0; i < text.Length && pos < buffer.Length; i++)
            {
                buffer[pos++] = (byte)text[i];
            }
        }

        private static void AppendColorTag(NativeArray<byte> buffer, ref int pos, Color32 color)
        {
            string colorTag = $"<color=#{color.r:X2}{color.g:X2}{color.b:X2}>";
            AppendToBuffer(buffer, ref pos, colorTag);
        }

        private static void ApplyStyleTags(NativeArray<byte> buffer, ref int pos, TextStyle style, bool open)
        {
            if ((style & TextStyle.Bold) != 0)
            {
                AppendToBuffer(buffer, ref pos, open ? "<b>" : "</b>");
            }
            if ((style & TextStyle.Italic) != 0)
            {
                AppendToBuffer(buffer, ref pos, open ? "<i>" : "</i>");
            }
            if ((style & TextStyle.Underline) != 0)
            {
                AppendToBuffer(buffer, ref pos, open ? "<u>" : "</u>");
            }
            if ((style & TextStyle.Strikethrough) != 0)
            {
                AppendToBuffer(buffer, ref pos, open ? "<s>" : "</s>");
            }
        }

        private static bool TryParseHex(NativeArray<byte> input, int startPos, int length, out Color32 color)
        {
            color = default;

            if (length == 3)
            {
                // Parse #RGB format
                if (TryParseHexByte(input, startPos, 1, out byte r) &&
                    TryParseHexByte(input, startPos + 1, 1, out byte g) &&
                    TryParseHexByte(input, startPos + 2, 1, out byte b))
                {
                    // Expand single digits: R -> RR
                    color = new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), 255);
                    return true;
                }
            }
            else if (length == 6)
            {
                // Parse #RRGGBB format
                if (TryParseHexByte(input, startPos, 2, out byte r) &&
                    TryParseHexByte(input, startPos + 2, 2, out byte g) &&
                    TryParseHexByte(input, startPos + 4, 2, out byte b))
                {
                    color = new Color32(r, g, b, 255);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseHexByte(NativeArray<byte> input, int startPos, int length, out byte value)
        {
            value = 0;

            for (int i = 0; i < length; i++)
            {
                byte b = input[startPos + i];
                byte digit;

                if (b >= '0' && b <= '9')
                    digit = (byte)(b - '0');
                else if (b >= 'A' && b <= 'F')
                    digit = (byte)(b - 'A' + 10);
                else if (b >= 'a' && b <= 'f')
                    digit = (byte)(b - 'a' + 10);
                else
                    return false;

                value = (byte)((value << 4) | digit);
            }

            return true;
        }

        private static int FindClosingBracket(NativeArray<byte> input, int startPos, int length)
        {
            for (int i = startPos; i < length; i++)
            {
                if (input[i] == ']')
                    return i;
            }
            return -1;
        }

        private static string ExtractColorName(NativeArray<byte> input, int startPos, int endPos)
        {
            var result = "";
            for (int i = startPos; i < endPos; i++)
            {
                result += (char)input[i];
            }
            return result.ToLower();
        }

        private static bool TryGetNamedColor(string colorName, out Color32 color)
        {
            color = default;

            switch (colorName)
            {
                case "red": color = ParadoxColors[1]; return true;
                case "green": color = ParadoxColors[2]; return true;
                case "blue": color = ParadoxColors[3]; return true;
                case "yellow": color = ParadoxColors[4]; return true;
                case "white": color = ParadoxColors[0]; return true;
                case "black": color = ParadoxColors[15]; return true;
                case "gray": case "grey": color = ParadoxColors[7]; return true;
                case "orange": color = ParadoxColors[8]; return true;
                case "purple": color = ParadoxColors[9]; return true;
                case "brown": color = ParadoxColors[11]; return true;
                case "pink": color = ParadoxColors[12]; return true;
                default: return false;
            }
        }

        private static int CopyStringToBuffer(FixedString512Bytes str, NativeArray<byte> buffer)
        {
            int length = Math.Min(str.Length, buffer.Length);
            for (int i = 0; i < length; i++)
            {
                buffer[i] = (byte)str[i];
            }
            return length;
        }

        private static FixedString512Bytes BufferToFixedString(NativeArray<byte> buffer, int length)
        {
            var result = new FixedString512Bytes();
            for (int i = 0; i < Math.Min(length, 511); i++)
            {
                result.Append((char)buffer[i]);
            }
            return result;
        }
    }
}
