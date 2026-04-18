using UnityEditor;
using UnityEngine;

public static class WebGLBuilder
{
    /// <summary>
    /// Called by GameCI in the GitHub Actions workflow.
    /// Configures WebGL settings for GitHub Pages (no server-side compression headers)
    /// then performs the build.
    /// </summary>
    public static void Build()
    {
        // GitHub Pages can't set Content-Encoding headers, so we need decompression fallback
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;

        // Use scenes from EditorBuildSettings so new scenes are included automatically
        var editorScenes = EditorBuildSettings.scenes;
        var scenes = new string[editorScenes.Length];
        for (int i = 0; i < editorScenes.Length; i++)
            scenes[i] = editorScenes[i].path;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError($"WebGL build failed: {report.summary.totalErrors} error(s)");
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log("WebGL build succeeded!");
        }
    }
}
