#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

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

        static int textureReimports, textureReuses;

        public static void Build()
        {
            textureReimports = textureReuses = 0;
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
            TextAsset meshyT = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "Corsair.bytes");
            b.targetShip = meshyT != null && mats.shipBody != null ? new ShipVariant { name = "Мишень", mesh = meshyT, materials = new[] { mats.shipBody } } : null;
            b.postShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/PostFX.shader");
            b.volumetricShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/VolumetricClouds.shader");
            b.impostorShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/CloudImpostor.shader");
            b.fxShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Fx.shader");
            b.decalShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/HullDecal.shader");
            // Небесная крепость-причал: появится в сцене, как только в Models лежат SkyFortress.bytes и текстуры.
            b.fortressMesh = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "SkyFortress.bytes");
            b.fortressData = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "SkyFortress_data.json");
            if (b.fortressMesh != null)
            {
                Texture2D fa = Tex(Models + "SkyFortress_Albedo.jpg", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
                Texture2D fn = Tex(Models + "SkyFortress_Normal.png", true, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
                Texture2D fe = Tex(Models + "SkyFortress_Emission.png", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
                if (fa != null) b.fortressMaterials = new[] { MaterialFactory.ShipBody(fa, fn, fe) };
            }
            b.hudShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Hud.shader");
            b.hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            mats.skyReflection = BakeSkyReflection();
            SkyTideBootstrap.ApplyEnvironment(mats);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("SkyTide: импорт текстур — сохранён без повторного импорта=" + textureReuses + ", изменено=" + textureReimports);
            Debug.Log("SkyTide: сцена собрана → " + ScenePath);
        }

        static Cubemap BakeSkyReflection()
        {
            const string path = MaterialFolder + "/SunsetReflection.exr";
            GameObject go = new GameObject("Sky reflection bake");
            try
            {
                ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.resolution = Mathf.NextPowerOfTwo(Mathf.Max(256, RenderSettings.defaultReflectionResolution));
                probe.hdr = true;
                probe.cullingMask = 0; // только небо; без корабля, чтобы он не отражался сам в себе
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probe.size = Vector3.one;
                float started = Time.realtimeSinceStartup;
                if (!Lightmapping.BakeReflectionProbe(probe, path))
                    throw new System.InvalidOperationException("Sunset reflection bake failed");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cube == null || cube.mipmapCount < 2)
                    throw new System.InvalidOperationException("Sunset reflection is missing its roughness mips");
                Debug.Log("SkyTide: отражение заката " + cube.width + "px, mips=" + cube.mipmapCount
                    + ", секунд=" + (Time.realtimeSinceStartup - started).ToString("F1"));
                return cube;
            }
            finally { Object.DestroyImmediate(go); }
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
            // Звук: моно, Vorbis; длинные — сжатыми в памяти.
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Resources/Audio" }))
            {
                string ap = AssetDatabase.GUIDToAssetPath(guid);
                AudioImporter ai = AssetImporter.GetAtPath(ap) as AudioImporter;
                if (ai == null) continue;
                AudioImporterSampleSettings ss = ai.defaultSampleSettings;
                bool longClip = ap.Contains("ship_destroyed") || ap.Contains("sail_loop");
                ss.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                ss.compressionFormat = AudioCompressionFormat.Vorbis; ss.quality = 0.7f;
                ai.defaultSampleSettings = ss; ai.forceToMono = !ap.Contains("sail_loop"); ai.loadInBackground = false;
                ai.SaveAndReimport();
            }
            // Интерфейс: набор HUD (с альфой), главное меню, фон меню, заставка — без сжатия и мипов.
            Tex("Assets/_Project/Resources/UI/hud_kit.png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, false, true);
            Tex("Assets/_Project/Resources/UI/aim_kit.png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true, true);
            foreach (string rp in new[] { "radar_frame", "radar_face", "radar_sweep", "radar_ship", "radar_enemy", "radar_enemy_far" })
                Tex("Assets/_Project/Resources/UI/" + rp + ".png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1024, true, true);
            Tex("Assets/_Project/Resources/UI/main_menu.jpg", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, false, true);
            Tex("Assets/_Project/Resources/UI/splash_studio.jpg", false, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, false, true);
            // Баннеры победы/поражения, плашка оповещения, виньетка урона, иконки 4×4 — с альфой, без сжатия.
            foreach (string bp in new[] { "banner_victory", "banner_defeat", "plate_notice", "vignette_damage", "icons" })
                Tex("Assets/_Project/Resources/UI/" + bp + ".png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, bp == "icons", true);
            // Эффекты: флипбуки (взрывы, вспышка, дым, всплеск, огонь, клубы) и наклейки урона корпуса — сжатые, с мипами.
            Tex("Assets/_Project/Resources/FX/fx_flipbooks.png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 4096, true);
            Tex("Assets/_Project/Resources/FX/hull_decals.png", false, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true);
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
            if (repaired != null)
            {
                repaired.wind = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "CorsairRepaired_wind.json");
                if (repaired.wind == null) throw new System.InvalidOperationException("Corsair cloth wind map is missing");
                repaired.cannons = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "CorsairRepaired_cannons.json");
                repaired.recoil = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "CorsairRepaired_recoil.json");
                if (repaired.recoil == null) throw new System.InvalidOperationException("Corsair cannon recoil map is missing");
                repaired.crew=BuildCrewModel();
                repaired.steering=AssetDatabase.LoadAssetAtPath<TextAsset>(Models+"CorsairRepaired_steering.json");
                if(repaired.steering==null)throw new System.InvalidOperationException("Steering sail map is missing");
                repaired.upperGun=AssetDatabase.LoadAssetAtPath<TextAsset>(Models+"Crew/UpperGunBarrel.bytes");
                if(repaired.upperGun==null)throw new System.InvalidOperationException("Upper gun geometry is missing");
                list.Add(repaired);
            }
            TextAsset meshy = AssetDatabase.LoadAssetAtPath<TextAsset>(Models + "Corsair.bytes");
            // Модель Meshy — больше не в выборе: это корабль-мишень (SkyTideBootstrap.targetShip).
            return list.ToArray();
        }

        static CrewModelData BuildCrewModel()
        {
            const string folder=Models+"Crew/";
            Material material=MaterialFactory.Opaque("Deckhand PBR",Color.white,.4f);
            material.mainTexture=Tex(folder+"Deckhand_Albedo.jpg",false,false,TextureWrapMode.Clamp,TextureWrapMode.Clamp,2048,true);
            material.SetTexture("_BumpMap",Tex(folder+"Deckhand_Normal.jpg",true,false,TextureWrapMode.Clamp,TextureWrapMode.Clamp,2048,true));
            material.SetFloat("_BumpScale",1f);material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic",1f);material.SetFloat("_GlossMapScale",1f);
            material.SetTexture("_MetallicGlossMap",Tex(folder+"Deckhand_MetalSmooth.png",false,true,TextureWrapMode.Clamp,TextureWrapMode.Clamp,2048,true,false,true));
            material.EnableKeyword("_METALLICGLOSSMAP");AssetDatabase.CreateAsset(material,MaterialFolder+"/Deckhand.mat");
            CrewModelData result=new CrewModelData{material=material,geometry=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"Deckhand.bytes"),
                skeleton=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"Deckhand_rig.json"),
                helmGeometry=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"Deckhand_HelmGrip.bytes"),
                helmPose=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"Deckhand_HelmGrip.json"),
                captainAlbedo=Tex(folder+"Captain_Albedo.jpg",false,false,TextureWrapMode.Clamp,TextureWrapMode.Clamp,2048,true)};
            if(!result.IsValid||material.mainTexture==null||material.GetTexture("_BumpMap")==null||material.GetTexture("_MetallicGlossMap")==null)
                throw new System.InvalidOperationException("Crew model/PBR assets are missing");
            return result;
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
                bool cloth = j.name == "Weathered sewn linen" || j.name == "Old linen repair patches" || j.name == "Oxblood pennant";
                float transmission = j.name == "Old linen repair patches" ? 0.07f : (j.name == "Oxblood pennant" ? 0.12f : 0.2f);
                Material m = cloth ? MaterialFactory.Cloth(file + "_" + i, c, Mathf.Clamp01(j.smoothness), transmission)
                    : MaterialFactory.Opaque(file + "_" + i, c, Mathf.Clamp01(j.smoothness));
                if (j.name == "Black forged iron")
                {
                    Shader iron = Shader.Find("SkyTide/ShipIron");
                    if (iron == null) throw new System.InvalidOperationException("ShipIron shader is missing");
                    m.shader = iron;
                }
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
            Debug.Log("SkyTide: материалы корабля — дерево/металл PBR, ткань двухсторонняя с просветом; слотов=" + result.Length);
            return new ShipVariant { name = title, mesh = mesh, materials = result };
        }

        static Texture2D Tex(string path, bool normalMap, bool alpha, TextureWrapMode wrapU, TextureWrapMode wrapV, int maxSize, bool mips, bool uncompressed = false, bool linear = false)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return null;
            bool model = path.StartsWith(Models, System.StringComparison.Ordinal);
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterCompression compression = uncompressed ? TextureImporterCompression.Uncompressed
                : model ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            TextureImporterAlphaSource source = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            int aniso = mips ? (model ? 8 : 4) : 0;
            FilterMode filter = model ? (mips ? FilterMode.Trilinear : FilterMode.Bilinear) : ti.filterMode;
            bool readable = path == Art + "Noise.png" || ti.isReadable;
            bool changed = ti.textureType != type || ti.sRGBTexture != (!normalMap && !linear)
                || ti.textureCompression != compression || ti.alphaSource != source || ti.alphaIsTransparency
                || ti.wrapModeU != wrapU || ti.wrapModeV != wrapV || ti.maxTextureSize != maxSize
                || ti.mipmapEnabled != mips || ti.anisoLevel != aniso || ti.filterMode != filter || ti.isReadable != readable
                || (model && ti.compressionQuality != 100);
            if (changed)
            {
                ti.textureType = type; ti.sRGBTexture = !normalMap && !linear;
                ti.textureCompression = compression; ti.alphaSource = source; ti.alphaIsTransparency = false;
                ti.wrapModeU = wrapU; ti.wrapModeV = wrapV; ti.maxTextureSize = maxSize; ti.mipmapEnabled = mips;
                ti.isReadable = readable; ti.anisoLevel = aniso; ti.filterMode = filter;
                if (model) ti.compressionQuality = 100;
                ti.SaveAndReimport();
                textureReimports++;
            }
            else textureReuses++;
            // Интерфейс: в редакторе без сжатия, на Android — ASTC 4×4 (8 бит/пиксель, на глаз без потерь; APK < 100 МБ).
            if (path.Contains("/Resources/UI/"))
            {
                TextureImporterPlatformSettings android = ti.GetPlatformTextureSettings("Android");
                if (!android.overridden || android.format != TextureImporterFormat.ASTC_4x4 || android.maxTextureSize != maxSize)
                {
                    android.overridden = true; android.format = TextureImporterFormat.ASTC_4x4; android.maxTextureSize = maxSize;
                    android.compressionQuality = 100;
                    ti.SetPlatformTextureSettings(android);
                    ti.SaveAndReimport();
                }
            }
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
