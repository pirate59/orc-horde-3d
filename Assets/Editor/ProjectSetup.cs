using OrcHordeFps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates the game scene and names the physics layers. Runs from the menu,
/// or headless: Unity -batchmode -executeMethod ProjectSetup.Run -quit
/// </summary>
[InitializeOnLoad]
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/QuarryRoad.unity";

    static readonly (int index, string name)[] LayerNames =
    {
        (Layers.Rock, "Rock"), (Layers.BuildGround, "BuildGround"), (Layers.Enemy, "Enemy"),
        (Layers.Tower, "Tower"), (Layers.Wall, "Wall"), (Layers.Player, "Player"), (Layers.Road, "Road"),
    };

    /// <summary>Opens the game scene when the project starts on an empty untitled one.</summary>
    static ProjectSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty && System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
        };
    }

    [MenuItem("Orc Horde/Rebuild Game Scene")]
    public static void Run()
    {
        NameLayers();
        IncludeRuntimeShaders();
        // A horde of skinned orcs is far cheaper skinned on the GPU.
        PlayerSettings.gpuSkinning = true;

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Bootstrap").AddComponent<Bootstrap>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] Scene written to " + ScenePath);
    }

    /// <summary>Builds a macOS player to Builds/, for the smoke test.</summary>
    public static void BuildMac()
    {
        Run();
        // The smoke test runs unattended, behind whatever window has focus.
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/OrcHordeFPS.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development,
        });
        Debug.Log("[ProjectSetup] Build " + report.summary.result + " in " + report.summary.totalTime);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// <summary>
    /// Builds the browser version into web/worker/public, for the Cloudflare Worker to serve. The
    /// output is committed: Cloudflare's build machines can't run Unity, so they only deploy it.
    /// Gzipped with a decompression fallback: the loader unpacks it itself, so it works from any
    /// static host without special Content-Encoding headers (Workers static assets doesn't set them).
    /// </summary>
    public static void BuildWeb()
    {
        Run();
        PlayerSettings.runInBackground = true;
        PlayerSettings.WebGL.template = "PROJECT:OrcHorde";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "web/worker/public",
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });
        Debug.Log("[ProjectSetup] Web build " + report.summary.result + " in " + report.summary.totalTime + ", " + report.summary.totalSize / (1024 * 1024) + " MB");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// <summary>Materials are made in code, so the shaders they use must ship in every build.</summary>
    static void IncludeRuntimeShaders()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var list = settings.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in new[] { "Standard", "Sprites/Default", "OrcHorde/VertexLit", "OrcHorde/TexturedVertexLit", "OrcHorde/Orc", "OrcHorde/Sky", "Hidden/OrcHorde/PostFx", "OrcHorde/Scatter" })
        {
            var shader = Shader.Find(name);
            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
            if (present) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }
        // The scatter is drawn instanced with materials made at runtime, which the build can't
        // see, so it would strip the instancing variants as unused. Keep them all.
        settings.FindProperty("m_InstancingStripping").intValue = 2;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static void NameLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        foreach (var (index, name) in LayerNames) layers.GetArrayElementAtIndex(index).stringValue = name;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }
}
