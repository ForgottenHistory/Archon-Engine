using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Core.Commands
{
    /// <summary>
    /// ENGINE LAYER - Tracks executed commands for replay/verification
    ///
    /// Purpose:
    /// - Hybrid save/load: Store recent commands for determinism verification
    /// - Bug reproduction: Log commands to reproduce exact game state
    /// - Replay system foundation: Command history for replay viewer
    ///
    /// Architecture:
    /// - Ring buffer: Keeps last N commands only (bounded memory)
    /// - Serialization: Commands serialize to byte arrays
    /// - Integration: CommandProcessor calls LogCommand after execution
    ///
    /// Usage:
    /// commandLogger.LogCommand(command);
    /// byte[][] recentCommands = commandLogger.GetRecentCommands(100);
    /// </summary>
    public class CommandLogger : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Maximum commands to keep in history (ring buffer size)")]
        public int maxCommandHistory = 6000; // 100 ticks × 60 commands/tick

        [Tooltip("Enable debug logging for command tracking")]
        public bool logCommandExecution = false;

        // Ring buffer for command history
        private Queue<byte[]> commandHistory = new Queue<byte[]>();

        // Statistics
        private int totalCommandsLogged = 0;
        private long totalBytesLogged = 0;

        /// <summary>
        /// Log a command after execution
        /// </summary>
        public void LogCommand(ICommand command)
        {
            if (command == null)
            {
                ArchonLogger.LogWarning("CommandLogger: Cannot log null command", "core_commands");
                return;
            }

            try
            {
                // Serialize command to bytes
                byte[] commandBytes = SerializeCommand(command);

                // Add to ring buffer
                commandHistory.Enqueue(commandBytes);

                // Maintain ring buffer size
                while (commandHistory.Count > maxCommandHistory)
                {
                    commandHistory.Dequeue();
                }

                // Update statistics
                totalCommandsLogged++;
                totalBytesLogged += commandBytes.Length;

                if (logCommandExecution)
                {
                    ArchonLogger.Log($"CommandLogger: Logged {command.GetType().Name} ({commandBytes.Length} bytes)", "core_commands");
                }
            }
            catch (System.Exception ex)
            {
                ArchonLogger.LogError($"CommandLogger: Failed to serialize command - {ex.Message}", "core_commands");
            }
        }

        /// <summary>
        /// Get recent commands (last N commands)
        /// </summary>
        public List<byte[]> GetRecentCommands(int count)
        {
            List<byte[]> recentCommands = new List<byte[]>();

            if (count >= commandHistory.Count)
            {
                // Return all commands
                recentCommands.AddRange(commandHistory);
            }
            else
            {
                // Return last N commands
                int skipCount = commandHistory.Count - count;
                int currentIndex = 0;

                foreach (var commandBytes in commandHistory)
                {
                    if (currentIndex >= skipCount)
                    {
                        recentCommands.Add(commandBytes);
                    }
                    currentIndex++;
                }
            }

            return recentCommands;
        }

        /// <summary>
        /// Get all logged commands
        /// </summary>
        public List<byte[]> GetAllCommands()
        {
            return new List<byte[]>(commandHistory);
        }

        /// <summary>
        /// Clear command history
        /// </summary>
        public void ClearHistory()
        {
            commandHistory.Clear();
            totalCommandsLogged = 0;
            totalBytesLogged = 0;

            if (logCommandExecution)
            {
                ArchonLogger.Log("CommandLogger: Command history cleared", "core_commands");
            }
        }

        /// <summary>
        /// Get statistics about logged commands
        /// </summary>
        public (int totalCommands, long totalBytes, int currentBufferSize) GetStatistics()
        {
            return (totalCommandsLogged, totalBytesLogged, commandHistory.Count);
        }

        // ====================================================================
        // SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Serialize command to byte array
        /// </summary>
        private byte[] SerializeCommand(ICommand command)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // Write command type name (for deserialization)
                string typeName = command.GetType().AssemblyQualifiedName;
                writer.Write(typeName);

                // Call command's Serialize method
                command.Serialize(writer);

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Deserialize command from byte array
        /// NOTE: Requires command type to be available at runtime
        /// </summary>
        public ICommand DeserializeCommand(byte[] commandBytes)
        {
            using (MemoryStream stream = new MemoryStream(commandBytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Read command type name
                string typeName = reader.ReadString();

                // Get command type
                System.Type commandType = System.Type.GetType(typeName);
                if (commandType == null)
                {
                    throw new System.Exception($"Command type not found: {typeName}");
                }

                // Create instance
                ICommand command = System.Activator.CreateInstance(commandType) as ICommand;
                if (command == null)
                {
                    throw new System.Exception($"Failed to create command instance: {typeName}");
                }

                // TODO: Call command's Deserialize method (need to add Deserialize to ICommand interface)
                // For now, just return the empty instance
                // command.Deserialize(reader);

                return command;
            }
        }

        // ====================================================================
        // DEBUG UTILITIES
        // ====================================================================

        #if UNITY_EDITOR
        /// <summary>
        /// Context menu: Display command logger statistics
        /// </summary>
        [ContextMenu("Show Statistics")]
        private void ShowStatistics()
        {
            var stats = GetStatistics();
            ArchonLogger.Log($"CommandLogger Statistics:", "core_commands");
            ArchonLogger.Log($"  Total Commands Logged: {stats.totalCommands}", "core_commands");
            ArchonLogger.Log($"  Total Bytes Logged: {stats.totalBytes:N0}", "core_commands");
            ArchonLogger.Log($"  Current Buffer Size: {stats.currentBufferSize}", "core_commands");
            ArchonLogger.Log($"  Average Command Size: {(stats.totalCommands > 0 ? stats.totalBytes / stats.totalCommands : 0)} bytes", "core_commands");
        }

        /// <summary>
        /// Context menu: Clear command history
        /// </summary>
        [ContextMenu("Clear History")]
        private void ClearHistoryContextMenu()
        {
            ClearHistory();
            ArchonLogger.Log("CommandLogger: History cleared via context menu", "core_commands");
        }
        #endif
    }
}
