#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SkyTide.EditorTools
{
    /// <summary>
    /// Сборка Android-APK без участия человека (для CI).
    /// Вызывается через -executeMethod SkyTide.EditorTools.BuildScript.BuildAndroid.
    /// GameCI передаёт -customBuildPath и -customBuildName; по строке «Build succeeded!» подтверждает успех.
    /// Сцена в репозитории не хранится — собирается из кода (SceneBuilder) прямо перед сборкой.
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Sky Tide/Собрать Android APK")]
        public static void BuildAndroid()
        {
            try
            {
                string name = GetArg("-customBuildName");
                if (string.IsNullOrEmpty(name)) name = "SkyTide";
                string path = GetArg("-customBuildPath");
                if (string.IsNullOrEmpty(path)) path = "build/Android/" + name + ".apk";
                else if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, name + ".apk");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

                Debug.Log("SkyTide: сборка APK → " + path);

                SceneBuilder.Build();
                ConfigurePlayer();

                BuildPlayerOptions opts = new BuildPlayerOptions();
                opts.scenes = new string[] { SceneBuilder.ScenePath };
                opts.locationPathName = path;
                opts.target = BuildTarget.Android;
                opts.targetGroup = BuildTargetGroup.Android;
                opts.options = BuildOptions.None;

                BuildReport report = BuildPipeline.BuildPlayer(opts);
                BuildSummary sum = report.summary;
                Debug.Log("SkyTide: результат=" + sum.result + ", ошибок=" + sum.totalErrors + ", размер=" + sum.totalSize + " байт");

                if (sum.result == BuildResult.Succeeded)
                {
                    Debug.Log("Build succeeded!");
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError("SkyTide: сборка не удалась: " + sum.result);
                    if (Application.isBatchMode) EditorApplication.Exit(2);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("SkyTide: исключение в сборке: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "SkyTide";
            PlayerSettings.productName = "Небесный прилив";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.skytide.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // Горизонтальная ориентация
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            EditorUserBuildSettings.buildAppBundle = false;                       // APK, не AAB
            EditorUserBuildSettings.androidBuildType = AndroidBuildType.Release;  // подпись debug-ключом
            PlayerSettings.Android.useCustomKeystore = false;

            ForceLegacyInputManager();
        }

        /// <summary>Прототип читает касания через старый Input Manager — включаем его явно.</summary>
        static void ForceLegacyInputManager()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("SkyTide: ProjectSettings.asset не найден, ввод не переключён"); return; }
            SerializedObject so = new SerializedObject(assets[0]);
            SerializedProperty p = so.FindProperty("activeInputHandler");
            if (p == null) { Debug.LogWarning("SkyTide: свойство activeInputHandler не найдено"); return; }
            p.intValue = 0; // 0 = Input Manager (Old), 1 = Input System Package, 2 = Both
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("SkyTide: activeInputHandler = Input Manager (Old)");
        }

        static string GetArg(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
#endif
