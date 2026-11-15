using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Отладочный отчёт для разработки: журнал событий игры (последние 60), ошибки и предупреждения Unity (последние
    /// 25, со стеком первой строки), снимок состояния (устройство, сборка, FPS и время видеокарты, настройки, корабли,
    /// автобой, погода, ветер) и последняя таблица автозамера. Панель FPS → «КОПИРОВАТЬ ОТЧЁТ» кладёт всё в буфер
    /// обмена — владелец вставляет в чат.
    /// </summary>
    public static class DebugReport
    {
        static readonly Queue<string> events = new Queue<string>();
        static readonly Queue<string> errors = new Queue<string>();
        static int errorCount, warningCount;
        static bool hooked;
        public static string LastBench = "";
        // Счётчики хода игры: стрельба, попадания, потери, поведение ИИ — по ним видно, что в бою реально происходит.
        static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
        static readonly List<string> counterOrder = new List<string>();
        public static void Count(string name, int add = 1)
        {
            if (!Application.isPlaying) return;
            int v; if (!counters.TryGetValue(name, out v)) counterOrder.Add(name);
            counters[name] = v + add;
        }
        static int Get(string name) { int v; return counters.TryGetValue(name, out v) ? v : 0; }
        // Время в состояниях автобоя (с).
        static readonly Dictionary<string, float> stateTime = new Dictionary<string, float>();
        public static void StateTick(string state, float dt) { if (string.IsNullOrEmpty(state)) return; float v; stateTime.TryGetValue(state, out v); stateTime[state] = v + dt; }

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type == LogType.Warning) { warningCount++; if (warningCount > 40) return; }
            else errorCount++;
            string first = string.IsNullOrEmpty(stack) ? "" : stack.Split('\n')[0];
            Push(errors, 25, Stamp() + " " + type + ": " + Trim(message, 220) + (first.Length > 0 ? " @ " + Trim(first, 140) : ""));
        }

        /// <summary>Событие игры в журнал (оповещения HUD, гибели, абордаж, автобой и т. п.).</summary>
        public static void Event(string text) { Push(events, 60, Stamp() + " " + text); }

        static void Push(Queue<string> q, int max, string s) { q.Enqueue(s); while (q.Count > max) q.Dequeue(); }
        static string Stamp() { float t = Time.timeSinceLevelLoad; return ((int)(t / 60f)).ToString("00") + ":" + ((int)(t % 60f)).ToString("00"); }
        static string Trim(string s, int n) { return s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s; }

        public static string Build(float fps, string timers)
        {
            StringBuilder b = new StringBuilder(4096);
            b.AppendLine("=== Небесный прилив · отладочный отчёт ===");
            b.AppendLine("сборка " + Application.version + " · " + SystemInfo.deviceModel + " · " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType
                + " · " + Screen.width + "×" + Screen.height + " · ОЗУ " + SystemInfo.systemMemorySize + " МБ · Android " + SystemInfo.operatingSystem);
            b.AppendLine("время в бою " + Stamp() + " · FPS " + fps.ToString("F1") + " · потолок " + Application.targetFrameRate + " · ошибок " + errorCount + " · предупреждений " + warningCount);
            FrameTiming[] ft = new FrameTiming[1];
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                b.AppendLine("кадр: ВК " + ft[0].gpuFrameTime.ToString("F1") + " мс, поток игры " + ft[0].cpuMainThreadFrameTime.ToString("F1") + " мс, отрисовка " + ft[0].cpuRenderThreadFrameTime.ToString("F1") + " мс");
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (g != null)
            {
                b.AppendLine("облака " + (g.volumetricClouds ? "объёмные" : "рисованные") + " · тени " + QualitySettings.shadows + " " + QualitySettings.shadowDistance + " м · сумерки " + SkyTidePost.DuskOn
                    + " · погода " + (WeatherSystem.Instance != null ? WeatherSystem.Names[(int)WeatherSystem.Instance.Current] : "—")
                    + " · ветер " + WindField.Strength.ToString("F2") + " " + Mathf.RoundToInt(Quaternion.LookRotation(WindField.Dir).eulerAngles.y) + "°");
                PlayerHull ph = PlayerHull.Instance;
                if (ph != null && g.Ship != null)
                {
                    Broadside br = g.Ship.GetComponent<Broadside>();
                    TouchControls tc = g.GetComponent<TouchControls>();
                    b.AppendLine("флагман: корпус " + Mathf.RoundToInt(ph.Health01 * 100f) + "% · ход " + g.Ship.Speed.ToString("F1") + "/" + g.Ship.maxSpeed + " · "
                        + (br != null ? "предел пушек " + Mathf.RoundToInt(br.MaxRange) + " м · " + (br.chainShot ? "книппели" : "ядра") + " · в прицеле " + br.AimOnTarget + " · решение " + br.HasSolution : "")
                        + (tc != null ? " · автобой " + (tc.AutoBattle ? tc.AutoState : "выкл") : ""));
                    Vector3 pp = g.Ship.transform.position;
                    b.AppendLine("враги (" + TargetShip.All.Count + "):");
                    foreach (TargetShip t in TargetShip.All)
                    {
                        if (t == null) continue;
                        b.AppendLine("  " + t.name + (t.Heavy ? " [тяжёлый]" : "") + " · " + (t.Alive ? Mathf.RoundToInt(t.Health01 * 100f) + "%" : t.Captured ? "взят" : "потоплен")
                            + " · " + Mathf.RoundToInt(Vector3.Distance(t.transform.position, pp)) + " м · капитан " + TargetShip.SkillNames[(int)t.skill]
                            + (t.InPort ? " · в порту" : "") + (t.Outlaw ? " · нарушитель" : "") + (t.Controller != null ? " · ход " + t.Controller.Speed.ToString("F0") : ""));
                    }
                    b.AppendLine("свои (" + AllyShip.All.Count + "):");
                    foreach (AllyShip a in AllyShip.All)
                        if (a != null) b.AppendLine("  " + AllyShip.RoleNames[(int)a.role] + " · " + (a.Alive ? Mathf.RoundToInt(a.Health01 * 100f) + "%" : "потоплен") + " · " + Mathf.RoundToInt(Vector3.Distance(a.transform.position, pp)) + " м" + (a.Fighting ? " · в бою" : ""));
                }
            }
            // Ход боя и выводы.
            b.AppendLine("ход боя:");
            foreach (string k in counterOrder) b.AppendLine("  " + k + ": " + counters[k]);
            int shells = Get("ядер флагмана выпущено"), hitsP = Get("попаданий флагмана");
            if (shells > 0) b.AppendLine("  точность флагмана: " + Mathf.RoundToInt(100f * hitsP / shells) + "%");
            if (stateTime.Count > 0)
            {
                b.Append("  автобой, время в состояниях:");
                foreach (KeyValuePair<string, float> kv in stateTime) b.Append(" " + kv.Key.Replace("АВТОБОЙ · ", "") + " " + Mathf.RoundToInt(kv.Value) + "с;");
                b.AppendLine();
            }
            b.AppendLine("выводы (автоматически):");
            int outs = 0;
            float minutes = Mathf.Max(0.5f, Time.timeSinceLevelLoad / 60f);
            if (shells > 20 && hitsP * 100 < shells * 15) { b.AppendLine("  ! точность флагмана ниже 15% — наводка/упреждение мажут или стреляем вне прицела"); outs++; }
            if (Get("залпов флагмана") > 0 && shells == 0) { b.AppendLine("  ! залпы есть, а ядер нет — пушки не стреляют"); outs++; }
            if (Get("попаданий по флагману") > Get("попаданий флагмана") * 2 && Get("попаданий по флагману") > 10) { b.AppendLine("  ! по нам попадают вдвое чаще, чем мы — враг слишком меткий или мы в его зоне"); outs++; }
            if (Get("флагман потоплен") > 0 && minutes < 10f) { b.AppendLine("  ! флагман гибнет быстро — баланс прочности/урона"); outs++; }
            if (Get("врагов потоплено") + Get("призов взято") == 0 && minutes > 5f) { b.AppendLine("  ! за " + Mathf.RoundToInt(minutes) + " мин ни одного уничтоженного врага"); outs++; }
            if (Get("своих потоплено") > Get("врагов потоплено") && Get("своих потоплено") > 2) { b.AppendLine("  ! свои гибнут чаще врагов — союзники слабы или лезут под огонь"); outs++; }
            float hunt = 0f, fight = 0f; foreach (KeyValuePair<string, float> kv in stateTime) { if (kv.Key.Contains("ПОИСК") || kv.Key.Contains("ПРЕСЛЕД")) hunt += kv.Value; if (kv.Key.Contains("БОЙ") || kv.Key.Contains("ПЕРЕХВАТ")) fight += kv.Value; }
            if (hunt > 60f && hunt > fight * 2f) { b.AppendLine("  ! автобой больше ищет, чем воюет — врагов мало или они далеко"); outs++; }
            if (errorCount > 0) { b.AppendLine("  ! в логе есть ошибки — см. ниже"); outs++; }
            if (fps > 0f && fps < 22f) { b.AppendLine("  ! FPS " + fps.ToString("F0") + " — упор, см. кадр/ВК"); outs++; }
            if (outs == 0) b.AppendLine("  явных проблем не найдено");
            if (!string.IsNullOrEmpty(timers)) { b.AppendLine("время систем (ЦП, мс):"); b.AppendLine(timers); }
            if (!string.IsNullOrEmpty(LastBench)) { b.AppendLine("последний автозамер:"); b.AppendLine(LastBench); }
            b.AppendLine("события:"); foreach (string e in events) b.AppendLine("  " + e);
            b.AppendLine("ошибки и предупреждения:"); if (errors.Count == 0) b.AppendLine("  нет"); foreach (string e in errors) b.AppendLine("  " + e);
            return b.ToString();
        }
    }
}
