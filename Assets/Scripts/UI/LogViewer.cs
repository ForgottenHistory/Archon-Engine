using UnityEngine;
using System.Collections.Generic;
using System.IO;
using ProvinceSystem.Utils;

namespace ProvinceSystem.UI
{
    /// <summary>
    /// In-game log viewer window
    /// </summary>
    public class LogViewer : MonoBehaviour
    {
        [Header("Display Settings")]
        public bool showLogViewer = false;
        public KeyCode toggleKey = KeyCode.F12;
        public int maxDisplayedLogs = 100;

        [Header("Window Settings")]
        public Rect windowRect = new Rect(20, 20, 600, 400);
        public string windowTitle = "Log Viewer";

        [Header("Style Settings")]
        public int fontSize = 12;
        public Color logColor = Color.white;
        public Color warningColor = Color.yellow;
        public Color errorColor = Color.red;

        private List<LogEntry> logEntries = new List<LogEntry>();
        private Vector2 scrollPosition;
        private bool autoScroll = true;
        private string filterText = "";
        private LogType filterType = LogType.Log | LogType.Warning | LogType.Error;

        private GUIStyle logStyle;
        private GUIStyle warningStyle;
        private GUIStyle errorStyle;
        private GUIStyle windowStyle;

        private FileLogger fileLogger;

        private class LogEntry
        {
            public string message;
            public string stackTrace;
            public LogType type;
            public float time;

            public LogEntry(string msg, string stack, LogType t)
            {
                message = msg;
                stackTrace = stack;
                type = t;
                time = Time.time;
            }
        }

        void Start()
        {
            // Subscribe to log messages
            Application.logMessageReceived += HandleLog;

            // Get FileLogger reference
            fileLogger = FileLogger.Instance;

            InitializeStyles();
        }

        void InitializeStyles()
        {
            logStyle = new GUIStyle();
            logStyle.normal.textColor = logColor;
            logStyle.fontSize = fontSize;
            logStyle.wordWrap = true;

            warningStyle = new GUIStyle();
            warningStyle.normal.textColor = warningColor;
            warningStyle.fontSize = fontSize;
            warningStyle.wordWrap = true;

            errorStyle = new GUIStyle();
            errorStyle.normal.textColor = errorColor;
            errorStyle.fontSize = fontSize;
            errorStyle.wordWrap = true;

            windowStyle = new GUIStyle("window");
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            logEntries.Add(new LogEntry(logString, stackTrace, type));

            // Limit log entries
            if (logEntries.Count > maxDisplayedLogs)
            {
                logEntries.RemoveAt(0);
            }

            // Auto scroll to bottom if enabled
            if (autoScroll)
            {
                scrollPosition.y = float.MaxValue;
            }
        }

        void Update()
        {
            // Toggle viewer with F12
            if (Input.GetKeyDown(toggleKey))
            {
                showLogViewer = !showLogViewer;
            }

            // Additional shortcuts when viewer is open
            if (showLogViewer)
            {
                // Clear logs with Ctrl+K
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.K))
                {
                    ClearLogs();
                }

