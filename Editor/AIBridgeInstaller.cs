using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAIBridge.Editor
{
    /// <summary>
    /// Bootstraps Claude Code integration by copying the install skill into the
    /// project root's .claude/skills/ directory. Once installed, open Claude Code
    /// from the project root and run /install-unity-ai-bridge to complete setup.
    /// </summary>
    public static class AIBridgeInstaller
    {
        private const string SkillName = "install-unity-ai-bridge";
        private const string SkillFileName = "SKILL.md";

        [MenuItem("Unity AI Bridge/Setup Claude Code Integration")]
        public static void SetupClaudeCodeIntegration()
        {
            // Locate this package's directory via its assembly
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
            string sourceSkillPath = Path.Combine(
                packageRoot, ".claude", "skills", SkillName, SkillFileName);

            if (!File.Exists(sourceSkillPath))
            {
                EditorUtility.DisplayDialog(
                    "Unity AI Bridge",
                    $"Install skill not found at:\n{sourceSkillPath}",
                    "OK");
                return;
            }

            // Project root is the parent of Assets/
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string destDir = Path.Combine(
                projectRoot, ".claude", "skills", SkillName);
            string destPath = Path.Combine(destDir, SkillFileName);

            Directory.CreateDirectory(destDir);
            File.Copy(sourceSkillPath, destPath, overwrite: true);

            Debug.Log($"[UnityAIBridge] Install skill copied to {destPath}");

            EditorUtility.DisplayDialog(
                "Unity AI Bridge — Claude Code Integration",
                "Install skill is ready.\n\n" +
                "Open Claude Code from your Unity project root and run:\n\n" +
                "    /install-unity-ai-bridge\n\n" +
                "Claude will complete the setup, including skills, tools, and CLAUDE.md.",
                "OK");
        }
    }
}
