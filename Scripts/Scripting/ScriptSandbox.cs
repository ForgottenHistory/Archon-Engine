using Core;
using MoonSharp.Interpreter;

namespace Scripting
{
    /// <summary>
    /// Security configuration for Lua script execution.
    /// Whitelists safe modules and blocks dangerous operations.
    ///
    /// SECURITY: Scripts cannot access filesystem, network, or debugging.
    /// Only registered bindings provide access to game functionality.
    /// </summary>
    public class ScriptSandbox
    {
        /// <summary>
        /// Get the allowed CoreModules for sandboxed execution.
        /// Blocks io, os, debug, load functions, and other dangerous APIs.
        /// </summary>
        public CoreModules GetCoreModules()
        {
            // Start with preset "safe" modules
            // This includes: basic, table, string, math, coroutine
            // This excludes: io, os, debug, load, metatables (partially)
            return CoreModules.Preset_SoftSandbox;
        }

        /// <summary>
        /// Additional hardening applied after script creation.
        /// Removes any remaining dangerous globals.
        /// </summary>
        public void HardenScript(Script script)
        {
            // Remove potentially dangerous functions
            script.Globals["load"] = DynValue.Nil;
            script.Globals["loadfile"] = DynValue.Nil;
            script.Globals["loadstring"] = DynValue.Nil;
            script.Globals["dofile"] = DynValue.Nil;
            script.Globals["require"] = DynValue.Nil;

            // Remove debug library entirely
            script.Globals["debug"] = DynValue.Nil;

            // Remove io library entirely
            script.Globals["io"] = DynValue.Nil;

            // Remove os library entirely
            script.Globals["os"] = DynValue.Nil;

            // Remove package library (module loading)
            script.Globals["package"] = DynValue.Nil;

            // Keep but restrict rawset/rawget (needed for some table ops)
            // These are safe in our sandboxed context

            ArchonLogger.Log("Script sandbox hardening applied", "core_scripting");
        }

        /// <summary>
        /// Validate that a script string doesn't contain obvious bypass attempts.
        /// This is a defense-in-depth measure - sandbox should handle these anyway.
        /// </summary>
        public bool ValidateScriptSource(string code, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(code))
            {
                error = "Empty script code";
                return false;
            }

            // Check for common bypass patterns
            string[] blockedPatterns = new[]
            {
                "getfenv",
                "setfenv",
                "debug.",
                "io.",
                "os.",
                "loadfile",
                "dofile",
                "package.",
                "require(",
                "_G[",  // Accessing global table by indexing
                "rawequal",
                "rawlen"
            };

            string lowerCode = code.ToLowerInvariant();
            foreach (var pattern in blockedPatterns)
            {
                if (lowerCode.Contains(pattern.ToLowerInvariant()))
                {
                    error = $"Script contains blocked pattern: {pattern}";
                    ArchonLogger.LogWarning($"Script validation failed: blocked pattern '{pattern}'", "core_scripting");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Configuration limits for script execution
        /// </summary>
        public static class Limits
        {
            /// <summary>
            /// Maximum instructions before script is terminated
            /// Prevents infinite loops
            /// </summary>
            public const int MaxInstructions = 100000;

            /// <summary>
            /// Maximum recursion depth
            /// </summary>
            public const int MaxRecursionDepth = 100;

            /// <summary>
            /// Maximum string length for script input
            /// </summary>
            public const int MaxScriptLength = 1000000; // 1MB

            /// <summary>
            /// Maximum table size (number of entries)
            /// </summary>
            public const int MaxTableSize = 10000;
        }
    }
}
