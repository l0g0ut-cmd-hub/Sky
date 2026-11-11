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
            SaveFrame(cam, boot, rt, tex, dir, s.name);
        }

        static void SaveFrame(Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, string name)
        {
            float started = Time.realtimeSinceStartup;
            cam.aspect = (float)rt.width / rt.height;
            // Добавленные крупные планы не меняют сдвиг марша у контрольных ракурсов 45.
            int phase = -1;
            switch (name)
            {
                case "30_vol_toward_sun": phase = 0; break;
                case "32_vol_side": phase = 1; break;
                case "36_vol_away_from_sun": phase = 2; break;
                case "37_vol_high": phase = 3; break;
                case "45_vol_muzzle_flash": phase = 4; break;
                case "47_vol_cloud_splashes": phase = 5; break;
                case "40_vol_broadside_side":
                case "62_smoke_legacy_reference": phase = 6; break;
                case "60_ship_materials": phase = 7; break;
                case "61_ship_backlight": phase = 8; break;
                case "63_muzzle_headon":
                case "64_muzzle_legacy_reference": phase = 9; break;
            }
            if (phase >= 0 && boot.Volumetric != null) boot.Volumetric.SetStaticCapturePhase(phase);
            if (boot.Fx != null) boot.Fx.PrepareForRender(cam);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(88));
            Debug.Log("SkyTide: кадр " + name + ", секунд=" + (Time.realtimeSinceStartup - started).ToString("F1"));
        }

        static void StepSalvo(Broadside guns, FxSystem fx, ref float time, float until)
        {
            while (time < until - 1e-5f)
            {
                float dt = Mathf.Min(1f / 60f, until - time);
                time += dt;
                guns.Pump(time);
                fx.Simulate(dt);
            }
        }

        static void RenderSplash(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir)
        {
            Vector3 oldPosition = cam.transform.position;
            Quaternion oldRotation = cam.transform.rotation;
            try
            {
                Vector3 focus = boot.Fx.LastCloudSplash + Vector3.up * 4f;
                Quaternion rotation = Quaternion.Euler(25f, 45f, 0f);
                cam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 72f, rotation);
                // OrbitCamera.target остаётся кораблём: чистый коридор не вырезает дыру вокруг всплеска.
                SaveFrame(cam, boot, rt, tex, dir, "47_vol_cloud_splashes");
            }
            finally { cam.transform.SetPositionAndRotation(oldPosition, oldRotation); }
        }

        static void CaptureBroadside(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, Broadside guns, bool full = true, bool environment = false)
        {
            Random.State random = Random.state;
            Random.InitState(74131);
            try
            {
                FxSystem fx = boot.Fx;
                fx.Clear();
                guns.Fire(-1);
                guns.Fire(1);
                float time = 0f;
                StepSalvo(guns, fx, ref time, 0.06f);
                Render(oc, cam, boot, rt, tex, dir, new Shot("45_vol_muzzle_flash", 90f, 6f, 58f));
                if (environment && !full)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("63_muzzle_headon", 145f, 6f, 52f));
                    fx.SetLegacyAppearanceCapture(true);
                    try { Render(oc, cam, boot, rt, tex, dir, new Shot("64_muzzle_legacy_reference", 145f, 6f, 52f)); }
                    finally { fx.SetLegacyAppearanceCapture(false); }
                }
                if (full)
                {
                    StepSalvo(guns, fx, ref time, 0.26f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("46_vol_ripple_flashes", 90f, 8f, 70f));
                }
                StepSalvo(guns, fx, ref time, 0.75f);
                Render(oc, cam, boot, rt, tex, dir, new Shot("40_vol_broadside_side", 90f, 8f, 70f));
                if (environment && !full)
                {
                    fx.SetLegacyAppearanceCapture(true);
                    try { Render(oc, cam, boot, rt, tex, dir, new Shot("62_smoke_legacy_reference", 90f, 8f, 70f)); }
                    finally { fx.SetLegacyAppearanceCapture(false); }
                }
                if (full)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("41_vol_broadside_chase", 10f, 16f, 75f));
                    boot.SetVolumetric(false);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("42_broadside_drawn", 90f, 8f, 70f));
                    boot.SetVolumetric(true);
                    StepSalvo(guns, fx, ref time, 3.8f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("43_vol_broadside_later", 60f, 16f, 140f));
                }
                StepSalvo(guns, fx, ref time, 4.15f);
                Debug.Log("SkyTide: залп → всплесков в облаках: " + fx.CloudSplashCount);
                if (fx.CloudSplashCount > 0) RenderSplash(oc, cam, boot, rt, tex, dir);
                else Debug.LogWarning("SkyTide: залп не дал всплесков — проверить траектории и поверхность облаков");
                if (boot.Target != null && !environment)
                {
                    fx.Clear();
                    oc.yaw = 60f; oc.pitch = 10f; oc.distance = 90f; oc.Snap(); oc.Apply(0f);
                    Vector3 look = boot.Ship.transform.position - cam.transform.position; look.y = 0f;
                    boot.Target.transform.position = boot.Ship.transform.position + look.normalized * 120f;
                    fx.Explosion(boot.Target.transform.position + Vector3.up * 8f, 1f);
                    fx.Simulate(0.035f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("44_vol_target_hit", 60f, 10f, 90f));
                    for (int k = 0; k < 24; k++) fx.Simulate(1f / 60f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("48_vol_hit_smoke_debris", 60f, 10f, 90f));
                }
            }
            finally
            {
                boot.Fx.Clear();
                Random.state = random;
            }
        }

        // Обычная сборка проверяет только текущую задачу. Полная галерея на llvmpipe занимала ~69 минут.
        // Разрешение/число кадров меняются только у съёмки в редакторе, качество Android-игры прежнее.
        static void CaptureTemporal(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            VolumetricClouds vol = boot.Volumetric;
            if (vol == null) return;
            RenderTexture rt = new RenderTexture(400, 190, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                vol.SetTemporalCapture(true);
                Render(oc, cam, boot, rt, tex, dir, new Shot("50_temporal_seed", 90f, 8f, 70f));
                if (vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history was not reset before capture");
                Render(oc, cam, boot, rt, tex, dir, new Shot("51_temporal_turn", 92f, 8f, 70f));
                if (!vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history was not used for a small camera turn");
                Render(oc, cam, boot, rt, tex, dir, new Shot("52_temporal_cut", 215f, 12f, 70f));
                if (vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history survived a camera cut");
                vol.SetTemporalCapture(true);
                Render(oc, cam, boot, rt, tex, dir, new Shot("53_temporal_cut_reference", 215f, 12f, 70f));
                Debug.Log("SkyTide: temporal — first/reset OK, small turn uses history, camera cut resets; actual cloud distance=" + vol.LastFrameUsedCloudDistance);
            }
            finally
            {
                vol.SetTemporalCapture(false);
                cam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        static void CaptureQuick(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            float start = Time.realtimeSinceStartup;
            RenderTexture rt = new RenderTexture(800, 380, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                boot.SetVolumetric(true);
                if (boot.Volumetric != null) boot.Volumetric.ResetTowers(boot.Ship.transform.position);
                // По умолчанию — свет с разных сторон и два эффекта; профиль fx оставляет прежние 6 фаз залпа.
                bool environment = System.Environment.GetEnvironmentVariable("SKYTIDE_SHOT_PROFILE") != "fx";
                if (environment) Render(oc, cam, boot, rt, tex, dir, new Shot("30_vol_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("32_vol_side", 90f, 8f, 70f));
                if (boot.Volumetric != null && boot.Volumetric.LastFrameUsedCloudDistance)
                    throw new System.InvalidOperationException("Static capture unnecessarily used cloud-distance MRT");
                Debug.Log("SkyTide: статический кадр — без MRT/истории, цвет через общий March");
                if (environment)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("36_vol_away_from_sun", 215f, 12f, 70f));
                    Render(oc, cam, boot, rt, tex, dir, new Shot("37_vol_high", 140f, 50f, 115f));
                }
                Broadside guns = boot.Ship != null ? boot.Ship.GetComponent<Broadside>() : null;
                if (guns != null && guns.HasCannons && boot.Fx != null)
                    CaptureBroadside(oc, cam, boot, rt, tex, dir, guns, false, environment);
                else Debug.LogWarning("SkyTide: пушек нет — залп не снят");
                if (environment)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("60_ship_materials", 215f, 12f, 40f));
                    Render(oc, cam, boot, rt, tex, dir, new Shot("61_ship_backlight", 90f, 10f, 42f));
                }
                // Четыре маленьких кадра, по одному Render на состояние: без дорогого многокадрового прогрева.
                if (environment) CaptureTemporal(oc, cam, boot, dir);
                Debug.Log("SkyTide: быстрая проверка " + (environment ? "визуала (12 x 800x380 + 4 x 400x190)" : "залпов (6 x 800x380)") + ", кадров="
                    + Directory.GetFiles(dir, "*.jpg").Length + ", секунд=" + (Time.realtimeSinceStartup - start).ToString("F1"));
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
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

                // Полная галерея — только по явному запросу, не тормозит каждый выпуск APK.
                if (System.Environment.GetEnvironmentVariable("SKYTIDE_FULL_SHOTS") != "1")
                {
                    CaptureQuick(oc, cam, boot, dir);
                    return;
                }

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
                // Объёмные облака (тестовый режим) — кадры для настройки.
                boot.SetVolumetric(true);
                if (vol != null) vol.ResetTowers(boot.Ship.transform.position);
                Render(oc, cam, boot, rt, tex, dir, new Shot("30_vol_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("31_vol_wide", 60f, 22f, 140f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("32_vol_side", 90f, 10f, 60f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("33_vol_low", -40f, -14f, 70f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("36_vol_away_from_sun", 215f, 12f, 70f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("37_vol_high", 140f, 50f, 115f));
                // Залп прокручивается по времени: факел → волна вспышек → дым → контакт с облаками.
                Broadside guns = boot.Ship != null ? boot.Ship.GetComponent<Broadside>() : null;
                if (guns != null && guns.HasCannons && boot.Fx != null)
                {
                    CaptureBroadside(oc, cam, boot, rt, tex, dir, guns);
                }
                else Debug.LogWarning("SkyTide: пушек нет — залп не снят");
                if (vol != null)
                {
                    Texture2D sheet = vol.ImpostorSheet();
                    if (sheet != null) File.WriteAllBytes(Path.Combine(dir, "93_dbg_impostors.jpg"), sheet.EncodeToJPG(90));
                    else Debug.LogWarning("SkyTide: листа импосторов нет");
                }
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
                Debug.Log("SkyTide: кадров снято: " + Directory.GetFiles(dir, "*.jpg").Length + " → " + dir + ", моделей: " + shipCount);
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
