using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    public static class AIBridgeInstaller
    {
        [MenuItem("Unity AI Bridge/Setup Claude Code Integration", priority = 1)]
        public static void SetupClaudeCodeIntegration()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                System.Reflection.Assembly.GetExecutingAssembly());

            if (packageInfo == null)
            {
                EditorUtility.DisplayDialog(
                    "Unity AI Bridge",
                    "Could not locate the Unity AI Bridge package directory.",
                    "OK");
                return;
            }

            string packageRoot = packageInfo.resolvedPath;
            string sourceDir = Path.Combine(packageRoot, "ClaudeIntegration");

            if (!Directory.Exists(sourceDir))
            {
                EditorUtility.DisplayDialog(
                    "Unity AI Bridge",
                    $"ClaudeIntegration directory not found at:\n{sourceDir}",
                    "OK");
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            CopyDirectory(sourceDir, projectRoot);
            MakeToolsExecutable(Path.Combine(projectRoot, "Tools"));

            Debug.Log($"[UnityAIBridge] Claude Code integration installed to {projectRoot}");

            EditorUtility.DisplayDialog(
                "Unity AI Bridge — Claude Code Integration",
                "Tools and skills are ready.\n\n" +
                "Open Claude Code from your Unity project root and run:\n\n" +
                "    /install-unity-ai-bridge\n\n" +
                "Claude will write the Unity AI Bridge documentation into your CLAUDE.md.",
                "OK");
        }

        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        static void MakeToolsExecutable(string toolsDir)
        {
            if (!Directory.Exists(toolsDir))
                return;

            foreach (string file in Directory.GetFiles(toolsDir))
            {
                if (!file.EndsWith(".meta"))
                    System.Diagnostics.Process.Start("chmod", $"+x \"{file}\"");
            }
        }
    }
}