                // Open log file with Ctrl+O
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.O))
                {
                    OpenLogFile();
                }
            }
        }

        void OnGUI()
        {
            if (!showLogViewer) return;

            // Ensure styles are initialized
            if (windowStyle == null)
            {
                InitializeStyles();
            }

            windowRect = GUI.Window(0, windowRect, DrawLogWindow, windowTitle, windowStyle);
        }

        void DrawLogWindow(int windowID)
        {
            GUILayout.BeginVertical();

            // Toolbar
            DrawToolbar();

            // Filter bar
            DrawFilterBar();

            // Log entries
            DrawLogEntries();

            // Status bar
            DrawStatusBar();

            GUILayout.EndVertical();

            // Make window draggable
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 20));
        }

        void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                ClearLogs();
            }

            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                SaveLogsToFile();
            }

            if (GUILayout.Button("Open Log", GUILayout.Width(80)))
            {
                OpenLogFile();
            }

            GUILayout.FlexibleSpace();

            autoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", GUILayout.Width(100));

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                showLogViewer = false;
            }

            GUILayout.EndHorizontal();
        }

        void DrawFilterBar()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Filter:", GUILayout.Width(40));
            filterText = GUILayout.TextField(filterText, GUILayout.Width(200));

            GUILayout.Space(20);

            bool showLogs = GUILayout.Toggle((filterType & LogType.Log) != 0, "Info");
            bool showWarnings = GUILayout.Toggle((filterType & LogType.Warning) != 0, "Warnings");
            bool showErrors = GUILayout.Toggle((filterType & LogType.Error) != 0, "Errors");

            filterType = 0;
            if (showLogs) filterType |= LogType.Log;
            if (showWarnings) filterType |= LogType.Warning;
            if (showErrors) filterType |= LogType.Error | LogType.Exception | LogType.Assert;

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }

        void DrawLogEntries()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            foreach (var entry in logEntries)
            {
                // Apply filters
                if (!ShouldShowEntry(entry)) continue;

                // Choose style based on log type
                GUIStyle style = logStyle;
                switch (entry.type)
                {
                    case LogType.Warning:
                        style = warningStyle;
                        break;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        style = errorStyle;
                        break;
                }

                // Format and display log entry
                string timeStr = $"[{entry.time:F2}]";
                string logText = $"{timeStr} {entry.message}";

                GUILayout.Label(logText, style);
            }

            GUILayout.EndScrollView();
        }

        void DrawStatusBar()
        {
            GUILayout.BeginHorizontal();

            int errorCount = 0;
            int warningCount = 0;
            int logCount = 0;

            foreach (var entry in logEntries)
            {
                switch (entry.type)
                {
                    case LogType.Log:
                        logCount++;
                        break;
                    case LogType.Warning:
                        warningCount++;
                        break;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        errorCount++;
                        break;
                }
            }

            GUILayout.Label($"Logs: {logCount}", GUILayout.Width(80));
            GUILayout.Label($"Warnings: {warningCount}", GUILayout.Width(100));
            GUILayout.Label($"Errors: {errorCount}", GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            if (fileLogger != null)
            {
                string logPath = fileLogger.GetLogFilePath();
                if (!string.IsNullOrEmpty(logPath))
                {
                    GUILayout.Label($"Log: {Path.GetFileName(logPath)}");
                }
            }

            GUILayout.EndHorizontal();
        }

        bool ShouldShowEntry(LogEntry entry)
        {
            // Type filter
            bool typeMatch = false;
            switch (entry.type)
            {
                case LogType.Log:
                    typeMatch = (filterType & LogType.Log) != 0;
                    break;
                case LogType.Warning:
                    typeMatch = (filterType & LogType.Warning) != 0;
                    break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    typeMatch = (filterType & LogType.Error) != 0;
                    break;
            }

            if (!typeMatch) return false;

            // Text filter
            if (!string.IsNullOrEmpty(filterText))
            {
                return entry.message.ToLower().Contains(filterText.ToLower());
            }

            return true;
        }

        void ClearLogs()
        {
            logEntries.Clear();
            Debug.Log("Log viewer cleared");
        }

        void SaveLogsToFile()
        {
            string path = Path.Combine(Application.dataPath, "..", "Logs", "viewer_export.txt");

            using (StreamWriter writer = new StreamWriter(path))
            {
                foreach (var entry in logEntries)
                {
                    writer.WriteLine($"[{entry.time:F2}] [{entry.type}] {entry.message}");
                    if (!string.IsNullOrEmpty(entry.stackTrace))
                    {
                        writer.WriteLine(entry.stackTrace);
                    }
                }
            }

            Debug.Log($"Logs exported to: {path}");
            Application.OpenURL($"file:///{path}");
        }

        void OpenLogFile()
        {
            if (fileLogger != null)
            {
                fileLogger.OpenLogFile();
            }
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
        }
    }
}