using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace ProvinceSystem.Utils
{
    /// <summary>
    /// File-based logging system that captures all Debug.Log messages
    /// </summary>
    public class FileLogger : MonoBehaviour
    {
        private static FileLogger instance;
        public static FileLogger Instance
        {
            get
            {
                if (instance == null)
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
        public bool includeTimestamps = true;
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
        private StreamWriter logWriter;
        private Queue<string> pendingLogs = new Queue<string>();
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

            // Create logs directory
            string logsDir = Path.Combine(Application.dataPath, "..", "Logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // Setup log file path with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string baseFileName = Path.GetFileNameWithoutExtension(logFileName);
            string extension = Path.GetExtension(logFileName);
            logFilePath = Path.Combine(logsDir, $"{baseFileName}_{timestamp}{extension}");

            // Create or open log file
            try
            {
                logWriter = new StreamWriter(logFilePath, true, Encoding.UTF8);
                logWriter.AutoFlush = !useAsyncWrite;

                WriteHeader();

                // Subscribe to Unity's log message received event
                Application.logMessageReceived += HandleLog;

                isInitialized = true;
                Debug.Log($"FileLogger initialized. Log file: {logFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize FileLogger: {e.Message}");
            }
        }

        void WriteHeader()
        {
            logWriter.WriteLine("========================================");
            logWriter.WriteLine($"Dominion Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logWriter.WriteLine($"Unity Version: {Application.unityVersion}");
            logWriter.WriteLine($"Platform: {Application.platform}");
            logWriter.WriteLine($"Map: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            logWriter.WriteLine("========================================");
            logWriter.WriteLine();
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

            if (includeTimestamps)
            {
                sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] ");
            }

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
                }
            }
            else
            {
                WriteToFile(logEntry);
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
            if (pendingLogs.Count == 0) return;

            List<string> logsToWrite = new List<string>();

            lock (logLock)
            {
                while (pendingLogs.Count > 0)
                {
                    logsToWrite.Add(pendingLogs.Dequeue());
                }
            }

            foreach (string log in logsToWrite)
            {
                WriteToFile(log);
            }

            if (logWriter != null)
            {
                logWriter.Flush();
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
                // Can't use Debug.LogError here as it would create infinite loop
                Console.WriteLine($"Failed to write to log file: {e.Message}");
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

        public void LogSeparator(string title = null)
        {
            if (string.IsNullOrEmpty(title))
            {
                Debug.Log("----------------------------------------");
            }
            else
            {
                Debug.Log($"---------- {title} ----------");
            }
        }

        public void LogSection(string sectionName)
        {
            Debug.Log($"\n========== {sectionName} ==========\n");
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

                if (logWriter != null)
                {
                    logWriter.WriteLine("\n========== Session Ended ==========");
                    logWriter.Close();
                }
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
            FlushPendingLogs();

            if (logWriter != null)
            {
                logWriter.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Application quit");
                logWriter.Close();
            }
        }
    }

    /// <summary>
    /// Static helper class for easy logging
    /// </summary>
    public static class DominionLogger
    {
        public static void Log(string message)
        {
            Debug.Log(message);
        }

        public static void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }

        public static void LogError(string message)
        {
            Debug.LogError(message);
        }

        public static void LogFormat(string format, params object[] args)
        {
            Debug.LogFormat(format, args);
        }

        public static void LogSection(string sectionName)
        {
            if (FileLogger.Instance != null)
                FileLogger.Instance.LogSection(sectionName);
        }

        public static void LogSeparator(string title = null)
        {
            if (FileLogger.Instance != null)
                FileLogger.Instance.LogSeparator(title);
        }
    }
}