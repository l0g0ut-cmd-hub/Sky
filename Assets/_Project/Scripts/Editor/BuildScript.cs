#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine.Rendering;
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
                // Ошибки шейдеров не валят сборку Unity сами — ловим их и валим сборку (иначе розовый экран на телефоне).
                System.Collections.Generic.List<string> shaderErrors = new System.Collections.Generic.List<string>();
                Application.LogCallback watch = (msg, st, type) => { if (msg != null && msg.StartsWith("Shader error")) lock (shaderErrors) shaderErrors.Add(msg); };
                Application.logMessageReceivedThreaded += watch;

                SceneBuilder.Build();
                SceneShots.Capture(SceneBuilder.ScenePath);   // кадры для оценки картинки (build/shots)
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

                Application.logMessageReceivedThreaded -= watch;
                if (shaderErrors.Count > 0)
                {
                    Debug.LogError("SkyTide: ошибки шейдеров (" + shaderErrors.Count + "), первая: " + shaderErrors[0]);
                    if (Application.isBatchMode) EditorApplication.Exit(3);
                    return;
                }
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

        /// <summary>Значок игры: Art/AppIcon.png — как значок по умолчанию и для Android (обычный и круглый).</summary>
        static void SetAppIcon()
        {
            const string path = "Assets/_Project/Art/AppIcon.png";
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("SkyTide: значок не найден: " + path); return; }
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new Texture2D[] { icon }, IconKind.Any);
            foreach (PlatformIconKind kind in new PlatformIconKind[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round })
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                for (int i = 0; i < icons.Length; i++) icons[i].SetTexture(icon);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            Debug.Log("SkyTide: значок установлен");
        }

        static void ConfigurePlayer()
        {
            SetAppIcon();
            PlayerSettings.companyName = "SkyTide";
            PlayerSettings.productName = "Небесный прилив";
            int buildNo = 1;
            string bnPath = "Assets/_Project/Resources/BuildNumber.txt";
            if (System.IO.File.Exists(bnPath)) int.TryParse(System.IO.File.ReadAllText(bnPath).Trim(), out buildNo);
            PlayerSettings.bundleVersion = "0.1." + buildNo;
            PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, buildNo);

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.skytide.prototype");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.enableFrameTimingStats = true;   // время кадра видеокарты для отладочной панели
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
            EnableHdrOnAllTiers();
            // Факты о настройках сборки — в лог, чтобы решения по производительности опирались на них, а не на догадки.
            Debug.Log("SkyTide: настройки Android — API " + string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))
                + ", авто API " + PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android)
                + ", многопоточная отрисовка " + PlayerSettings.GetMobileMTRendering(NamedBuildTarget.Android)
                + ", graphics jobs " + PlayerSettings.graphicsJobs
                + ", цветовое пространство " + PlayerSettings.colorSpace);
        }

        /// <summary>HDR-камера нужна постобработке (блум, тонмаппинг) — включаем на всех уровнях графики Android.</summary>
        static void EnableHdrOnAllTiers()
        {
            foreach (GraphicsTier tier in new[] { GraphicsTier.Tier1, GraphicsTier.Tier2, GraphicsTier.Tier3 })
            {
                TierSettings ts = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Android, tier);
                ts.hdr = true;
                ts.hdrMode = CameraHDRMode.R11G11B10;
                EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.Android, tier, ts);
            }
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
