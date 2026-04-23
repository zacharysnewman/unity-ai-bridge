using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Captures Unity console messages and appends them to a structured log file at
    /// Logs/unity-ai-bridge.log under the project root. The file is cleared on domain
    /// reload, Play Mode entry, and build start.
    /// query-logs reads this file instead of the OS Editor.log.
    /// </summary>
    [InitializeOnLoad]
    internal static class LogWriter
    {
        internal static readonly string LogPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Logs", "unity-ai-bridge.log"));

        static LogWriter()
        {
            EnsureLogDirectory();
            ClearLog(); // domain reload
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                ClearLog();
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            try
            {
                string typeTag = type switch
                {
                    LogType.Error     => "Error",
                    LogType.Warning   => "Warning",
                    LogType.Assert    => "Assert",
                    LogType.Exception => "Exception",
                    _                 => "Log",
                };

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string entry = string.IsNullOrEmpty(stackTrace)
                    ? $"[{timestamp}] [{typeTag}] {message}"
                    : $"[{timestamp}] [{typeTag}] {message}\n{stackTrace.TrimEnd()}";

                File.AppendAllText(LogPath, entry + "\n\n");
            }
            catch { }
        }

        internal static void ClearLog()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                File.WriteAllText(LogPath, $"[{timestamp}] [Log] [UnityAIBridge] Log cleared\n\n");
            }
            catch { }
        }

        private static void EnsureLogDirectory()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); }
            catch { }
        }
    }

    internal class LogWriterBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => LogWriter.ClearLog();
    }
}
