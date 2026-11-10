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
    /// Модель корабля подхватывается из Assets/_Project/Models/Ship.fbx, если файл есть.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/SkyTide_Prototype.unity";
        const string MaterialFolder = "Assets/_Project/Materials";
        const string ModelPath = "Assets/_Project/Models/Ship.fbx";

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
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model != null)
            {
                b.shipModel = model;
                Debug.Log("SkyTide: модель корабля подключена: " + ModelPath);
            }

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
