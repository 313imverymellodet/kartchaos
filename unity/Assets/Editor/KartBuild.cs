using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

// Unity -batchmode -projectPath unity -executeMethod KartBuild.WebGL -quit
public static class KartBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("KART CHAOS/Setup Scene")]
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Scenes");
        if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/UnlitAlpha.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Sprites/Default")), "Assets/Resources/UnlitAlpha.mat");
        // keep the Standard shader in the build: the arena, boxes and rockets create Standard materials at runtime
        if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/StandardKeep.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), "Assets/Resources/StandardKeep.mat");
        // ...and its emission variant: boxes, rockets, coins and the crown glow, and an unused variant gets stripped
        var glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/StandardGlowKeep.mat");
        if (!glow) { glow = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(glow, "Assets/Resources/StandardGlowKeep.mat"); }
        glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(0.5f, 0.4f, 0.1f));
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(glow);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(143, 211, 255, 255);
        camGo.transform.position = new Vector3(0, 30, -20);
        camGo.transform.rotation = Quaternion.Euler(58, 0, 0);
        camGo.AddComponent<AudioListener>();
        new GameObject("Game").AddComponent<Game>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
    }

    // Development build with managed stack traces, into ../dist-dev (never deployed).
    public static void WebGLDev() { dev = true; WebGL(); }
    static bool dev;

    [MenuItem("KART CHAOS/Build WebGL")]
    public static void WebGL()
    {
        Setup();
        PlayerSettings.companyName = "KartChaos";
        PlayerSettings.productName = "Kart Chaos";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.runInBackground = true;   // online matches keep going when the window loses focus
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.WebGL.template = "PROJECT:KartChaos";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = dev ? WebGLExceptionSupport.FullWithStacktrace : WebGLExceptionSupport.None;
        PlayerSettings.WebGL.showDiagnostics = false;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

        var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, dev ? "../../dist-dev" : "../../dist"));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = dev ? BuildOptions.Development : BuildOptions.None,
        });
        Debug.Log("KART BUILD RESULT: " + report.summary.result + " size=" + report.summary.totalSize + " errors=" + report.summary.totalErrors);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
