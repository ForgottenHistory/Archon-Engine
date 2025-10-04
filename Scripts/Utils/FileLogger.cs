using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
    /// <summary>
    /// File-based logging system that captures all DominionLogger.Log messages
    /// </summary>
    public class FileLogger : MonoBehaviour
    {
        private static FileLogger instance;
        private static bool isQuitting = false;

        public static bool IsQuitting => isQuitting;
        public static FileLogger Instance
        {
            get
            {
                if (instance == null && !isQuitting && Application.isPlaying)
                {
                    GameObject go = new GameObject("FileLogger");
                    instance = go.AddComponent<FileLogger>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("Settings")]
        public bool enableFileLogging = true;
        public bool includeTimestamps = true;  // Always include timestamps for better debugging
        public bool includeStackTrace = false;
        public int maxLogFileSize = 10485760; // 10MB
        public string logFileName = "dominion_log.log";

        [Header("Log Filters")]
        public bool logInfo = true;
        public bool logWarnings = true;
        public bool logErrors = true;
        public bool logExceptions = true;

        [Header("Performance")]
        public bool useAsyncWrite = true;
        public float flushInterval = 1f; // Flush to disk every second

        private string logFilePath;
        private StreamWriter logWriter;
        private Queue<string> pendingLogs = new Queue<string>();

        // System-based logging
        private Dictionary<string, StreamWriter> systemLogWriters = new Dictionary<string, StreamWriter>();
        private Dictionary<string, Queue<string>> systemPendingLogs = new Dictionary<string, Queue<string>>();
        private object logLock = new object();
        private float lastFlushTime;
        private bool isInitialized = false;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        void Initialize()
        {
            if (isInitialized) return;

            // Create logs directory in project root (NOT inside Assets/ - Unity tries to import them!)
            // Application.dataPath = "D:/Project/Assets", we want "D:/Project/Logs"
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string logsDir = Path.Combine(projectRoot, "Logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Setup log file paths (overwrite existing for testing)
            logFilePath = Path.Combine(logsDir, logFileName);

            // Create or open main log file (overwrite existing)
            try
            {
                logWriter = new StreamWriter(logFilePath, false, Encoding.UTF8); // false = overwrite
                logWriter.AutoFlush = !useAsyncWrite;

                WriteHeader();

                // Disable Unity's log message received event - we use direct logging now
                // Application.logMessageReceived += HandleLog;

                isInitialized = true;
                // Use Unity's Debug.Log directly to avoid infinite recursion during initialization
                Debug.Log($"FileLogger initialized. Main log: {logFilePath}");
            }
            catch (Exception e)
            {
                DominionLogger.LogError($"Failed to initialize FileLogger: {e.Message}");
            }
        }

        void WriteHeader()
        {
            logWriter.WriteLine("========================================");
            logWriter.WriteLine($"Dominion Complete Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logWriter.WriteLine($"Unity Version: {Application.unityVersion}");
            logWriter.WriteLine($"Platform: {Application.platform}");
            logWriter.WriteLine($"Map: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            logWriter.WriteLine("========================================");
            logWriter.WriteLine();
        }

        /// <summary>
        /// Initialize a system-specific log file
        /// </summary>
        private void InitializeSystemLog(string systemName)
        {
            if (systemLogWriters.ContainsKey(systemName)) return;

            string systemLogPath = Path.Combine(Path.GetDirectoryName(logFilePath), $"{systemName}.log");

            try
            {
                var writer = new StreamWriter(systemLogPath, false, Encoding.UTF8); // false = overwrite
                writer.AutoFlush = !useAsyncWrite;

                // Write system-specific header
                writer.WriteLine("========================================");
                writer.WriteLine($"Dominion {systemName} Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"System-specific logging for {systemName}");
                writer.WriteLine("========================================");
                writer.WriteLine();

                systemLogWriters[systemName] = writer;
                systemPendingLogs[systemName] = new Queue<string>();

                Debug.Log($"Initialized system log for {systemName}: {systemLogPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize system log for {systemName}: {e.Message}");
            }
        }

        /// <summary>
        /// Log a message to both main log and system-specific log
        /// </summary>
        public void WriteSystemLog(string message, string systemName = null, LogType logType = LogType.Log)
        {
            if (!enableFileLogging || !isInitialized) return;

            // Filter based on log type
            bool shouldLog = logType switch
            {
                LogType.Log => logInfo,
                LogType.Warning => logWarnings,
                LogType.Error => logErrors,
                LogType.Exception => logExceptions,
                LogType.Assert => logErrors,
                _ => true
            };

            if (!shouldLog) return;

            // Format log entry
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] [{logType}] {message}";

            if (useAsyncWrite)
            {
                lock (logLock)
                {
                    // Only add to main log if no system is specified
                    if (string.IsNullOrEmpty(systemName))
                    {
                        pendingLogs.Enqueue(logEntry);
                    }
                    else
                    {
                        // Add to system-specific log only
                        if (!systemPendingLogs.ContainsKey(systemName))
                        {
                            InitializeSystemLog(systemName);
                        }
                        systemPendingLogs[systemName].Enqueue(logEntry);
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(systemName))
                {
                    WriteToFile(logEntry);
                }
                else
                {
                    WriteToSystemFile(logEntry, systemName);
                }
            }
        }

        void Update()
        {
            if (!useAsyncWrite || !isInitialized) return;

            // Flush pending logs periodically
            if (Time.time - lastFlushTime > flushInterval)
            {
                FlushPendingLogs();
                lastFlushTime = Time.time;
            }
        }

        void FlushPendingLogs()
        {
            if (pendingLogs.Count == 0 && systemPendingLogs.Values.All(q => q.Count == 0)) return;

            List<string> logsToWrite = new List<string>();
            Dictionary<string, List<string>> systemLogsToWrite = new Dictionary<string, List<string>>();

            lock (logLock)
            {
                // Collect main logs
                while (pendingLogs.Count > 0)
                {
                    logsToWrite.Add(pendingLogs.Dequeue());
                }

                // Collect system logs
                foreach (var kvp in systemPendingLogs)
                {
                    string systemName = kvp.Key;
                    var logs = new List<string>();
                    while (kvp.Value.Count > 0)
                    {
                        logs.Add(kvp.Value.Dequeue());
                    }
                    if (logs.Count > 0)
                    {
                        systemLogsToWrite[systemName] = logs;
                    }
                }
            }

            // Write to main log
            foreach (string log in logsToWrite)
            {
                WriteToFile(log);
            }

            // Write to system logs
            foreach (var kvp in systemLogsToWrite)
            {
                string systemName = kvp.Key;
                foreach (string log in kvp.Value)
                {
                    WriteToSystemFile(log, systemName);
                }
            }

            // Flush all writers
            if (logWriter != null) logWriter.Flush();
            foreach (var writer in systemLogWriters.Values)
            {
                writer?.Flush();
            }
        }

        void WriteToFile(string logEntry)
        {
            if (logWriter == null) return;

            try
            {
                logWriter.WriteLine(logEntry);

                // Check file size and rotate if necessary
                if (logWriter.BaseStream.Length > maxLogFileSize)
                {
                    RotateLogFile();
                }
            }
            catch (Exception e)
            {
                // Can't use DominionLogger.LogError here as it would create infinite loop
                Console.WriteLine($"Failed to write to log file: {e.Message}");
            }
        }

        void WriteToSystemFile(string logEntry, string systemName)
        {
            if (!systemLogWriters.ContainsKey(systemName))
            {
                InitializeSystemLog(systemName);
            }

            var writer = systemLogWriters[systemName];
            if (writer == null) return;

            try
            {
                writer.WriteLine(logEntry);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write to {systemName} log file: {e.Message}");
            }
        }

        void RotateLogFile()
        {
            try
            {
                logWriter.Close();

                // Archive current log
                string archivePath = logFilePath.Replace(".log", "_archived.log");
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
                File.Move(logFilePath, archivePath);

                // Create new log file
                logWriter = new StreamWriter(logFilePath, true, Encoding.UTF8);
                logWriter.AutoFlush = !useAsyncWrite;
                WriteHeader();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to rotate log file: {e.Message}");
            }
        }

        /// <summary>
        /// Direct logging method that bypasses Unity's logging system
        /// </summary>
        public void WriteLogDirect(string message, string systemName = null, LogType logType = LogType.Log)
        {
            WriteSystemLog(message, systemName, logType);
        }

        public void LogSeparator(string title = null)
        {
            if (string.IsNullOrEmpty(title))
            {
                WriteLogDirect("----------------------------------------");
            }
            else
            {
                WriteLogDirect($"---------- {title} ----------");
            }
        }

        public void LogSection(string sectionName)
        {
            WriteLogDirect($"\n========== {sectionName} ==========\n");
        }

        public string GetLogFilePath()
        {
            return logFilePath;
        }

        public void OpenLogFile()
        {
            if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
            {
                Application.OpenURL($"file:///{logFilePath}");
            }
        }

        public void ClearLogFile()
        {
            if (logWriter != null)
            {
                logWriter.Close();
                logWriter = null;
            }

            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }

            // Recreate log file
            logWriter = new StreamWriter(logFilePath, true, Encoding.UTF8);
            logWriter.AutoFlush = !useAsyncWrite;
            WriteHeader();
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                FlushPendingLogs();

                try
                {
                    // Close main log
                    if (logWriter != null)
                    {
                        logWriter.WriteLine("\n========== Session Ended ==========");
                        logWriter.Close();
                        logWriter = null;
                    }

                    // Close all system logs
                    foreach (var kvp in systemLogWriters)
                    {
                        var writer = kvp.Value;
                        if (writer != null)
                        {
                            writer.WriteLine("\n========== Session Ended ==========");
                            writer.Close();
                        }
                    }
                    systemLogWriters.Clear();
                    systemPendingLogs.Clear();
                }
                catch (Exception e)
                {
                    // Ignore cleanup errors during shutdown
                    Console.WriteLine($"FileLogger cleanup warning: {e.Message}");
                }

                instance = null;
                isInitialized = false;
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                FlushPendingLogs();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                FlushPendingLogs();
            }
        }

        void OnApplicationQuit()
        {
            isQuitting = true;
            FlushPendingLogs();

            if (logWriter != null)
            {
                logWriter.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Application quit");
                logWriter.Close();
            }
        }
    }

