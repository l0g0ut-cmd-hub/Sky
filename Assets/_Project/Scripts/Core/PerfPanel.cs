using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Отладочная панель производительности: касание плашки FPS (или F1) — открыть/закрыть.
    /// Время кадра процессора и видеокарты (FrameTimingManager), средний FPS, и переключатели крупных частей
    /// картинки — чтобы на телефоне увидеть, что сколько стоит. Переключатели только для замера, в сохранение не идут.
    /// </summary>
    public class PerfPanel : MonoBehaviour
    {
        public static bool Open { get; private set; }
        /// <summary>Касание плашки FPS в HUD.</summary>
        public static void Toggle() { Open = !Open; }
        /// <summary>Область панели на экране (пиксели, начало сверху-слева) — касания внутри не уходят кораблю.</summary>
        public static Rect Area { get; private set; }
        SkyTideBootstrap game;
        readonly FrameTiming[] timings = new FrameTiming[1];
        float cpu, gpu, fps, acc; int frames;
        // Замер за последние 10 с: средний FPS, худший 1% кадров (99-й процентиль времени кадра), самый долгий кадр.
        readonly float[] window = new float[1200]; int wHead, wCount; float wSum;
        float avgFps, lowFps, worstMs, statClock;
        readonly float[] sorted = new float[1200];
        GUIStyle label, button, small;
        // Страница «Замер»: время процессора по нашим системам (PerfTimers), отрисовка камеры на процессоре
        // (от начала отсечения до конца сцены) и счётчики Unity (ProfilerRecorder — доступны и в релизной сборке).
        bool details;
        readonly List<KeyValuePair<string, float>> top = new List<KeyValuePair<string, float>>();
        ProfilerRecorder drawCalls, setPass, batches, tris, shadowCasters, gcAlloc, mainThread, renderThread;
        bool recorders;
        long camStart; float camMs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isPlaying) return;
            GameObject go = new GameObject("PerfPanel");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfPanel>();
        }

        void StartRecorders()
        {
            if (recorders) return; recorders = true;
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
            Camera.onPreCull += CamPre;
            Camera.onPostRender += CamPost;
        }
        void OnDestroy()
        {
            if (!recorders) return;
            drawCalls.Dispose(); setPass.Dispose(); batches.Dispose(); tris.Dispose(); shadowCasters.Dispose();
            gcAlloc.Dispose(); mainThread.Dispose(); renderThread.Dispose();
            Camera.onPreCull -= CamPre; Camera.onPostRender -= CamPost;
        }
        void CamPre(Camera c) { if (PerfTimers.Enabled && c == Camera.main) camStart = System.Diagnostics.Stopwatch.GetTimestamp(); }
        void CamPost(Camera c)
        {
            if (!PerfTimers.Enabled || c != Camera.main || camStart == 0) return;
            float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - camStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            camMs = camMs * 0.9f + ms * 0.1f; camStart = 0;
        }
        static string Rec(ProfilerRecorder r) { return r.Valid ? r.LastValue.ToString() : "—"; }
        static string RecMs(ProfilerRecorder r)
        {
            if (!r.Valid || r.Count == 0) return "—";
            double sum = 0; int n = r.Count;
            for (int i = 0; i < n; i++) sum += r.GetSample(i).Value;
            return (sum / n * 1e-6).ToString("F1");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) Open = !Open;
            PerfTimers.Enabled = Open && details;
            if (PerfTimers.Enabled) { StartRecorders(); PerfTimers.Collect(); }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpu = Mathf.Lerp(cpu, (float)timings[0].cpuFrameTime, 0.1f);
                gpu = Mathf.Lerp(gpu, (float)timings[0].gpuFrameTime, 0.1f);
            }
            float dt = Time.unscaledDeltaTime;
            if (wCount == window.Length) { wSum -= window[wHead]; } else wCount++;
            window[wHead] = dt; wSum += dt; wHead = (wHead + 1) % window.Length;
            while (wCount > 1 && wSum - window[(wHead - wCount + window.Length) % window.Length] > 10f)
            { wSum -= window[(wHead - wCount + window.Length) % window.Length]; wCount--; }
            statClock += dt;
            if (Open && statClock >= 0.5f && wCount > 10)
            {
                statClock = 0f;
                for (int i = 0; i < wCount; i++) sorted[i] = window[(wHead - wCount + i + window.Length) % window.Length];
                System.Array.Sort(sorted, 0, wCount);
                avgFps = wCount / Mathf.Max(wSum, 1e-4f);
                lowFps = 1f / Mathf.Max(sorted[Mathf.Min(wCount - 1, Mathf.FloorToInt(wCount * 0.99f))], 1e-4f);
                worstMs = sorted[wCount - 1] * 1000f;
            }
            acc += dt; frames++;
            if (acc >= 0.5f) { fps = frames / acc; acc = 0f; frames = 0; }
        }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -800;
            if (game == null) game = FindFirstObjectByType<SkyTideBootstrap>();
            float u = Mathf.Min(Screen.width, Screen.height);
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(u * 0.032f), alignment = TextAnchor.MiddleLeft };
                label.normal.textColor = new Color(0.96f, 0.9f, 0.77f);
                button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(u * 0.032f) };
            }
            Rect r = new Rect(Screen.width * 0.27f, u * 0.05f, Screen.width * 0.46f, u * 0.86f);
            Area = r;
            GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            float y = r.y + u * 0.02f, h = u * 0.075f, x = r.x + u * 0.03f, w = r.width - u * 0.06f;
            GUI.Label(new Rect(x, y, w, h), string.Format("FPS {0:F0}   ЦП {1:F1} мс   ВК {2}", fps, cpu, gpu > 0.01f ? gpu.ToString("F1") + " мс" : "нет данных"), label); y += h;
            GUI.Label(new Rect(x, y, w, h), string.Format("10 с: средний {0:F0}   худший 1% {1:F0}   макс. кадр {2:F0} мс", avgFps, lowFps, worstMs), label); y += h;
            if (GUI.Button(new Rect(x, y, w, h * 0.9f), details ? "← Переключатели" : "Замер по частям →", button)) { details = !details; PerfTimers.Reset(); camMs = 0f; }
            y += h;
            if (details) { Details(x, y, w, h); return; }
            if (game == null) return;
            VolumetricClouds v = game.Volumetric;
            if (v != null)
            {
                if (Toggle(x, ref y, w, h, "Объёмные облака целиком", v.enabled)) game.SetVolumetric(!v.enabled);
                if (Toggle(x, ref y, w, h, "Башни", v.debugTowers)) v.debugTowers = !v.debugTowers;
                if (Toggle(x, ref y, w, h, "Море облаков", v.debugDeck)) v.debugDeck = !v.debugDeck;
                if (Toggle(x, ref y, w, h, "Импосторы (дальние облака)", v.impostors)) v.impostors = !v.impostors;
            }
            ShipCrew crew = game.Ship != null ? game.Ship.GetComponent<ShipCrew>() : null;
            if (crew != null && Toggle(x, ref y, w, h, "Экипаж", crew.Visible)) crew.SetVisible(!crew.Visible);
            SkyTidePost post = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
            if (post != null && Toggle(x, ref y, w, h, "Пост-обработка", post.enabled)) post.enabled = !post.enabled;
            if (game.Fx != null && Toggle(x, ref y, w, h, "Дым, вспышки, всплески", !game.Fx.debugHidden)) game.Fx.debugHidden = !game.Fx.debugHidden;
            if (GUI.Button(new Rect(x, y + h * 0.2f, w, h), "Закрыть", button)) Open = false;
        }

        void Details(float x, float y, float w, float h)
        {
            if (small == null) { small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(label.fontSize * 0.8f) }; }
            float lh = h * 0.62f;
            GUI.Label(new Rect(x, y, w, lh), string.Format("{0} {1}   {2}×{3}   тени {4} {5}м ×{6}   MSAA {7}",
                SystemInfo.graphicsDeviceType, SystemInfo.graphicsDeviceName, Screen.width, Screen.height,
                QualitySettings.shadows, QualitySettings.shadowDistance, QualitySettings.shadowCascades, QualitySettings.antiAliasing), small); y += lh;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Поток игры {0} мс   поток отрисовки {1} мс   камера на ЦП {2:F1} мс", RecMs(mainThread), RecMs(renderThread), camMs), small); y += lh;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Вызовов {0}   смен шейдера {1}   пакетов {2}   треуг. {3}   тени {4}   мусор {5} Б",
                Rec(drawCalls), Rec(setPass), Rec(batches), Rec(tris), Rec(shadowCasters), Rec(gcAlloc)), small); y += lh;
            PerfTimers.Sorted(top);
            float total = 0f; foreach (KeyValuePair<string, float> kv in top) total += kv.Value;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Наши скрипты всего: {0:F1} мс", total), small); y += lh;
            for (int i = 0; i < top.Count && i < 9; i++)
            {
                GUI.Label(new Rect(x, y, w, lh), string.Format("{0,6:F2} мс   {1}", top[i].Value, top[i].Key), small); y += lh;
            }
        }

        bool Toggle(float x, ref float y, float w, float h, string name, bool on)
        {
            bool hit = GUI.Button(new Rect(x, y, w, h * 0.9f), (on ? "ВКЛ   " : "выкл   ") + name, button);
            y += h;
            return hit;
        }
    }
}
