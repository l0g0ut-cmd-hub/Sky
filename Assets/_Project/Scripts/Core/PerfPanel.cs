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
        // Работа потоков без ожиданий (FrameTiming): main — PlayerLoop минус ожидание отрисовки/Present/целевого FPS.
        float mainWork, renderWork, presentWait;
        // Разрешение отрисовки: доля от родного экрана (Screen.SetResolution) — проверка, упирается ли ВК в пиксели.
        static readonly float[] scales = { 1f, 0.85f, 0.7f };
        int scaleIndex; int nativeW, nativeH, styleFor;
        const int DefaultScaleIndex = 2;
        bool defaultScaleApplied;
        // Авторазрешение сцены: держит ~30 FPS, меняя долю разрешения ступенями 100/85/70/60% (HUD всегда родной).
        // Телефон греется и за сессию теряет до половины скорости видеокарты (автозамеры 89 и 92) — одна
        // постоянная доля под это не подходит.
        bool autoScale = true;
        float sceneScale = 1f, autoAcc, autoHold;
        int autoFrames;
        // Автозамер: по очереди выключает каждую часть (остальное включено), меряет и показывает таблицу на одном экране.
        struct BenchRow { public string name; public float fps, gpu, frame, main; }
        readonly List<BenchRow> bench = new List<BenchRow>();
        bool benchRunning, benchShow; string benchStep = ""; int benchIndex, benchTotal;
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
            // Сцена по умолчанию — в 70% разрешения, HUD в родном: по автозамеру сборки 89 это −6.5 мс видеокарты
            // (85% почти ничего не даёт). Кнопка «Сцена N%» в панели возвращает 100%.
            if (!defaultScaleApplied && Application.isPlaying && !benchRunning && game != null && Camera.main != null && Time.timeSinceLevelLoad > 0.5f)
            {
                SetScale(DefaultScaleIndex);
                defaultScaleApplied = SceneScaler.Active;
                if (!defaultScaleApplied) { scaleIndex = 0; sceneScale = 1f; }
                autoHold = Time.unscaledTime + 12f;   // прогрев шейдеров и первые кадры в расчёт не идут
            }
            if (defaultScaleApplied && autoScale && !benchRunning && Application.isPlaying)
            {
                autoAcc += Time.unscaledDeltaTime; autoFrames++;
                if (autoAcc >= 4f)
                {
                    float autoFps = autoFrames / autoAcc; autoAcc = 0f; autoFrames = 0;
                    if (Time.unscaledTime > autoHold)
                    {
                        float next = sceneScale;
                        if (autoFps < 26f && sceneScale > 0.61f) next = sceneScale > 0.86f ? 0.85f : sceneScale > 0.71f ? 0.7f : 0.6f;
                        else if (autoFps > 46f && sceneScale < 0.99f) next = sceneScale < 0.65f ? 0.7f : sceneScale < 0.8f ? 0.85f : 1f;
                        if (!Mathf.Approximately(next, sceneScale)) { ApplyScale(next); autoHold = Time.unscaledTime + 10f; }
                    }
                }
            }
            PerfTimers.Enabled = Open && details;
            if (PerfTimers.Enabled) { StartRecorders(); PerfTimers.Collect(); }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpu = Mathf.Lerp(cpu, (float)timings[0].cpuFrameTime, 0.1f);
                gpu = Mathf.Lerp(gpu, (float)timings[0].gpuFrameTime, 0.1f);
                mainWork = Mathf.Lerp(mainWork, (float)timings[0].cpuMainThreadFrameTime, 0.1f);
                renderWork = Mathf.Lerp(renderWork, (float)timings[0].cpuRenderThreadFrameTime, 0.1f);
                presentWait = Mathf.Lerp(presentWait, (float)timings[0].cpuMainThreadPresentWaitTime, 0.1f);
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
            if (styleFor != Screen.height) { label = null; small = null; styleFor = Screen.height; }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(u * 0.032f), alignment = TextAnchor.MiddleLeft };
                label.normal.textColor = new Color(0.96f, 0.9f, 0.77f);
                button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(u * 0.032f) };
            }
            if (benchRunning)
            {
                Rect pr = new Rect(Screen.width * 0.3f, u * 0.02f, Screen.width * 0.4f, u * 0.07f);
                Area = pr;
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(pr, Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(pr.x + u * 0.02f, pr.y, pr.width, pr.height), string.Format("Автозамер {0}/{1}: {2}", benchIndex, benchTotal, benchStep), label);
                return;
            }
            Rect r = new Rect(Screen.width * 0.27f, u * 0.05f, Screen.width * 0.46f, u * 0.86f);
            Area = r;
            if (benchShow) { BenchTable(r, u); return; }
            GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            float y = r.y + u * 0.02f, h = u * 0.075f, x = r.x + u * 0.03f, w = r.width - u * 0.06f;
            GUI.Label(new Rect(x, y, w, h), string.Format("FPS {0:F0}   ЦП {1:F1} мс   ВК {2}", fps, cpu, gpu > 0.01f ? gpu.ToString("F1") + " мс" : "нет данных"), label); y += h;
            GUI.Label(new Rect(x, y, w, h), string.Format("10 с: средний {0:F0}   худший 1% {1:F0}   макс. кадр {2:F0} мс", avgFps, lowFps, worstMs), label); y += h;
            if (small == null) { small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(label.fontSize * 0.8f) }; }
            GUI.Label(new Rect(x, y, w, h * 0.62f), string.Format("работа: игра {0:F1} · отрисовка {1:F1} · ВК {2:F1} мс   → {3}", mainWork, renderWork, gpu, Bottleneck()), small); y += h * 0.7f;
            float hw = (w - u * 0.02f) * 0.5f;
            if (GUI.Button(new Rect(x, y, hw, h * 0.9f), details ? "← Переключатели" : "Замер по частям →", button)) { details = !details; PerfTimers.Reset(); camMs = 0f; }
            if (GUI.Button(new Rect(x + hw + u * 0.02f, y, hw, h * 0.9f), "АВТОЗАМЕР ▶", button) && game != null) StartCoroutine(Bench());
            y += h;
            if (details) { Details(x, y, w, h); return; }
            if (game == null) return;
            VolumetricClouds v = game.Volumetric;
            float cw = (w - u * 0.02f) * 0.5f; int col = 0;
            if (v != null)
            {
                if (Cell(x, ref y, cw, h, ref col, "Объёмные облака", v.enabled)) game.SetVolumetric(!v.enabled);
                if (Cell(x, ref y, cw, h, ref col, "Башни", v.debugTowers)) v.debugTowers = !v.debugTowers;
                if (Cell(x, ref y, cw, h, ref col, "Море облаков", v.debugDeck)) v.debugDeck = !v.debugDeck;
                if (Cell(x, ref y, cw, h, ref col, "Импосторы", v.impostors)) v.impostors = !v.impostors;
                if (Cell(x, ref y, cw, h, ref col, "Облака ¼ за кадр", v.interleave)) v.interleave = !v.interleave;
            }
            ShipCrew crew = game.Ship != null ? game.Ship.GetComponent<ShipCrew>() : null;
            if (crew != null && Cell(x, ref y, cw, h, ref col, "Экипаж", crew.Visible)) crew.SetVisible(!crew.Visible);
            SkyTidePost post = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
            if (post != null && Cell(x, ref y, cw, h, ref col, "Пост-обработка", post.enabled)) post.enabled = !post.enabled;
            if (game.Fx != null && Cell(x, ref y, cw, h, ref col, "Дым и вспышки", !game.Fx.debugHidden)) game.Fx.debugHidden = !game.Fx.debugHidden;
            bool shadows = QualitySettings.shadows != ShadowQuality.Disable;
            if (Cell(x, ref y, cw, h, ref col, "Тени", shadows)) QualitySettings.shadows = shadows ? ShadowQuality.Disable : ShadowQuality.All;
            if (Cell(x, ref y, cw, h, ref col, "Лёгкие тени/LOD", ShipLod.Enabled)) ShipLod.Enabled = !ShipLod.Enabled;
            if (nativeW == 0) { nativeW = Screen.width; nativeH = Screen.height; }
            // Кнопка по кругу: авто → 100% → 85% → 70% → авто.
            if (GUI.Button(new Rect(x + col * (cw + u * 0.02f), y, cw, h * 0.9f), (autoScale ? "Сцена авто · " : "Сцена ") + Mathf.RoundToInt(sceneScale * 100f) + "%", button))
            {
                if (autoScale) { autoScale = false; SetScale(0); }
                else if (scaleIndex < scales.Length - 1) SetScale(scaleIndex + 1);
                else { autoScale = true; autoAcc = 0f; autoFrames = 0; }
            }
            y += h; col = 0;
            if (GUI.Button(new Rect(x, y + h * 0.2f, w, h), "Закрыть", button)) Open = false;
        }

        /// <summary>
        /// Автозамер (~70 с): база — всё включено; затем по одной части выключается (остальное включено), 2 с на
        /// успокоение, 3 с замер (FPS, ВК, полный кадр, работа потока игры). В конце всё как было и таблица — один снимок.
        /// </summary>
        System.Collections.IEnumerator Bench()
        {
            if (benchRunning || game == null) yield break;
            benchRunning = true; benchShow = false; bench.Clear();
            VolumetricClouds v = game.Volumetric;
            ShipCrew crew = game.Ship != null ? game.Ship.GetComponent<ShipCrew>() : null;
            SkyTidePost post = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
            if (nativeW == 0) { nativeW = Screen.width; nativeH = Screen.height; }
            // как было
            bool vOn = v != null && v.enabled, tOn = v != null && v.debugTowers, dOn = v != null && v.debugDeck, iOn = v != null && v.impostors;
            bool ilOn = v != null && v.interleave;
            bool cOn = crew != null && crew.Visible, pOn = post != null && post.enabled, fOn = game.Fx != null && !game.Fx.debugHidden;
            ShadowQuality sq = QualitySettings.shadows; bool lodOn = ShipLod.Enabled; int si = scaleIndex;
            bool autoWas = autoScale; float scaleWas = sceneScale;
            autoScale = false;   // на время замера разрешение не плавает
            System.Action<bool> all = on =>
            {
                if (v != null) { if (v.enabled != on) game.SetVolumetric(on); v.debugTowers = on; v.debugDeck = on; v.impostors = on; v.interleave = on; }
                if (crew != null && crew.Visible != on) crew.SetVisible(on);
                if (post != null) post.enabled = on;
                if (game.Fx != null) game.Fx.debugHidden = !on;
                QualitySettings.shadows = on ? ShadowQuality.All : ShadowQuality.Disable;
                ShipLod.Enabled = true;
                SetScale(DefaultScaleIndex);   // база — как в игре по умолчанию: сцена 70%, облака ¼ лучей за кадр
            };
            var steps = new List<KeyValuePair<string, System.Action>>();
            steps.Add(new KeyValuePair<string, System.Action>("Как в игре (база)", () => all(true)));
            if (v != null)
            {
                steps.Add(new KeyValuePair<string, System.Action>("без объёмных облаков", () => { all(true); game.SetVolumetric(false); }));
                steps.Add(new KeyValuePair<string, System.Action>("без башен", () => { all(true); v.debugTowers = false; }));
                steps.Add(new KeyValuePair<string, System.Action>("без моря облаков", () => { all(true); v.debugDeck = false; }));
                steps.Add(new KeyValuePair<string, System.Action>("без импосторов", () => { all(true); v.impostors = false; }));
            }
            if (v != null) steps.Add(new KeyValuePair<string, System.Action>("облака: все лучи каждый кадр", () => { all(true); v.interleave = false; }));
            if (crew != null) steps.Add(new KeyValuePair<string, System.Action>("без экипажа", () => { all(true); crew.SetVisible(false); }));
            if (post != null) steps.Add(new KeyValuePair<string, System.Action>("без пост-обработки", () => { all(true); post.enabled = false; }));
            if (game.Fx != null) steps.Add(new KeyValuePair<string, System.Action>("без дыма и вспышек", () => { all(true); game.Fx.debugHidden = true; }));
            steps.Add(new KeyValuePair<string, System.Action>("без теней", () => { all(true); QualitySettings.shadows = ShadowQuality.Disable; }));
            steps.Add(new KeyValuePair<string, System.Action>("полные модели (без лёгких)", () => { all(true); ShipLod.Enabled = false; }));
            steps.Add(new KeyValuePair<string, System.Action>("сцена 85% вместо 70%", () => { all(true); SetScale(1); }));
            steps.Add(new KeyValuePair<string, System.Action>("сцена 100% вместо 70%", () => { all(true); SetScale(0); }));
            steps.Add(new KeyValuePair<string, System.Action>("всё выключено", () => all(false)));
            benchTotal = steps.Count;
            for (int k = 0; k < steps.Count; k++)
            {
                benchIndex = k + 1; benchStep = steps[k].Key;
                steps[k].Value();
                float t = 0f;
                while (t < 2f) { t += Time.unscaledDeltaTime; yield return null; }
                float sumDt = 0f, sumGpu = 0f, sumFrame = 0f, sumMain = 0f; int n = 0, ng = 0;
                while (sumDt < 3f)
                {
                    yield return null;
                    sumDt += Time.unscaledDeltaTime; n++;
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        sumFrame += (float)timings[0].cpuFrameTime; sumMain += (float)timings[0].cpuMainThreadFrameTime;
                        sumGpu += (float)timings[0].gpuFrameTime; ng++;
                    }
                }
                bench.Add(new BenchRow { name = steps[k].Key, fps = n / Mathf.Max(sumDt, 1e-3f),
                    gpu = ng > 0 ? sumGpu / ng : 0f, frame = ng > 0 ? sumFrame / ng : sumDt * 1000f / n, main = ng > 0 ? sumMain / ng : 0f });
            }
            // вернуть как было
            if (v != null) { if (v.enabled != vOn) game.SetVolumetric(vOn); v.debugTowers = tOn; v.debugDeck = dOn; v.impostors = iOn; v.interleave = ilOn; }
            if (crew != null && crew.Visible != cOn) crew.SetVisible(cOn);
            if (post != null) post.enabled = pOn;
            if (game.Fx != null) game.Fx.debugHidden = !fOn;
            QualitySettings.shadows = sq; ShipLod.Enabled = lodOn;
            scaleIndex = si; ApplyScale(scaleWas); autoScale = autoWas; autoAcc = 0f; autoFrames = 0; autoHold = Time.unscaledTime + 6f;
            System.Text.StringBuilder log = new System.Text.StringBuilder("SkyTide: автозамер\n");
            foreach (BenchRow b in bench) log.AppendFormat("{0}: FPS {1:F1}, ВК {2:F1} мс, кадр {3:F1} мс, игра {4:F1} мс\n", b.name, b.fps, b.gpu, b.frame, b.main);
            Debug.Log(log.ToString());
            benchRunning = false; benchShow = true; Open = true;
        }

        void SetScale(int i)
        {
            scaleIndex = i;
            ApplyScale(scales[i]);
        }

        void ApplyScale(float s)
        {
            if (Mathf.Approximately(s, sceneScale)) return;
            sceneScale = s;
            SceneScaler.SetScale(s);   // сцена в доле разрешения, HUD — в родном
        }

        void BenchTable(Rect r, float u)
        {
            // Почти непрозрачная подложка: под таблицей не должны читаться плашки HUD.
            GUI.color = new Color(0f, 0f, 0f, 0.95f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            if (small == null) small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(label.fontSize * 0.8f) };
            float x = r.x + u * 0.03f, w = r.width - u * 0.06f, y = r.y + u * 0.012f, lh = u * 0.046f;
            string dev = SystemInfo.graphicsDeviceName + "  " + nativeW + "×" + nativeH + "  сборка " + Application.version;
            GUI.Label(new Rect(x, y, w, lh), "Автозамер · " + dev, small); y += lh;
            float c0 = w * 0.44f, cwid = (w - c0) / 5f;
            string[] head = { "FPS", "ВК мс", "кадр", "игра", "ΔВК" };
            GUI.Label(new Rect(x, y, c0, lh), "что изменено", small);
            for (int i = 0; i < 5; i++) GUI.Label(new Rect(x + c0 + cwid * i, y, cwid, lh), head[i], small);
            y += lh;
            float baseGpu = bench.Count > 0 ? bench[0].gpu : 0f;
            foreach (BenchRow b in bench)
            {
                float d = b.gpu - baseGpu;
                string[] cells = { b.fps.ToString("F1"), b.gpu.ToString("F1"), b.frame.ToString("F1"), b.main.ToString("F1"), (d >= 0f ? "+" : "") + d.ToString("F1") };
                GUI.Label(new Rect(x, y, c0, lh), b.name, small);
                for (int i = 0; i < 5; i++) GUI.Label(new Rect(x + c0 + cwid * i, y, cwid, lh), cells[i], small);
                y += lh;
            }
            if (GUI.Button(new Rect(x, r.yMax - u * 0.08f, w * 0.48f, u * 0.065f), "Ещё раз", button) && !benchRunning) StartCoroutine(Bench());
            if (GUI.Button(new Rect(x + w * 0.52f, r.yMax - u * 0.08f, w * 0.48f, u * 0.065f), "Закрыть", button)) { benchShow = false; Open = false; }
        }

        /// <summary>Во что упирается кадр — по образцу DetermineBottleneck из документации Unity FrameTiming.</summary>
        string Bottleneck()
        {
            if (gpu <= 0.01f) return "ВК не измеряется";
            float full = 0.8f * cpu;
            if (presentWait > 0.5f && gpu < full && mainWork < full && renderWork < full) return "упор: ограничение FPS";
            bool g = gpu > full, c = mainWork > full || renderWork > full;
            if (g && c) return "упор: и ЦП, и ВК";
            if (g) return "упор: ВИДЕОКАРТА";
            if (c) return "упор: ПРОЦЕССОР";
            return "упор: не ясно";
        }

        bool Cell(float x, ref float y, float cw, float h, ref int col, string name, bool on)
        {
            float u = Mathf.Min(Screen.width, Screen.height);
            bool hit = GUI.Button(new Rect(x + col * (cw + u * 0.02f), y, cw, h * 0.9f), (on ? "ВКЛ  " : "выкл  ") + name, button);
            if (col == 1) { col = 0; y += h; } else col = 1;
            return hit;
        }

        void Details(float x, float y, float w, float h)
        {
            float lh = h * 0.62f;
            GUI.Label(new Rect(x, y, w, lh), string.Format("{0} {1}   {2}×{3}   тени {4} {5}м ×{6}   MSAA {7}",
                SystemInfo.graphicsDeviceType, SystemInfo.graphicsDeviceName, Screen.width, Screen.height,
                QualitySettings.shadows, QualitySettings.shadowDistance, QualitySettings.shadowCascades, QualitySettings.antiAliasing), small); y += lh;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Поток игры {0} мс   поток отрисовки {1} мс   камера на ЦП {2:F1} мс", RecMs(mainThread), RecMs(renderThread), camMs), small); y += lh;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Вызовов {0}   смен шейдера {1}   пакетов {2}", Rec(drawCalls), Rec(setPass), Rec(batches)), small); y += lh;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Треугольников {0}   отбрасывают тень {1}   мусор {2} Б", Rec(tris), Rec(shadowCasters), Rec(gcAlloc)), small); y += lh;
            PerfTimers.Sorted(top);
            float total = 0f; foreach (KeyValuePair<string, float> kv in top) total += kv.Value;
            GUI.Label(new Rect(x, y, w, lh), string.Format("Наши скрипты всего: {0:F1} мс", total), small); y += lh;
            for (int i = 0; i < top.Count && i < 7; i++)
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
