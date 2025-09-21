using System;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace ParadoxParser.Core
{
    public static class MemoryProfiler
    {
        private static readonly ProfilerMarker s_LexerMarker = new ProfilerMarker("ParadoxParser.Lexer");
        private static readonly ProfilerMarker s_ParserMarker = new ProfilerMarker("ParadoxParser.Parser");
        private static readonly ProfilerMarker s_ASTMarker = new ProfilerMarker("ParadoxParser.AST");

        public static void BeginLexerSample()
        {
            s_LexerMarker.Begin();
        }

        public static void EndLexerSample()
        {
            s_LexerMarker.End();
        }

        public static void BeginParserSample()
        {
            s_ParserMarker.Begin();
        }

        public static void EndParserSample()
        {
            s_ParserMarker.End();
        }

        public static void BeginASTSample()
        {
            s_ASTMarker.Begin();
        }

        public static void EndASTSample()
        {
            s_ASTMarker.End();
        }

        public static long GetCurrentMemoryUsage()
        {
            return Profiler.GetTotalAllocatedMemoryLong();
        }

        public static void LogMemoryUsage(string context)
        {
            var totalMemory = Profiler.GetTotalAllocatedMemoryLong();
            var reservedMemory = Profiler.GetTotalReservedMemoryLong();

            UnityEngine.Debug.Log($"[ParadoxParser] {context} - Total: {totalMemory / (1024 * 1024):F2}MB, Reserved: {reservedMemory / (1024 * 1024):F2}MB");
        }

        public static MemorySnapshot TakeSnapshot(string name)
        {
            return new MemorySnapshot
            {
                Name = name,
                TotalMemory = Profiler.GetTotalAllocatedMemoryLong(),
                ReservedMemory = Profiler.GetTotalReservedMemoryLong(),
                Timestamp = DateTime.Now
            };
        }
    }

    public struct MemorySnapshot
    {
        public string Name;
        public long TotalMemory;
        public long ReservedMemory;
        public DateTime Timestamp;

        public override string ToString()
        {
            return $"{Name} - Total: {TotalMemory / (1024 * 1024):F2}MB, Reserved: {ReservedMemory / (1024 * 1024):F2}MB at {Timestamp:HH:mm:ss}";
        }
    }
}