using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.YAML
{
    /// <summary>
    /// Advanced string replacement system for dynamic localization
    /// Supports parameter substitution, nested replacements, and conditional text
    /// </summary>
    public static class StringReplacementSystem
    {
        /// <summary>
        /// Replacement parameter
        /// </summary>
        public struct ReplacementParam
        {
            public FixedString64Bytes Key;
            public FixedString512Bytes Value;
            public bool IsConditional;
            public bool ConditionValue; // For conditional parameters
        }

        /// <summary>
        /// Replacement context for complex operations
        /// </summary>
        public struct ReplacementContext
        {
            public NativeHashMap<uint, FixedString512Bytes> Parameters; // Key hash -> value
            public NativeHashMap<uint, bool> Conditions; // Condition hash -> bool
            public FixedString64Bytes CurrentLanguage;
            public bool AllowNestedReplacements;
            public int MaxReplacementDepth;

            public void Dispose()
            {
                if (Parameters.IsCreated)
                    Parameters.Dispose();
                if (Conditions.IsCreated)
                    Conditions.Dispose();
            }
        }

        /// <summary>
        /// Result of string replacement operation
        /// </summary>
        public struct ReplacementResult
        {
            public FixedString512Bytes ProcessedString;
            public int ReplacementsMade;
            public int UnresolvedReferences;
            public bool Success;
        }

        /// <summary>
        /// Create replacement context
        /// </summary>
        public static ReplacementContext CreateContext(
            int expectedParams,
            Allocator allocator)
        {
            return new ReplacementContext
            {
                Parameters = new NativeHashMap<uint, FixedString512Bytes>(expectedParams, allocator),
                Conditions = new NativeHashMap<uint, bool>(expectedParams / 2, allocator),
                AllowNestedReplacements = true,
                MaxReplacementDepth = 5
            };
        }

        /// <summary>
        /// Add parameter to replacement context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddParameter(
            ref ReplacementContext context,
            FixedString64Bytes key,
            FixedString512Bytes value)
        {
            uint keyHash = HashString(key);
            context.Parameters[keyHash] = value;
        }

        /// <summary>
        /// Add condition to replacement context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddCondition(
            ref ReplacementContext context,
            FixedString64Bytes key,
            bool value)
        {
            uint keyHash = HashString(key);
            context.Conditions[keyHash] = value;
        }

        /// <summary>
        /// Process string with parameter replacement
        /// Supports: $KEY$, ${KEY}, [KEY], conditional text [?CONDITION]text[/CONDITION]
        /// </summary>
        public static ReplacementResult ProcessString(
            FixedString512Bytes inputString,
            ReplacementContext context)
        {
            var result = new ReplacementResult
            {
                ProcessedString = inputString,
                ReplacementsMade = 0,
                UnresolvedReferences = 0,
                Success = true
            };

            if (inputString.Length == 0)
                return result;

            // Convert to byte array for processing
            var workingBuffer = new NativeArray<byte>(1024, Allocator.Temp);
            var outputBuffer = new NativeArray<byte>(1024, Allocator.Temp);

            try
            {
                // Copy input to working buffer
                int inputLength = CopyStringToBuffer(inputString, workingBuffer);

                // Process replacements
                int depth = 0;
                while (depth < context.MaxReplacementDepth)
                {
                    var iterationResult = ProcessReplacementIteration(
                        workingBuffer, inputLength, outputBuffer, context);

                    if (iterationResult.ReplacementsMade == 0)
                        break; // No more replacements needed

                    result.ReplacementsMade += iterationResult.ReplacementsMade;
                    result.UnresolvedReferences += iterationResult.UnresolvedReferences;

                    // Swap buffers for next iteration
                    var temp = workingBuffer;
                    workingBuffer = outputBuffer;
                    outputBuffer = temp;
                    inputLength = iterationResult.OutputLength;

                    depth++;

                    if (!context.AllowNestedReplacements)
                        break;
                }

                // Convert back to FixedString
                result.ProcessedString = BufferToFixedString(workingBuffer, inputLength);
            }
            finally
            {
                workingBuffer.Dispose();
                outputBuffer.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Process string with simple parameter array
        /// </summary>
        public static ReplacementResult ProcessStringSimple(
            FixedString512Bytes inputString,
            NativeArray<ReplacementParam> parameters,
            Allocator allocator)
        {
            var context = CreateContext(parameters.Length, allocator);

            try
            {
                foreach (var param in parameters)
                {
                    if (param.IsConditional)
                    {
                        AddCondition(ref context, param.Key, param.ConditionValue);
                    }
                    else
                    {
                        AddParameter(ref context, param.Key, param.Value);
                    }
                }

                return ProcessString(inputString, context);
            }
            finally
            {
                context.Dispose();
            }
        }

        /// <summary>
        /// Single iteration of replacement processing
        /// </summary>
        private static (int ReplacementsMade, int UnresolvedReferences, int OutputLength) ProcessReplacementIteration(
            NativeArray<byte> input,
            int inputLength,
            NativeArray<byte> output,
            ReplacementContext context)
        {
            int replacements = 0;
            int unresolved = 0;
            int outputPos = 0;
            int inputPos = 0;

            while (inputPos < inputLength && outputPos < output.Length - 1)
            {
                byte currentByte = input[inputPos];

                // Check for replacement patterns
                if (currentByte == '$' || currentByte == '[' || currentByte == '{')
                {
                    var replacement = FindAndProcessReplacement(
                        input, inputPos, inputLength, context, out int consumed);

                    if (replacement.Found)
                    {
                        // Copy replacement value
                        for (int i = 0; i < replacement.Value.Length && outputPos < output.Length - 1; i++)
                        {
                            output[outputPos++] = (byte)replacement.Value[i];
                        }
                        inputPos += consumed;
                        replacements++;
                    }
                    else
                    {
                        if (replacement.WasValidPattern)
                            unresolved++;

                        // Copy original character
                        output[outputPos++] = currentByte;
                        inputPos++;
                    }
                }
                else
                {
                    // Copy normal character
                    output[outputPos++] = currentByte;
                    inputPos++;
                }
            }

            return (replacements, unresolved, outputPos);
        }

        /// <summary>
        /// Find and process replacement at current position
        /// </summary>
        private static (bool Found, bool WasValidPattern, FixedString512Bytes Value) FindAndProcessReplacement(
            NativeArray<byte> input,
            int startPos,
            int inputLength,
            ReplacementContext context,
            out int consumed)
        {
            consumed = 1;

            byte startChar = input[startPos];

            // Handle different replacement patterns
            if (startChar == '$')
            {
                return ProcessDollarReplacement(input, startPos, inputLength, context, out consumed);
            }
            else if (startChar == '[')
            {
                return ProcessBracketReplacement(input, startPos, inputLength, context, out consumed);
            }
            else if (startChar == '{')
            {
                return ProcessBraceReplacement(input, startPos, inputLength, context, out consumed);
            }

            return (false, false, default);
        }

        /// <summary>
        /// Process $KEY$ replacement
        /// </summary>
        private static (bool Found, bool WasValidPattern, FixedString512Bytes Value) ProcessDollarReplacement(
            NativeArray<byte> input,
            int startPos,
            int inputLength,
            ReplacementContext context,
            out int consumed)
        {
            consumed = 1;

            // Find closing $
            int endPos = FindClosingChar(input, startPos + 1, inputLength, (byte)'$');
            if (endPos == -1)
                return (false, false, default);

            // Extract key
            var key = ExtractKey(input, startPos + 1, endPos);
            consumed = endPos - startPos + 1;

            // Look up replacement
            uint keyHash = HashString(key);
            if (context.Parameters.TryGetValue(keyHash, out var value))
            {
                return (true, true, value);
            }

            return (false, true, default);
        }

        /// <summary>
        /// Process [KEY] replacement or [?CONDITION] conditional
        /// </summary>
        private static (bool Found, bool WasValidPattern, FixedString512Bytes Value) ProcessBracketReplacement(
            NativeArray<byte> input,
            int startPos,
            int inputLength,
            ReplacementContext context,
            out int consumed)
        {
            consumed = 1;

            // Find closing ]
            int endPos = FindClosingChar(input, startPos + 1, inputLength, (byte)']');
            if (endPos == -1)
                return (false, false, default);

            consumed = endPos - startPos + 1;

            // Check if it's a conditional
            if (startPos + 1 < inputLength && input[startPos + 1] == '?')
            {
                return ProcessConditional(input, startPos, inputLength, context, out consumed);
            }

            // Regular parameter replacement
            var key = ExtractKey(input, startPos + 1, endPos);
            uint keyHash = HashString(key);
            if (context.Parameters.TryGetValue(keyHash, out var value))
            {
                return (true, true, value);
            }

            return (false, true, default);
        }

        /// <summary>
        /// Process {KEY} replacement
        /// </summary>
        private static (bool Found, bool WasValidPattern, FixedString512Bytes Value) ProcessBraceReplacement(
            NativeArray<byte> input,
            int startPos,
            int inputLength,
            ReplacementContext context,
            out int consumed)
        {
            consumed = 1;

            // Find closing }
            int endPos = FindClosingChar(input, startPos + 1, inputLength, (byte)'}');
            if (endPos == -1)
                return (false, false, default);

            var key = ExtractKey(input, startPos + 1, endPos);
            consumed = endPos - startPos + 1;

            uint keyHash = HashString(key);
            if (context.Parameters.TryGetValue(keyHash, out var value))
            {
                return (true, true, value);
            }

            return (false, true, default);
        }

        /// <summary>
        /// Process conditional text [?CONDITION]text[/CONDITION]
        /// </summary>
        private static (bool Found, bool WasValidPattern, FixedString512Bytes Value) ProcessConditional(
            NativeArray<byte> input,
            int startPos,
            int inputLength,
            ReplacementContext context,
            out int consumed)
        {
            consumed = 1;

            // Find closing ] for condition
            int conditionEnd = FindClosingChar(input, startPos + 1, inputLength, (byte)']');
            if (conditionEnd == -1)
                return (false, false, default);

            // Extract condition key (skip the ?)
            var conditionKey = ExtractKey(input, startPos + 2, conditionEnd);

            // Find closing tag [/CONDITION]
            int closingTagStart = FindConditionalClosing(input, conditionEnd + 1, inputLength, conditionKey);
            if (closingTagStart == -1)
            {
                consumed = conditionEnd - startPos + 1;
                return (false, true, default);
            }

            consumed = closingTagStart + conditionKey.Length + 3 - startPos; // +3 for [/]

            // Check condition
            uint conditionHash = HashString(conditionKey);
            if (context.Conditions.TryGetValue(conditionHash, out bool conditionValue) && conditionValue)
            {
                // Return the text between the tags
                var conditionalText = ExtractConditionalText(input, conditionEnd + 1, closingTagStart);
                return (true, true, conditionalText);
            }

            // Condition false or not found, return empty
            return (true, true, new FixedString512Bytes());
        }

        /// <summary>
        /// Helper methods for string processing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashString(FixedString64Bytes str)
        {
            var bytes = new NativeArray<byte>(str.Length, Allocator.Temp);
            for (int i = 0; i < str.Length; i++)
            {
                bytes[i] = (byte)str[i];
            }
            uint hash = ComputeHash(bytes);
            bytes.Dispose();
            return hash;
        }

        /// <summary>
        /// Compute FNV-1a hash of byte array
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeHash(NativeArray<byte> data)
        {
            const uint FNV_OFFSET_BASIS = 2166136261;
            const uint FNV_PRIME = 16777619;

            uint hash = FNV_OFFSET_BASIS;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }
            return hash;
        }

        private static int FindClosingChar(NativeArray<byte> input, int startPos, int length, byte closingChar)
        {
            for (int i = startPos; i < length; i++)
            {
                if (input[i] == closingChar)
                    return i;
            }
            return -1;
        }

        private static FixedString64Bytes ExtractKey(NativeArray<byte> input, int startPos, int endPos)
        {
            var key = new FixedString64Bytes();
            for (int i = startPos; i < endPos && i < startPos + 63; i++)
            {
                key.Append((char)input[i]);
            }
            return key;
        }

        private static int FindConditionalClosing(NativeArray<byte> input, int startPos, int length, FixedString64Bytes conditionKey)
        {
            // Look for [/CONDITION]
            for (int i = startPos; i < length - conditionKey.Length - 2; i++)
            {
                if (input[i] == '[' && input[i + 1] == '/')
                {
                    bool match = true;
                    for (int j = 0; j < conditionKey.Length; j++)
                    {
                        if (input[i + 2 + j] != (byte)conditionKey[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match && input[i + 2 + conditionKey.Length] == ']')
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static FixedString512Bytes ExtractConditionalText(NativeArray<byte> input, int startPos, int endPos)
        {
            var text = new FixedString512Bytes();
            for (int i = startPos; i < endPos && text.Length < 511; i++)
            {
                text.Append((char)input[i]);
            }
            return text;
        }

        private static int CopyStringToBuffer(FixedString512Bytes str, NativeArray<byte> buffer)
        {
            int length = Math.Min(str.Length, buffer.Length - 1);
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