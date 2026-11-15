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
