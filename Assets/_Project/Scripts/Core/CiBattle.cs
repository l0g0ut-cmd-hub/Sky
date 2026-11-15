using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Прогон боя в CI (automated playtest / soak test): Linux-сборка игры запускается на сервере с ключом -skysim.
    /// Меню убирается, включается автобой, время идёт шагом 1/20 с независимо от скорости машины (Time.captureDeltaTime),
    /// камера рисует только для снимков. Каждые 30 с игрового времени — строка «SIM|» в лог; снимки раз в 40 с и на
    /// потопления; в конце — полный отчёт, проверки и код выхода (0 — бой прошёл здраво, 2 — есть провалы).
    /// Ключи: -simSeconds N (по умолчанию 240), -simOut папка.
    /// </summary>
    public class CiBattle : MonoBehaviour
    {
        static int active = -1;
        public static bool Active
        {
            get
            {
                if (active < 0) active = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-skysim") >= 0 ? 1 : 0;
                return active == 1;
            }
        }

        static string Arg(string key, string def)
        {
            string[] a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return def;
        }

        string outDir;
        float seconds;
        int shot;
        readonly StringBuilder fails = new StringBuilder();
        Vector3 lastPos; float stuckFor, maxStuck;

        IEnumerator Start()
        {
            seconds = float.Parse(Arg("-simSeconds", "240"), System.Globalization.CultureInfo.InvariantCulture);
            outDir = Arg("-simOut", Path.Combine(Application.persistentDataPath, "sim"));
            Directory.CreateDirectory(outDir);
            Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            Time.captureDeltaTime = 1f / 20f;
            Log("старт: " + seconds + " с боя, шаг 1/20 с, графика " + SystemInfo.graphicsDeviceType + ", сборка " + DebugReport.BuildNo);
            yield return null; yield return null;
            foreach (MainMenu m in FindObjectsByType<MainMenu>(FindObjectsSortMode.None)) Destroy(m.gameObject);
            foreach (StudioSplash s in FindObjectsByType<StudioSplash>(FindObjectsSortMode.None)) Destroy(s.gameObject);
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
            if (tc != null) tc.SetAutoBattle(true); else fails.AppendLine("нет управления/автобоя");
            Camera cam = g != null && g.Cam != null ? g.Cam.GetComponent<Camera>() : Camera.main;
            if (cam != null) cam.enabled = false;   // рисуем только снимки — прогон в разы быстрее
            if (g != null && g.Ship != null) lastPos = g.Ship.transform.position;

            float nextLog = 30f, nextShot = 5f; int kills = 0;
            while (Time.timeSinceLevelLoad < seconds)
            {
                yield return null;
                float t = Time.timeSinceLevelLoad;
                if (g != null && g.Ship != null)
                {
                    Vector3 p = g.Ship.transform.position;
                    stuckFor = (p - lastPos).magnitude < 0.05f ? stuckFor + Time.deltaTime : 0f;
                    maxStuck = Mathf.Max(maxStuck, stuckFor); lastPos = p;
                }
                int k = DebugReport.CountOf("врагов потоплено");
                if (k > kills) { kills = k; StartCoroutine(ShotLater(cam, "kill" + k, 1.2f)); }
                if (t >= nextShot) { nextShot += 40f; Shot(cam, "t" + Mathf.RoundToInt(t)); }
                if (t >= nextLog) { nextLog += 30f; Log(DebugReport.AliveLine().Trim() + " · убито " + kills + " · " + Accuracy()); }
            }
            Finish(cam);
        }

        IEnumerator ShotLater(Camera cam, string n, float delay)
        {
            float until = Time.timeSinceLevelLoad + delay;
            while (Time.timeSinceLevelLoad < until) yield return null;
            Shot(cam, n);
        }

        void Shot(Camera cam, string name)
        {
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            RenderTexture rt = RenderTexture.GetTemporary(960, 456, 24);
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(960, 456, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 456), 0, 0); tex.Apply();
            cam.targetTexture = prevT; RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(outDir, "sim_" + (shot++).ToString("00") + "_" + name + ".jpg"), tex.EncodeToJPG(80));
            Destroy(tex);
        }

        static string Accuracy()
        {
            int s = DebugReport.CountOf("залпов флагмана") + DebugReport.CountOf("залпов флагмана вне прицела"), h = DebugReport.CountOf("попаданий флагмана");
            return "залпов флагмана " + s + ", попаданий " + h;
        }

        void Finish(Camera cam)
        {
            Shot(cam, "end");
            // Проверки здравости боя.
            int errors = DebugReport.ErrorCount;
            int kills = DebugReport.CountOf("врагов потоплено"), deaths = DebugReport.CountOf("флагман потоплен");
            int volleys = DebugReport.CountOf("залпов флагмана") + DebugReport.CountOf("залпов флагмана вне прицела");
            int enemyVolleys = DebugReport.CountOf("залпов врага по флагману") + DebugReport.CountOf("залпов врага по своим");
            if (errors > 0) fails.AppendLine("ошибок Unity: " + errors);
            if (volleys == 0) fails.AppendLine("флагман на автобое не дал ни одного залпа");
            if (kills == 0 && seconds >= 180f) fails.AppendLine("за " + seconds + " с ни одного потопленного врага");
            if (enemyVolleys == 0 && seconds >= 180f) fails.AppendLine("враги ни разу не стреляли");
            if (maxStuck > 20f) fails.AppendLine("флагман стоял на месте " + Mathf.RoundToInt(maxStuck) + " с подряд");
            if (deaths > 0) fails.AppendLine("флагман потоплен " + deaths + " раз (должен быть в 2–3 раза сильнее)");
            int intersections = 0; foreach (string s in DebugReport.FindIntersections()) intersections++;
            if (intersections > 0) fails.AppendLine("пересечений моделей: " + intersections);
            string report = DebugReport.Build(1f / Mathf.Max(1e-3f, Time.smoothDeltaTime), "", true);
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report);
            string verdict = fails.Length == 0 ? "ПРОШЁЛ" : "ПРОВАЛЫ:\n" + fails;
            File.WriteAllText(Path.Combine(outDir, "verdict.txt"), verdict);
            Log("ИТОГ " + seconds + " с: убито " + kills + ", флагман потоплен " + deaths + ", " + Accuracy() + ", залпов врагов " + enemyVolleys + ", ошибок " + errors + ", макс. простой " + Mathf.RoundToInt(maxStuck) + " с");
            Log(verdict.Replace("\n", " | "));
            Application.Quit(fails.Length == 0 ? 0 : 2);
        }

        static void Log(string s) { Debug.Log("SIM| " + s); }
    }
}
