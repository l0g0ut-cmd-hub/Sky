#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkyTide.EditorTools
{
    /// <summary>
    /// Собирает сцену прототипа из кода: объект SkyTide с SkyTideBootstrap, солнце, камеру,
    /// материалы-ассеты (чтобы шейдеры попали в сборку) и настройки неба/тумана.
    /// Меню: Sky Tide → Собрать сцену прототипа. При первом открытии проекта сцена собирается сама.
    /// Подхватывает ассеты, если они есть: модель корабля (Models/Corsair.*), небо и облака (Art/*).
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/SkyTide_Prototype.unity";
        const string MaterialFolder = "Assets/_Project/Materials";
        const string Art = "Assets/_Project/Art/";
        const string Models = "Assets/_Project/Models/";

        static SceneBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                if (System.IO.File.Exists(ScenePath)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Debug.Log("SkyTide: сцены прототипа нет — собираю.");
                Build();
                EditorSceneManager.OpenScene(ScenePath);
            };
        }

        [MenuItem("Sky Tide/Собрать сцену прототипа")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            EnsureFolder("Assets/_Project/Scenes");
            if (AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.DeleteAsset(MaterialFolder);
            EnsureFolder(MaterialFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameMaterials mats = new GameMaterials();
            ApplyArt(mats);
            mats.EnsureAll();
            foreach (FieldInfo f in typeof(GameMaterials).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(Material)) continue;
                Material m = (Material)f.GetValue(mats);
                if (m == null) continue;
                AssetDatabase.CreateAsset(m, MaterialFolder + "/" + f.Name + ".mat");
            }
            AssetDatabase.SaveAssets();

            GameObject boot = new GameObject("SkyTide");
            SkyTideBootstrap b = boot.AddComponent<SkyTideBootstrap>();
            b.materials = mats;
            b.shipMesh = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "Corsair.bytes");
            b.noiseTexture = Tex(Art + "Noise.png", false, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 256, true, true, true);
            Debug.Log("SkyTide: модель корабля " + (b.shipMesh != null && mats.shipBody != null ? "подключена" : "не найдена — будет заглушка"));

            Light sun = new GameObject("Sun").AddComponent<Light>();
            SkyTideBootstrap.ConfigureSun(sun);

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            SkyTideBootstrap.ConfigureCamera(cam);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 80f, -60f), Quaternion.Euler(15f, 0f, 0f));

            SkyTideBootstrap.ApplyEnvironment(mats);
            RenderSettings.sun = sun;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("SkyTide: сцена собрана → " + ScenePath);
        }

        /// <summary>Настраивает импорт текстур и делает из них материалы. Чего нет — остаётся пустым (запасные варианты).</summary>
        static void ApplyArt(GameMaterials mats)
        {
            Texture2D sky = Tex(Art + "SkyPanorama.png", false, false, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 2048, false);
            if (sky != null) mats.skybox = MaterialFactory.PanoramicSkybox(sky);

            Texture2D sea = Tex(Art + "CloudSea.jpg", false, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1024, true, true);
            if (sea != null) mats.cloudSea = MaterialFactory.CloudSea("CloudSea", sea, false);
            Texture2D layer = Tex(Art + "CloudLayer.png", false, true, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1024, true, true);
            if (layer != null) mats.cloudLayer = MaterialFactory.CloudSea("CloudLayer", layer, true);
            Texture2D sprites = Tex(Art + "CloudAtlas.png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 4096, true);
            if (sprites != null) mats.cloudSprite = MaterialFactory.CloudSprites(sprites);

            Texture2D albedo = Tex(Models + "Corsair_Albedo.jpg", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 4096, true);
            Texture2D normal = Tex(Models + "Corsair_Normal.png", true, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
            Texture2D emission = Tex(Models + "Corsair_Emission.png", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
            if (albedo != null) mats.shipBody = MaterialFactory.ShipBody(albedo, normal, emission);
        }

        static Texture2D Tex(string path, bool normalMap, bool alpha, TextureWrapMode wrapU, TextureWrapMode wrapV, int maxSize, bool mips, bool uncompressed = false, bool linear = false)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return null;
            ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normalMap && !linear;
            ti.textureCompression = uncompressed ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = false;   // цвет под прозрачностью уже залит в самих PNG (tools/*), заливка Unity даёт полосы
            ti.wrapModeU = wrapU;
            ti.wrapModeV = wrapV;
            ti.maxTextureSize = maxSize;
            ti.mipmapEnabled = mips;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
