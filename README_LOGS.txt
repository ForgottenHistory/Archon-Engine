DOMINION - LOG FILE INFORMATION
================================

All game logs are automatically saved to text files for easy debugging and analysis.

LOG FILE LOCATION:
------------------
Logs are saved in: [Project Root]/Logs/
File format: dominion_log_YYYY-MM-DD_HH-mm-ss.txt

Example: D:\Stuff\My Games\Dominion\Logs\dominion_log_2024-01-15_14-30-45.txt

IN-GAME LOG VIEWER:
-------------------
Press F12 during gameplay to toggle the log viewer window.

Log Viewer Controls:
- F12: Toggle viewer on/off
- Ctrl+K: Clear displayed logs (doesn't affect file)
- Ctrl+O: Open current log file in default text editor

LOG FEATURES:
-------------
- Automatic file logging of all Debug.Log messages
- Timestamped entries for precise debugging
- Separate files for each session
- Automatic file rotation when size exceeds 10MB
- Async writing for better performance
- Stack traces for errors and exceptions

LOG TYPES CAPTURED:
-------------------
- Info messages (Debug.Log)
- Warnings (Debug.LogWarning)
- Errors (Debug.LogError)
- Exceptions
- Assertions

READING LOGS:
-------------
1. Open any text editor (Notepad, VS Code, etc.)
2. Navigate to the Logs folder
3. Open the desired log file
4. Use Ctrl+F to search for specific messages

LOG SECTIONS:
-------------
Major operations are marked with section headers like:
========== DOMINION INITIALIZATION ==========
---------- Province Generation Complete ----------

This makes it easy to find specific parts of the execution flow.

PERFORMANCE NOTES:
------------------
- Logs are buffered and written asynchronously
- Flush happens every 1 second or on application pause/quit
- File I/O doesn't impact game performance

TROUBLESHOOTING:
----------------
If logs aren't appearing:
1. Check that the Logs folder exists
2. Ensure FileLogger component is active
3. Verify write permissions for the Logs folder
4. Check Unity Console for any FileLogger errors

CONFIGURATION:
--------------
You can adjust logging settings on the FileLogger component:
- Enable/disable file logging
- Include/exclude timestamps
- Include/exclude stack traces
- Filter by log type
- Adjust max file size before rotation