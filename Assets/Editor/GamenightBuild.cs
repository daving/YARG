using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class GamenightBuildEntry
{
    [InitializeOnLoadMethod]
    public static void BuildWindowsFromCommandLine()
    {
        if (!Environment.GetCommandLineArgs().Contains("-gamenightAutobuild"))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            try
            {
                Debug.Log("Starting Gamenight YARG Windows build from startup hook.");
                Editor.GamenightBuild.BuildWindows();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        };
    }

    public static void BuildWindows()
    {
        Debug.Log("Starting Gamenight YARG Windows build.");
        Editor.GamenightBuild.BuildWindows();
    }
}

namespace Editor
{
    public static class GamenightBuild
    {
        public static void BuildWindows()
        {
            var args = Environment.GetCommandLineArgs();
            var output = GetArg(args, "-gamenightOutput");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath("../YARG-Gamenight-build/YARG.exe");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var previousGraphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "YARG_TEST_BUILD" }
            });

            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, previousGraphicsApis);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Gamenight YARG build failed: {report.summary.result}");
            }
        }

        private static string GetArg(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
