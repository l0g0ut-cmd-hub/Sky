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
            // Линейное пространство цвета — основа для HDR, блума и тонмаппинга.
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
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
            b.ships = BuildShipVariants(mats);
            b.postShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/PostFX.shader");
            b.volumetricShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/VolumetricClouds.shader");
            b.impostorShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/CloudImpostor.shader");
            b.cloudNoise3D = AssetDatabase.LoadAssetAtPath<TextAsset>(Art + "CloudNoise3D.bytes");
            // Шаблоны объёмных башен со светом под неподвижное солнце — запекаем здесь, телефон при старте не считает.
            Texture3D towerField = VolumetricClouds.BakeField(VolumetricClouds.SunTowardDefault());
            AssetDatabase.CreateAsset(towerField, MaterialFolder + "/TowerField.asset");
            b.towerField = towerField;
            b.noiseTexture = Tex(Art + "Noise.png", false, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 256, true, true, true);
            Debug.Log("SkyTide: моделей корабля: " + b.ships.Length);

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
            Texture2D sky = Tex(Art + "SkyPanorama.jpg", false, false, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 8192, false);
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

        [System.Serializable]
        class MatJson
        {
            public string name;
            public float[] color;
            public float metallic;
            public float smoothness;
            public string albedo;
            public string normal;
            public float normalScale = 1f;
            public string metalSmooth;
            public string emission;
            public float[] emissionColor;
            public float emissionIntensity;
        }

        [System.Serializable]
        class MatList
        {
            public MatJson[] materials;
        }

        /// <summary>Модели для переключателя: новые многоматериальные (Models/*.json рядом с .bytes) и Meshy-версия.</summary>
        static ShipVariant[] BuildShipVariants(GameMaterials mats)
        {
            var list = new System.Collections.Generic.List<ShipVariant>();
            ShipVariant repaired = SceneVariant("CorsairRepaired", "Корсар (Blender)");
            if (repaired != null) list.Add(repaired);
            TextAsset meshy = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "Corsair.bytes");
            if (meshy != null && mats.shipBody != null)
                list.Add(new ShipVariant { name = "Корсар (Meshy)", mesh = meshy, materials = new[] { mats.shipBody } });
            return list.ToArray();
        }

        static ShipVariant SceneVariant(string file, string title)
        {
            TextAsset mesh = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + file + ".bytes");
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + file + ".json");
            if (mesh == null || json == null) return null;
            MatList ml = JsonUtility.FromJson<MatList>(json.text);
            Material[] result = new Material[ml.materials.Length];
            for (int i = 0; i < result.Length; i++)
            {
                MatJson j = ml.materials[i];
                Color c = j.color != null && j.color.Length >= 3 ? new Color(j.color[0], j.color[1], j.color[2]).gamma : Color.white;
                Material m = MaterialFactory.Opaque(file + "_" + i, c, Mathf.Clamp01(j.smoothness));
                m.SetFloat("_Metallic", Mathf.Clamp01(j.metallic));
                if (!string.IsNullOrEmpty(j.albedo)) m.mainTexture = Tex(Models + j.albedo, false, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 4096, true);
                if (!string.IsNullOrEmpty(j.normal))
                {
                    m.SetTexture("_BumpMap", Tex(Models + j.normal, true, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2048, true));
                    m.SetFloat("_BumpScale", j.normalScale > 0f ? j.normalScale : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (!string.IsNullOrEmpty(j.metalSmooth))
                {
                    m.SetTexture("_MetallicGlossMap", Tex(Models + j.metalSmooth, false, true, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2048, true, false, true));
                    m.SetFloat("_GlossMapScale", 1f);
                    m.EnableKeyword("_METALLICGLOSSMAP");
                }
                if (!string.IsNullOrEmpty(j.emission))
                {
                    MaterialFactory.WithEmission(m, new Color(1.6f, 1.6f, 1.6f));
                    m.SetTexture("_EmissionMap", Tex(Models + j.emission, false, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2048, true));
                }
                else if (j.emissionColor != null && j.emissionColor.Length >= 3)
                {
                    Color e = new Color(j.emissionColor[0], j.emissionColor[1], j.emissionColor[2]).gamma * Mathf.Max(1f, j.emissionIntensity);
                    MaterialFactory.WithEmission(m, e);
                }
                AssetDatabase.CreateAsset(m, MaterialFolder + "/" + file + "_" + i + ".mat");
                result[i] = m;
            }
            return new ShipVariant { name = title, mesh = mesh, materials = result };
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
            // Анизотропия без мипов бессмысленна и на шве панорамы (скачок производных) рисует пунктир.
            ti.anisoLevel = mips ? 4 : 0;
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
