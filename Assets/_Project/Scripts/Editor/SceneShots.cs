#if UNITY_EDITOR
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide.EditorTools
{
    /// <summary>
    /// Кадры сцены из CI (build/shots/*.jpg → ветка apk-latest): корабль и небо с разных ракурсов.
    /// Нужны, чтобы оценивать картинку без телефона. Мир собирается тем же кодом, что и в игре (SkyTideBootstrap.Init),
    /// но вне Play Mode; после съёмки сцена перезагружается с диска, в сборку ничего не попадает.
    /// Без видеокарты (Null device) кадров нет — только строка в логе.
    /// </summary>
    public static class SceneShots
    {
        struct Shot
        {
            public string name; public float yaw, pitch, dist;
            public Shot(string n, float y, float p, float d) { name = n; yaw = y; pitch = p; dist = d; }
        }

        static void Render(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, Shot s)
        {
            oc.yaw = s.yaw; oc.pitch = s.pitch; oc.distance = s.dist;
            oc.Snap();
            oc.Apply(0f);
            boot.World.Step(cam.transform.position, 0f);
            cam.aspect = (float)rt.width / rt.height;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, s.name + ".jpg"), tex.EncodeToJPG(88));
        }

        public static void Capture(string scenePath)
        {
            Debug.Log("SkyTide: кадры — графика " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Debug.Log("SkyTide: нет графического устройства — кадров не будет"); return; }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../build/shots"));
            Directory.CreateDirectory(dir);
            try
            {
                EditorSceneManager.OpenScene(scenePath);
                SkyTideBootstrap boot = Object.FindFirstObjectByType<SkyTideBootstrap>();
                if (boot == null) { Debug.LogWarning("SkyTide: нет SkyTideBootstrap в сцене"); return; }
                boot.Init();
                OrbitCamera oc = boot.Cam;
                Camera cam = oc.GetComponent<Camera>();
                QualitySettings.shadowDistance = 160f;

                // Курс корабля на старте 55°, солнце — курс 90°. yaw камеры считается от курса корабля.
                Shot[] shots =
                {
                    new Shot("01_chase", 0f, 14f, 62f),
                    new Shot("02_toward_sun", 35f, 8f, 75f),
                    new Shot("03_side", 90f, 10f, 60f),
                    new Shot("04_away_from_sun", 215f, 12f, 70f),
                    new Shot("05_high", 140f, 50f, 115f),
                    new Shot("06_low", -40f, -14f, 70f),
                    new Shot("07_close_stern", 160f, 6f, 38f),
                    new Shot("08_wide", 60f, 22f, 140f),
                };

                VolumetricClouds vol = cam.GetComponent<VolumetricClouds>();
                if (vol != null) Debug.Log("SkyTide: облака — " + vol.Probe());

                RenderTexture rt = new RenderTexture(1600, 760, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                Shot[] debugShots =
                {
                    new Shot("90_dbg_fields", 35f, 8f, 75f),
                    new Shot("91_dbg_clouds", 35f, 8f, 75f),
                    new Shot("92_dbg_trans", 35f, 8f, 75f),
                };
                Shot[] shipShots =
                {
                    new Shot("20_ship2_chase", 20f, 12f, 62f),
                    new Shot("21_ship2_side", 90f, 8f, 58f),
                    new Shot("22_ship2_stern_close", 165f, 6f, 40f),
                };
                int idx = 0;
                Shot[] all = new Shot[shots.Length + debugShots.Length];
                shots.CopyTo(all, 0);
                debugShots.CopyTo(all, shots.Length);
                foreach (Shot s in all)
                {
                    if (vol != null) vol.debugView = idx >= shots.Length ? idx - shots.Length + 1 : 0;
                    boot.SetVolumetric(idx >= shots.Length);   // обычные кадры — как в игре по умолчанию, отладка — с объёмом
                    idx++;
                    oc.yaw = s.yaw; oc.pitch = s.pitch; oc.distance = s.dist;
                    oc.Snap();
                    oc.Apply(0f);
                    boot.World.Step(cam.transform.position, 0f);
                    cam.aspect = (float)rt.width / rt.height;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, s.name + ".jpg"), tex.EncodeToJPG(88));
                }
                if (vol != null) vol.debugView = 0;
                // Объёмные облака (тестовый режим) — два кадра для настройки.
                boot.SetVolumetric(true);
                Render(oc, cam, boot, rt, tex, dir, new Shot("30_vol_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("31_vol_wide", 60f, 22f, 140f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("32_vol_side", 90f, 10f, 60f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("33_vol_low", -40f, -14f, 70f));
                // Те же облака без постобработки (настройка формы и света до ACES/блума).
                SkyTidePost post = cam.GetComponent<SkyTidePost>();
                if (post != null) post.enabled = false;
                Render(oc, cam, boot, rt, tex, dir, new Shot("34_vol_nopost_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("35_vol_nopost_wide", 60f, 22f, 140f));
                if (post != null) post.enabled = true;
                boot.SetVolumetric(false);

                int shipCount = boot.ships != null ? boot.ships.Length : 0;
                if (shipCount > 1)
                {
                    boot.NextShip();
                    foreach (Shot s in shipShots) Render(oc, cam, boot, rt, tex, dir, s);
                }
                Debug.Log("SkyTide: кадров снято: " + (all.Length + (shipCount > 1 ? shipShots.Length : 0)) + " → " + dir + ", моделей: " + shipCount);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SkyTide: кадры не сняты: " + e);
            }
            finally
            {
                // Сцену с диска — без объектов, созданных для съёмки.
                EditorSceneManager.OpenScene(scenePath);
            }
        }
    }
}
#endif
