using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Регистратор для отладочного отчёта (только в игре): хроника каждые 10 с, рывки кадра, счётчики отрисовки
    /// (вызовы, треугольники, память), температура батареи, автосохранение отчёта (ошибка, пауза, выход) — на
    /// следующем запуске доступен «отчёт прошлой сессии».
    /// </summary>
    public class DebugRecorder : MonoBehaviour
    {
        public static DebugRecorder Instance { get; private set; }
        readonly Queue<string> chronicle = new Queue<string>();
        readonly Queue<string> spikes = new Queue<string>();
        readonly Queue<string> track = new Queue<string>();
        float nextTrack; bool benchChecked;
        float nextSnap, nextTemp, nextSave = 60f;
        int gcAtFrame;
        ProfilerRecorder drawCalls, batches, setPass, triangles, gcAlloc, usedMemory;
        public float BatteryTemp { get; private set; } = -1f;
        public int BatteryLevel { get; private set; } = -1;
        public static string PreviousSession { get; private set; }
        public static string ReportPath { get { return System.IO.Path.Combine(Application.persistentDataPath, "skytide_report.txt"); } }

        void Awake()
        {
            Instance = this;
            try { if (System.IO.File.Exists(ReportPath)) PreviousSession = System.IO.File.ReadAllText(ReportPath); } catch (System.Exception) { PreviousSession = null; }
        }

        void OnEnable()
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }

        void OnDisable()
        {
            drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); triangles.Dispose(); gcAlloc.Dispose(); usedMemory.Dispose();
        }

        static string Rec(ProfilerRecorder r, float div, string unit) { return r.Valid ? (r.LastValue / div).ToString("F0") + unit : "н/д"; }

        public string RenderLine()
        {
            FxSystem fx = FxSystem.Instance;
            return "отрисовка: вызовов " + Rec(drawCalls, 1f, "") + " · пакетов " + Rec(batches, 1f, "") + " · SetPass " + Rec(setPass, 1f, "")
                + " · треуг " + Rec(triangles, 1000f, "k") + " · мусор/кадр " + Rec(gcAlloc, 1024f, " КБ") + " · память " + Rec(usedMemory, 1048576f, " МБ")
                + (fx != null ? " · частиц " + fx.ParticleCount + " · огней " + fx.LampCount : "")
                + " · батарея " + (BatteryTemp > 0f ? BatteryTemp.ToString("F1") + "°C " : "") + (BatteryLevel >= 0 ? BatteryLevel + "%" : "");
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            int gc = System.GC.CollectionCount(0);
            // Рывок: кадр дольше 60 мс (после первых 5 с — прогрев) — что было в этот момент.
            if (ms > 60f && Time.timeSinceLevelLoad > 5f)
            {
                FxSystem fx = FxSystem.Instance;
                Push(spikes, 12, DebugReport.Stamp() + " " + Mathf.RoundToInt(ms) + " мс" + (gc != gcAtFrame ? " +сборка мусора" : "") + (fx != null ? " частиц " + fx.ParticleCount : "") + " · " + DebugReport.LastEventText);
            }
            gcAtFrame = gc;
            if (Time.time >= nextSnap) { nextSnap = Time.time + 10f; Snapshot(); }
            if (Time.time >= nextTrack) { nextTrack = Time.time + 2f; Track(); }
            // Первый запуск новой сборки: автозамер сам, через 40 с боя (не в меню, не на паузе).
            if (!benchChecked && Time.timeSinceLevelLoad > 40f && !MainMenu.Active && Time.timeScale > 0.5f && !CiBattle.Active && PerfPanel.Instance != null)
            {
                benchChecked = true;
                int bn = DebugReport.BuildNo;
                if (bn > 0 && PlayerPrefs.GetInt("SkyTide.BenchedBuild", 0) != bn)
                {
                    PlayerPrefs.SetInt("SkyTide.BenchedBuild", bn); PlayerPrefs.Save();
                    HudNotice.Show("НОВАЯ СБОРКА: АВТОЗАМЕР ~30 с, не трогай", 0, 4f);
                    PerfPanel.Instance.AutoRun = true; PerfPanel.Instance.StartBench();
                }
            }
            if (Time.realtimeSinceStartup >= nextTemp) { nextTemp = Time.realtimeSinceStartup + 30f; ReadBattery(); }
            if (Time.realtimeSinceStartup >= nextSave) { nextSave = Time.realtimeSinceStartup + 60f; Save(); }
        }

        /// <summary>Хроника: одна строка на 10 с — где и как каждый корабль. Координаты мира в десятках метров (x/z),
        /// буква состояния: Б бой, П порт, Д дрейф/дозор, ✝ потоплен.</summary>
        void Snapshot()
        {
            StringBuilder b = new StringBuilder(DebugReport.Stamp());
            PlayerHull ph = PlayerHull.Instance;
            if (ph != null) b.Append(" СВОЙ " + Pos(ph.transform.position) + " " + Mathf.RoundToInt(ph.Health01 * 100f) + "%");
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
            if (tc != null && tc.AutoBattle) b.Append(" [" + tc.AutoState.Replace("АВТОБОЙ · ", "") + "]");
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null) continue;
                b.Append(" | " + Short(t.name) + (t.Alive ? " " + Pos(t.transform.position) + " " + Mathf.RoundToInt(t.Health01 * 100f) + "% " + t.AiState : " ✝"));
            }
            foreach (AllyShip a in AllyShip.All)
            {
                if (a == null) continue;
                b.Append(" | " + AllyShip.RoleNames[(int)a.role].Substring(0, 3) + (a.Alive ? " " + Pos(a.transform.position) + " " + Mathf.RoundToInt(a.Health01 * 100f) + "% " + a.AiState : " ✝"));
            }
            Push(chronicle, 18, b.ToString());
        }

        /// <summary>Карта боя: каждые 2 с позиции всех кораблей (10 мин). Формат строки:
        /// «t F x,z,курс,%[,А] | E<i>[T] x,z,% | A<роль> x,z,%», x/z — десятки метров, курс — градусы/10.
        /// Рисует tools/battlemap.py (картинка траекторий).</summary>
        void Track()
        {
            StringBuilder b = new StringBuilder(160);
            b.Append(Mathf.RoundToInt(Time.timeSinceLevelLoad));
            PlayerHull ph = PlayerHull.Instance;
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (ph != null)
            {
                TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
                b.Append(" F ").Append(P(ph.transform.position)).Append(',').Append(Mathf.RoundToInt(ph.transform.eulerAngles.y / 10f)).Append(',').Append(Mathf.RoundToInt(ph.Health01 * 100f));
                if (tc != null && tc.AutoBattle) b.Append(",А");
            }
            int i = 0;
            foreach (TargetShip t in TargetShip.All)
            {
                i++;
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                b.Append(" | E").Append(i).Append(t.Heavy ? "T" : "").Append(' ');
                if (t.Alive) b.Append(P(t.transform.position)).Append(',').Append(Mathf.RoundToInt(t.Health01 * 100f)); else b.Append('x');
            }
            foreach (AllyShip a in AllyShip.All)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                b.Append(" | A").Append("GWM"[(int)a.role]).Append(' ');
                if (a.Alive) b.Append(P(a.transform.position)).Append(',').Append(Mathf.RoundToInt(a.Health01 * 100f)); else b.Append('x');
            }
            Push(track, 300, b.ToString());
        }
        static string P(Vector3 p) { return Mathf.RoundToInt(p.x / 10f) + "," + Mathf.RoundToInt(p.z / 10f); }

        public void AppendTrack(StringBuilder b)
        {
            b.AppendLine("КАРТА БОЯ (каждые 2 с; x,z — десятки м; F флагман курс/10 %, E враг [T тяжёлый], A свой G/W/M; x — потоплен):");
            SkyFortress f = SkyFortress.Instance;
            if (f != null) b.AppendLine(" крепость " + P(f.Center));
            foreach (string s in track) b.AppendLine(" " + s);
        }

        /// <summary>Что рисуется: видимые рендереры по группам — штук, тыс. треугольников, отбрасывающих тень.
        /// Помогает решить, что резать на видеокарте (корабли/экипаж/облака/крепость/эффекты).</summary>
        public void AppendGpu(StringBuilder b)
        {
            string[] names = { "корабли", "экипаж", "облака", "крепость", "эффекты", "прочее" };
            int[] n = new int[6], sh = new int[6]; long[] tri = new long[6];
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || !r.isVisible) continue;
                int k = Group(r.transform);
                n[k]++; if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) sh[k]++;
                Mesh m = null;
                if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
                else { MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                if (m != null) for (int s = 0; s < m.subMeshCount; s++) tri[k] += m.GetIndexCount(s) / 3;
            }
            b.Append("видеокарта по группам (видимых · тыс.треуг · с тенью):");
            for (int k = 0; k < 6; k++) if (n[k] > 0) b.Append(" " + names[k] + " " + n[k] + "·" + (tri[k] / 1000) + "k·" + sh[k] + ";");
            b.AppendLine();
            b.AppendLine(RenderLine());
        }

        static int Group(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                string nm = p.name;
                if (nm.Contains("Crew") || nm.Contains("crew")) return 1;
                if (p.GetComponent<ShipController>() != null) return 0;
                if (p.GetComponent<SkyFortress>() != null) return 3;
                if (p.GetComponent<CloudSea>() != null || nm.Contains("Cloud")) return 2;
                if (p.GetComponent<FxSystem>() != null) return 4;
            }
            return 5;
        }

        static string Short(string n) { return n.Replace(" Ship", "").Replace("Enemy ", "в").Replace("Target", "в1").Replace("Raider", "в2").Replace("Scout", "разв"); }
        static string Pos(Vector3 p) { return Mathf.RoundToInt(p.x / 10f) + "/" + Mathf.RoundToInt(p.z / 10f); }

        void ReadBattery()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject filter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                using (AndroidJavaObject intent = act.Call<AndroidJavaObject>("registerReceiver", null, filter))
                {
                    if (intent != null)
                    {
                        BatteryTemp = intent.Call<int>("getIntExtra", "temperature", -10) / 10f;
                        int level = intent.Call<int>("getIntExtra", "level", -1), scale = intent.Call<int>("getIntExtra", "scale", 100);
                        BatteryLevel = level >= 0 && scale > 0 ? level * 100 / scale : -1;
                    }
                }
            }
            catch (System.Exception) { BatteryTemp = -1f; BatteryLevel = Mathf.RoundToInt(SystemInfo.batteryLevel * 100f); }
#else
            BatteryLevel = Mathf.RoundToInt(SystemInfo.batteryLevel * 100f);
#endif
        }

        public void AppendChronicle(StringBuilder b)
        {
            b.AppendLine("хроника каждые 10 с (x/z — мир в десятках м; состояния: БОЙ, ПОРТ, БЕГ, ДОЗОР, ИДЁТ, ПОМОЩЬ, РЕМОНТ):");
            foreach (string s in chronicle) b.AppendLine(" " + s);
        }
        public void AppendSpikes(StringBuilder b)
        {
            b.AppendLine("рывки > 60 мс (последние 12):");
            if (spikes.Count == 0) b.AppendLine(" нет");
            foreach (string s in spikes) b.AppendLine(" " + s);
        }

        static void Push(Queue<string> q, int max, string s) { q.Enqueue(s); while (q.Count > max) q.Dequeue(); }

        /// <summary>Полный отчёт — в файл (переживает вылет): ошибка, пауза, выход и раз в минуту.</summary>
        public void Save()
        {
            try { System.IO.File.WriteAllText(ReportPath, DebugReport.Build(1f / Mathf.Max(1e-3f, Time.smoothDeltaTime), "", true)); }
            catch (System.Exception) { }
        }
        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() { Save(); }
    }
}
