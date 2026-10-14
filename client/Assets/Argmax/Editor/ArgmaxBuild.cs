using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Argmax.EditorTools
{
    /// <summary>
    /// Release builds. Nothing secret goes in: keys live in PlayerPrefs, the environment or .env.local, never in
    /// assets, and a release build only connects to servers the player chooses (localhost or a custom URI).
    ///   Unity -batchmode -quit -projectPath client -executeMethod Argmax.EditorTools.ArgmaxBuild.CI
    ///   env ARGMAX_TARGETS = mac,windows,linux,webgl (default: all)   ARGMAX_VERSION = 0.3.0
    /// </summary>
    public static class ArgmaxBuild
    {
        const string ScenePath = "Assets/Argmax/Scenes/Main.unity";
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "builds"));

        [MenuItem("ARGMAX/Build/All")] public static void All() { Mac(); Windows(); Linux(); WebGL(); }
        [MenuItem("ARGMAX/Build/macOS")] public static void Mac() => Build(BuildTarget.StandaloneOSX, "mac/ARGMAX.app");
        [MenuItem("ARGMAX/Build/Windows")] public static void Windows() => Build(BuildTarget.StandaloneWindows64, "windows/ARGMAX.exe");
        [MenuItem("ARGMAX/Build/Linux")] public static void Linux() => Build(BuildTarget.StandaloneLinux64, "linux/ARGMAX.x86_64");
        [MenuItem("ARGMAX/Build/WebGL")] public static void WebGL() => Build(BuildTarget.WebGL, "webgl");

        public static void CI()
        {
            var targets = (Environment.GetEnvironmentVariable("ARGMAX_TARGETS") ?? "mac,windows,linux,webgl").Split(',');
            bool ok = true;
            foreach (var t in targets)
            {
                switch (t.Trim().ToLowerInvariant())
                {
                    case "mac": ok &= Build(BuildTarget.StandaloneOSX, "mac/ARGMAX.app"); break;
                    case "windows": ok &= Build(BuildTarget.StandaloneWindows64, "windows/ARGMAX.exe"); break;
                    case "linux": ok &= Build(BuildTarget.StandaloneLinux64, "linux/ARGMAX.x86_64"); break;
                    case "webgl": ok &= Build(BuildTarget.WebGL, "webgl"); break;
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void Configure()
        {
            PlayerSettings.productName = "ARGMAX";
            PlayerSettings.companyName = "argmax";
            PlayerSettings.bundleVersion = Environment.GetEnvironmentVariable("ARGMAX_VERSION") ?? "0.3.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "io.github.waltergropius.argmax");
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true; // plain static hosting (GitHub Pages) can't set Content-Encoding
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:ARGMAX";
            SetMacUniversal();
        }

        /// <summary>Apple Silicon + Intel in one .app. The settings type moved between Unity versions, so by reflection.</summary>
        static void SetMacUniversal()
        {
            try
            {
                var t = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
                var p = t?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (p == null) { Debug.Log("ARGMAX BUILD: macOS architecture setting not found; using the default"); return; }
                p.SetValue(null, Enum.Parse(p.PropertyType, "x64ARM64"));
            }
            catch (Exception e) { Debug.Log("ARGMAX BUILD: could not set universal macOS build: " + e.Message); }
        }

        static bool Build(BuildTarget target, string rel)
        {
            if (!File.Exists(ScenePath)) ArgmaxTools.CreateScene();
            Configure();
            var path = Path.Combine(Root, rel);
            var dir = target == BuildTarget.WebGL ? path : Path.GetDirectoryName(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"ARGMAX BUILD {target}: {s.result}  {s.totalSize / 1048576.0:0.0} MB  {s.totalTime.TotalSeconds:0}s  errors {s.totalErrors}  -> {path}");
            if (s.result != BuildResult.Succeeded)
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) Debug.LogError("ARGMAX BUILD: " + m.content);
            return s.result == BuildResult.Succeeded;
        }
    }
}
