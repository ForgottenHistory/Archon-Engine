using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
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
        public string logFileName = "dominion_log.txt";

        [Header("Log Filters")]
        public bool logInfo = true;
        public bool logWarnings = true;
        public bool logErrors = true;
        public bool logExceptions = true;

        [Header("Performance")]
        public bool useAsyncWrite = true;
        public float flushInterval = 1f; // Flush to disk every second

        private string logFilePath;
        private string infoLogFilePath;
        private string warningLogFilePath;
        private StreamWriter logWriter;
        private StreamWriter infoLogWriter;
        private StreamWriter warningLogWriter;
        private Queue<string> pendingLogs = new Queue<string>();
        private Queue<string> pendingInfoLogs = new Queue<string>();
        private Queue<string> pendingWarningLogs = new Queue<string>();
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

            // Create logs directory inside Assets
            string logsDir = Path.Combine(Application.dataPath, "Logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Setup log file paths (overwrite existing for testing)
            logFilePath = Path.Combine(logsDir, logFileName);
            infoLogFilePath = Path.Combine(logsDir, "dominion_info.txt");
            warningLogFilePath = Path.Combine(logsDir, "dominion_warnings.txt");

            // Create or open log files (overwrite existing)
            try
            {
                logWriter = new StreamWriter(logFilePath, false, Encoding.UTF8); // false = overwrite
                logWriter.AutoFlush = !useAsyncWrite;

                infoLogWriter = new StreamWriter(infoLogFilePath, false, Encoding.UTF8);
                infoLogWriter.AutoFlush = !useAsyncWrite;

                warningLogWriter = new StreamWriter(warningLogFilePath, false, Encoding.UTF8);
                warningLogWriter.AutoFlush = !useAsyncWrite;

                WriteHeader();
                WriteInfoHeader();
                WriteWarningHeader();

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

        void WriteInfoHeader()
        {
            infoLogWriter.WriteLine("========================================");
            infoLogWriter.WriteLine($"Dominion Info Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            infoLogWriter.WriteLine($"Info messages only");
            infoLogWriter.WriteLine("========================================");
            infoLogWriter.WriteLine();
        }

        void WriteWarningHeader()
        {
            warningLogWriter.WriteLine("========================================");
            warningLogWriter.WriteLine($"Dominion Warnings Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            warningLogWriter.WriteLine($"Warnings and errors only");
            warningLogWriter.WriteLine("========================================");
            warningLogWriter.WriteLine();
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (!enableFileLogging || !isInitialized) return;

            // Filter based on log type
            bool shouldLog = false;
            switch (type)
            {
                case LogType.Log:
                    shouldLog = logInfo;
                    break;
                case LogType.Warning:
                    shouldLog = logWarnings;
                    break;
                case LogType.Error:
                    shouldLog = logErrors;
                    break;
                case LogType.Exception:
                    shouldLog = logExceptions;
                    break;
                case LogType.Assert:
                    shouldLog = logErrors;
                    break;
            }

            if (!shouldLog) return;

            // Format log entry
            StringBuilder sb = new StringBuilder();

            // Always include timestamps for better debugging
            sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] ");

            sb.Append($"[{type}] ");
            sb.Append(logString);

            if (includeStackTrace && !string.IsNullOrEmpty(stackTrace) &&
                (type == LogType.Error || type == LogType.Exception))
            {
                sb.AppendLine();
                sb.Append("Stack Trace:");
                sb.AppendLine();
                sb.Append(stackTrace);
            }

            string logEntry = sb.ToString();

            if (useAsyncWrite)
            {
                lock (logLock)
                {
                    pendingLogs.Enqueue(logEntry);

                    // Add to specific log queues
                    if (type == LogType.Log)
                    {
                        pendingInfoLogs.Enqueue(logEntry);
                    }
                    else if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    {
                        pendingWarningLogs.Enqueue(logEntry);
                    }
                }
            }
            else
            {
                WriteToFile(logEntry);
                WriteToSpecificFile(logEntry, type);
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
            if (pendingLogs.Count == 0 && pendingInfoLogs.Count == 0 && pendingWarningLogs.Count == 0) return;

            List<string> logsToWrite = new List<string>();
            List<string> infoLogsToWrite = new List<string>();
            List<string> warningLogsToWrite = new List<string>();

            lock (logLock)
            {
                while (pendingLogs.Count > 0)
                {
                    logsToWrite.Add(pendingLogs.Dequeue());
                }
                while (pendingInfoLogs.Count > 0)
                {
                    infoLogsToWrite.Add(pendingInfoLogs.Dequeue());
                }
                while (pendingWarningLogs.Count > 0)
                {
                    warningLogsToWrite.Add(pendingWarningLogs.Dequeue());
                }
            }

            // Write to main log
            foreach (string log in logsToWrite)
            {
                WriteToFile(log);
            }

            // Write to info log
            foreach (string log in infoLogsToWrite)
            {
                WriteToInfoFile(log);
            }

            // Write to warning log
            foreach (string log in warningLogsToWrite)
            {
                WriteToWarningFile(log);
            }

            // Flush all writers
            if (logWriter != null) logWriter.Flush();
            if (infoLogWriter != null) infoLogWriter.Flush();
            if (warningLogWriter != null) warningLogWriter.Flush();
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

        void WriteToInfoFile(string logEntry)
        {
            if (infoLogWriter == null) return;

            try
            {
                infoLogWriter.WriteLine(logEntry);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write to info log file: {e.Message}");
            }
        }

        void WriteToWarningFile(string logEntry)
        {
            if (warningLogWriter == null) return;

            try
            {
                warningLogWriter.WriteLine(logEntry);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write to warning log file: {e.Message}");
            }
        }

        void WriteToSpecificFile(string logEntry, LogType type)
        {
            if (type == LogType.Log)
            {
                WriteToInfoFile(logEntry);
            }
            else if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                WriteToWarningFile(logEntry);
            }
        }

        void RotateLogFile()
        {
            try
            {
                logWriter.Close();

                // Archive current log
                string archivePath = logFilePath.Replace(".txt", "_archived.txt");
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
        public void WriteLogDirect(string message, LogType logType = LogType.Log)
        {
            if (!enableFileLogging || !isInitialized) return;

            // Format log entry with timestamp
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] [{logType}] {message}";

            // Write to appropriate files
            WriteToFile(logEntry);
            if (logType == LogType.Log && logInfo) WriteToInfoFile(logEntry);
            if (logType == LogType.Warning && logWarnings) WriteToWarningFile(logEntry);
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
                Application.logMessageReceived -= HandleLog;
                FlushPendingLogs();

                try
                {
                    if (logWriter != null)
                    {
                        logWriter.WriteLine("\n========== Session Ended ==========");
                        logWriter.Close();
                        logWriter = null;
                    }
                    if (infoLogWriter != null)
                    {
                        infoLogWriter.WriteLine("\n========== Session Ended ==========");
                        infoLogWriter.Close();
                        infoLogWriter = null;
                    }
                    if (warningLogWriter != null)
                    {
                        warningLogWriter.WriteLine("\n========== Session Ended ==========");
                        warningLogWriter.Close();
                        warningLogWriter = null;
                    }
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

