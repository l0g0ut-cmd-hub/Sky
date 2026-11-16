using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Полный журнал мира (владелец 17.11: «видеть АБСОЛЮТНО ВСЁ только в логе»). Пишется всю сессию боя:
    ///  • МИР (в начале каждого куска): крепость, причал, зона ремонта, границы вод, строения с габаритами и цилиндрами
    ///    столкновений, все облака-спрайты (место, размер, вид, включено), море облаков (низ/верх), погода, ветер, солнце;
    ///  • КАДР каждую секунду: камера (место, курс, наклон, обзор, дистанция, режим — обычная/труба/удалённая),
    ///    флагман (место, курс, ход, рукоять, руль, корпус, повреждения руля/баллона, автобой и его состояние, цель, заряд,
    ///    режим залпа, готовность бортов, решение прицела), каждый корабль (класс, длина×ширина, место, курс, ход, корпус,
    ///    настрой, занятие/роль, состояние ИИ, в порту, дурень, нарушитель, с кем воюет), ядер в полёте, уведомление,
    ///    все подписи, нарисованные на экране в этом кадре (текст и место);
    ///  • СОБЫТИЯ сразу: выстрел (кто, откуда, куда, скорость, калибр), попадание (кто, по кому, где, урон), корпус
    ///    флагмана (каждое изменение с причиной), касания экрана (где и по чему), столкновения (кто с кем/чем, ход), снимки.
    /// Куски по 60 с → reports/world/wNN.txt (60 по кругу — последний час), reports/world/index.txt — что в каком куске.
    /// Координаты мировые, метры; курс — градусы по компасу (0 — север/+z, 90 — восток/+x).
    /// </summary>
    public class WorldLog : MonoBehaviour
    {
        public static WorldLog Instance { get; private set; }
        static readonly StringBuilder chunk = new StringBuilder(1 << 18);
        static readonly List<string> index = new List<string>();
        static float chunkStart = -1f, nextFrame;
        static int slot, chunkNo;
        public const int Slots = 60;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Подписи, нарисованные HUD в последнем кадре (ShipHud.CapText).
        public static readonly List<string> Labels = new List<string>(64);
        static readonly List<string> lastLabels = new List<string>(64);
        static int labelsFrame = -1;
        public static void Label(string text, float x, float y, float h)
        {
            if (!Application.isPlaying || Instance == null) return;
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            if (Time.frameCount != labelsFrame) { lastLabels.Clear(); lastLabels.AddRange(Labels); Labels.Clear(); labelsFrame = Time.frameCount; }
            if (Labels.Count < 64) Labels.Add("«" + text + "»@" + Mathf.RoundToInt(x) + "," + Mathf.RoundToInt(y) + "h" + Mathf.RoundToInt(h));
        }

        public static void Boot()
        {
            if (Instance != null) return;
            Instance = new GameObject("World Log").AddComponent<WorldLog>();
            DontDestroyOnLoad(Instance.gameObject);
            slot = PlayerPrefs.GetInt("SkyTide.WorldSlot", 0);
        }

        static string T() { return (Time.timeSinceLevelLoad).ToString("F1", Inv); }
        static string V(Vector3 p) { return Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z); }
        static int Yaw(Transform t) { return Mathf.RoundToInt(Mathf.Repeat(t.eulerAngles.y, 360f)); }
        static string F1(float f) { return f.ToString("F1", Inv); }

        /// <summary>Строка события как есть (ИИ, события, ошибки, звуки, промахи) — со временем боя.</summary>
        public static void Raw(string s)
        {
            Line(s);
            if (s.StartsWith("ОШИБКА")) AutoPhoto("ошибка");
            else if (s.Contains("ФЛАГМАН ПОТОПЛЕН")) AutoPhoto("флагман потоплен");
        }
        static void Line(string s) { if (Instance != null && chunk.Length < 4000000) chunk.Append(T()).Append(' ').Append(s).Append('\n'); }

        // ——— события ———
        public static void Shot(string who, Vector3 muzzle, Vector3 vel, float caliber, bool chain)
        {
            if (Instance == null) return;
            Vector3 h = vel; h.y = 0f;
            Line("ВЫСТРЕЛ " + (who ?? "?") + " из " + V(muzzle) + " курс " + Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg, 360f))
                 + " возв " + Mathf.RoundToInt(Mathf.Asin(Mathf.Clamp(vel.normalized.y, -1f, 1f)) * Mathf.Rad2Deg) + " v" + Mathf.RoundToInt(vel.magnitude)
                 + (caliber > 1.01f ? " кал" + F1(caliber) : "") + (chain ? " книппель" : ""));
        }
        public static void Hit(string who, string victim, Vector3 at, float dmg) { Line("ПОПАДАНИЕ " + (who ?? "?") + " → " + victim + " в " + V(at) + (dmg > 0f ? " урон " + F1(dmg) : "")); }
        public static string HullCause;
        public static void Touch(Vector2 p, string what) { Line("КАСАНИЕ " + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + " → " + what); }
        static readonly Dictionary<string, float> bumpAt = new Dictionary<string, float>();
        public static void Bump(string a, string b, Vector3 at, float speed)
        {
            if (Instance == null) return;
            string k = a + "|" + b; float t;
            if (bumpAt.TryGetValue(k, out t) && Time.time - t < 1f) return;
            bumpAt[k] = Time.time;
            Line("СТОЛКНОВЕНИЕ " + a + " ↔ " + b + " в " + V(at) + " ход " + F1(speed));
            if ((a == "ФЛАГМАН" || b == "ФЛАГМАН") && speed > 8f) AutoPhoto("столкновение флагмана: " + (a == "ФЛАГМАН" ? b : a));
        }
        public static void Mark(string what) { Line("МЕТКА " + what); if (Instance != null) Instance.Frame(); }

        // ——— имена ———
        public static string Name(TargetShip t) { return t == null ? "?" : t.name + "(" + (t.Class != ShipClass.None ? ShipClasses.Get(t.Class).name : "КОРСАР") + ")"; }
        public static string Name(AllyShip a) { return a == null ? "?" : a.name + "(" + (a.Class != ShipClass.None ? ShipClasses.Get(a.Class).name : "СВОЙ") + ")"; }

        float lastHull = -1f;
        void Update()
        {
            if (!Application.isPlaying || MainMenu.Active) return;
            if (chunkStart < 0f) { chunkStart = Time.time; World(); }
            PlayerHull ph = PlayerHull.Instance;
            if (ph != null)
            {
                float h = ph.Health;
                if (lastHull >= 0f && Mathf.Abs(h - lastHull) >= ph.maxHealth * 0.005f)
                {
                    Line("КОРПУС " + Mathf.RoundToInt(lastHull / ph.maxHealth * 100f) + "% → " + Mathf.RoundToInt(h / ph.maxHealth * 100f) + "% · " + (HullCause ?? (h > lastHull ? "ремонт?" : "урон?")));
                    HullCause = null;
                }
                lastHull = h;
            }
            if (Time.unscaledTime >= nextWatch) { nextWatch = Time.unscaledTime + 0.5f; Watch(); }
            if (Time.unscaledTime >= nextFrame) { nextFrame = Time.unscaledTime + 1f; Frame(); }
            if (Time.time - chunkStart >= 60f) Flush();
        }

        void Frame()
        {
            StringBuilder b = new StringBuilder(4096);
            Camera c = Camera.main;
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
            OrbitCamera oc = g != null ? g.Cam : null;
            b.Append("КАДР fps ").Append(tc != null ? Mathf.RoundToInt(tc.Fps) : 0);
            if (tc != null && tc.Fps > 1f && tc.Fps < 15f && Time.timeSinceLevelLoad > 20f) AutoPhoto("FPS " + Mathf.RoundToInt(tc.Fps));
            if (tc != null && tc.Paused) b.Append(" ПАУЗА");
            if (c != null)
            {
                b.Append(" | КАМЕРА ").Append(V(c.transform.position)).Append(" курс ").Append(Yaw(c.transform))
                 .Append(" накл ").Append(Mathf.RoundToInt(Mathf.DeltaAngle(0f, c.transform.eulerAngles.x))).Append(" обзор ").Append(Mathf.RoundToInt(c.fieldOfView));
                if (oc != null) b.Append(" орбита ").Append(Mathf.RoundToInt(oc.yaw)).Append('/').Append(Mathf.RoundToInt(oc.pitch)).Append(" дист ").Append(Mathf.RoundToInt(oc.distance)).Append(oc.ScopeOn ? " ТРУБА" : "");
                if (RemoteCam.State != "обычная") b.Append(" удалённая:").Append(RemoteCam.State);
            }
            PlayerHull ph = PlayerHull.Instance;
            if (g != null && g.Ship != null)
            {
                ShipController s = g.Ship;
                b.Append(" | ФЛАГМАН ").Append(V(s.transform.position)).Append(" курс ").Append(Yaw(s.transform)).Append(" ход ").Append(F1(s.Speed))
                 .Append(" рукоять ").Append(F1(s.throttle)).Append(" руль ").Append(F1(s.turn));
                if (tc != null) b.Append(" ступень ").Append(tc.Gear);
                if (ph != null) b.Append(" корпус ").Append(Mathf.RoundToInt(ph.Health01 * 100f)).Append('%').Append(ph.Alive ? "" : " ТОНЕТ");
                if (s.RudderHit) b.Append(" руль-повр"); if (s.BalloonHit) b.Append(" баллон-повр");
                if (tc != null && tc.AutoBattle) b.Append(" [").Append(tc.AutoState).Append(']');
                if (RemotePilot.Active) b.Append(" пилот:").Append(RemotePilot.State);
                Broadside bs = s.GetComponent<Broadside>();
                if (bs != null)
                {
                    IAimTarget lk = bs.Locked;
                    b.Append(" цель ").Append(lk == null ? "-" : lk is TargetShip ? Name((TargetShip)lk) : lk is AllyShip ? Name((AllyShip)lk) : "?");
                    b.Append(" наведён ").Append(bs.AimTarget == null ? "-" : bs.AimTarget is TargetShip ? Name((TargetShip)bs.AimTarget) : Name(bs.AimTarget as AllyShip));
                    b.Append(" борт ").Append(bs.AimSide).Append(bs.AimOnTarget ? " в-прицеле" : "").Append(bs.HasSolution ? " решение" : "")
                     .Append(" Л").Append(Mathf.RoundToInt(bs.Readiness(-1) * 100f)).Append(" П").Append(Mathf.RoundToInt(bs.Readiness(1) * 100f))
                     .Append(bs.ripple ? " волной" : " весь-борт");
                }
            }
            Waters wv = g != null && g.Ship != null ? Relations.At(g.Ship.transform.position) : Waters.Neutral;
            b.Append(" | воды ").Append(wv).Append(" вне-закона ").Append(Relations.PlayerOutlaw ? "да" : "нет").Append(" злоба ").Append(Mathf.RoundToInt(Relations.Grudge));
            if (FxSystem.Instance != null) b.Append(" | частиц ").Append(FxSystem.Instance.ParticleCount);
            if (Time.time - HudNotice.At < HudNotice.Hold) b.Append(" | УВЕДОМЛЕНИЕ «").Append(HudNotice.Text).Append(HudNotice.Sub != "" ? " / " + HudNotice.Sub : "").Append('»');
            Line(b.ToString());

            // Корабли — по строке.
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                StringBuilder e = new StringBuilder(200);
                e.Append("  К ").Append(Name(t)).Append(' ').Append(F1(t.Length)).Append('×').Append(F1(t.HullHalfWidth * 2f)).Append("м ");
                if (!t.Alive) { e.Append("ПОТОПЛЕН"); chunk.Append(e).Append('\n'); continue; }
                e.Append(V(t.transform.position)).Append(" курс ").Append(Yaw(t.transform)).Append(" ход ").Append(t.Controller != null ? F1(t.Controller.Speed) : "?")
                 .Append(' ').Append(Mathf.RoundToInt(t.Health01 * 100f)).Append("% ").Append(t.Mood).Append(' ').Append(t.duty).Append(' ').Append(t.temper)
                 .Append(" ИИ:").Append(t.AiState);
                if (t.InPort) e.Append(" в-порту"); if (t.Fool) e.Append(" дурень"); if (t.Outlaw) e.Append(" НАРУШИТЕЛЬ"); if (t.Raiding) e.Append(" НАБЕГ");
                if (t.HostileToPlayer) e.Append(" враг-флагману"); if (t.FoeAlly != null) e.Append(" воюет-с ").Append(Name(t.FoeAlly));
                e.Append(" воды ").Append(Relations.At(t.transform.position));
                Extra(e, t.transform, t.Controller, t.Goal, t.DamageVisual);
                EnemyGunner eg = t.GetComponent<EnemyGunner>(); if (eg != null && eg.Hold.Length > 0) e.Append(" пушки: ").Append(eg.Hold);
                e.Append(" | ").Append(t.Timers());
                chunk.Append(e).Append('\n');
            }
            foreach (AllyShip a in AllyShip.All)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                StringBuilder e = new StringBuilder(160);
                e.Append("  С ").Append(Name(a)).Append(' ').Append(a.role).Append(' ').Append(F1(a.Length)).Append('×').Append(F1(a.HullHalfWidth * 2f)).Append("м ");
                if (!a.Alive) { e.Append("ПОТОПЛЕН"); chunk.Append(e).Append('\n'); continue; }
                e.Append(V(a.transform.position)).Append(" курс ").Append(Yaw(a.transform)).Append(" ход ").Append(a.Controller != null ? F1(a.Controller.Speed) : "?")
                 .Append(' ').Append(Mathf.RoundToInt(a.Health01 * 100f)).Append("% ИИ:").Append(a.AiState);
                if (a.Foe != null) e.Append(" бьёт ").Append(Name(a.Foe));
                if (a.HostileToPlayer) e.Append(" ЗОЛ-НА-ФЛАГМАН");
                Extra(e, a.transform, a.Controller, a.Goal, a.GetComponentInChildren<ShipDamageVisual>());
                if (a.Hold.Length > 0) e.Append(" пушки: ").Append(a.Hold);
                chunk.Append(e).Append('\n');
            }
            if (lastLabels.Count > 0) chunk.Append("  ПОДПИСИ ").Append(string.Join(" ", lastLabels.ToArray())).Append('\n');
            // HUD, меню, настройки, техника.
            StringBuilder h = new StringBuilder(512);
            h.Append("  HUD меню:").Append(MainMenu.Active ? "главное" : tc != null && tc.Paused ? "пауза" : "нет").Append(" скрыт:").Append(TouchControls.HideHud ? "да" : "нет");
            if (tc != null)
                h.Append(" звук:").Append(tc.SoundOn ? "вкл" : "выкл").Append(" камера-скор:").Append(tc.CameraSpeed).Append(" кнопки:").Append(tc.BigButtons ? "крупные" : "обычные")
                 .Append(" вибрация:").Append(Haptics.Enabled ? "вкл" : "выкл").Append(" радар:").Append(Mathf.RoundToInt(tc.RadarRange)).Append(" штурвал:").Append(tc.StickActive ? "держат" : "нет")
                 .Append(" подсказка:").Append(tc.HintAlpha > 0.05f ? tc.HintStage.ToString() : "нет");
            PlayerBoarding pb = g != null && g.Ship != null ? g.Ship.GetComponent<PlayerBoarding>() : null;
            if (pb != null) h.Append(" абордаж: ").Append(pb.Active ? "идёт " + Mathf.RoundToInt(pb.Progress01 * 100f) + "%" : pb.Candidate != null ? "можно " + Name(pb.Candidate) : "нет");
            if (ph != null) h.Append(" флагман-пробоин:").Append(ph.VentCount);
            string q = HudNotice.QueueText(); if (q.Length > 0) h.Append(" очередь:").Append(q);
            h.Append(" | 60fps:").Append(SkyTideBootstrap.Fps60 ? "да" : "нет").Append(" тени:").Append(QualitySettings.shadows).Append(" timeScale:").Append(F1(Time.timeScale));
            if (DebugRecorder.Instance != null) h.Append(" | ").Append(DebugRecorder.Instance.RenderLine());
            h.Append(" кадр ").Append(F1(Time.unscaledDeltaTime * 1000f)).Append("мс gc0:").Append(System.GC.CollectionCount(0));
            if (RemoteCommands.Running) h.Append(" | КОМАНДЫ CLAUDE идут");
            chunk.Append(h).Append('\n');
            // Свет.
            Light sun = RenderSettings.sun;
            chunk.Append("  СВЕТ солнце ").Append(sun != null ? Yaw(sun.transform) + "/" + Mathf.RoundToInt(sun.transform.eulerAngles.x) + " ярк " + F1(sun.intensity) : "-")
                 .Append(" туман ").Append(RenderSettings.fog ? RenderSettings.fogDensity.ToString("F5", Inv) : "выкл").Append(" погода ").Append(WeatherSystem.Instance != null ? WeatherSystem.Names[(int)WeatherSystem.Instance.Current] : "-").Append('\n');
        }

        /// <summary>Высота, крен, дифферент, точка назначения, повреждения, место на экране, видимость.</summary>
        static void Extra(StringBuilder e, Transform t, ShipController c, Vector3 goal, ShipDamageVisual dv)
        {
            if (c != null && c.visual != null)
            {
                Vector3 ve = c.visual.localEulerAngles;
                e.Append(" крен ").Append(Mathf.RoundToInt(Mathf.DeltaAngle(0f, ve.z))).Append(" диф ").Append(Mathf.RoundToInt(Mathf.DeltaAngle(0f, ve.x)));
                e.Append(" рук ").Append(F1(c.throttle)).Append(" руль ").Append(F1(c.turn));
                if (c.RudderHit) e.Append(" руль-повр"); if (c.BalloonHit) e.Append(" баллон-повр");
            }
            if (goal.sqrMagnitude > 1f) e.Append(" идёт-к ").Append(Mathf.RoundToInt(goal.x)).Append(',').Append(Mathf.RoundToInt(goal.z)).Append(" (").Append(Mathf.RoundToInt(new Vector2(goal.x - t.position.x, goal.z - t.position.z).magnitude)).Append(" м)");
            if (dv != null) e.Append(" пробоин ").Append(dv.ScarCount).Append(" дым ").Append(dv.VentCount).Append(dv.Burning ? " ГОРИТ" : "");
            Camera cam = Camera.main;
            if (cam != null)
            {
                Renderer best = null; int lod = -1, k = 0;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff || r is ParticleSystemRenderer) continue;
                    if (best == null || r.bounds.size.sqrMagnitude > best.bounds.size.sqrMagnitude) { best = r; lod = r.name.Contains("Far") ? 2 : r.name.Contains("LOD") || r.name.Contains("Lite") ? 1 : 0; }
                    k++;
                }
                if (best != null)
                {
                    Bounds b = best.bounds; float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f; bool front = false;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 sp = cam.WorldToScreenPoint(p); if (sp.z <= 0f) continue; front = true;
                        x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x); y0 = Mathf.Min(y0, Screen.height - sp.y); y1 = Mathf.Max(y1, Screen.height - sp.y);
                    }
                    bool on = front && x1 > 0 && x0 < Screen.width && y1 > 0 && y0 < Screen.height;
                    e.Append(on ? " ЭКРАН " + Mathf.RoundToInt(x0) + "," + Mathf.RoundToInt(y0) + "–" + Mathf.RoundToInt(x1) + "," + Mathf.RoundToInt(y1) + " модель " + (lod == 2 ? "дальняя" : lod == 1 ? "лёгкая" : "полная") : " вне-кадра");
                    if (on)
                    {
                        Vector3 cp = cam.transform.position, to = b.center - cp; RaycastHit rh;
                        if (Physics.Raycast(cp, to.normalized, out rh, to.magnitude - b.extents.magnitude * 0.5f) && !rh.transform.IsChildOf(t)) e.Append(" заслонён ").Append(rh.transform.name);
                    }
                }
            }
        }

        // Застревание, смена цели и хода — проверка каждые 0,5 с.
        readonly Dictionary<Transform, float> stuckSince = new Dictionary<Transform, float>();
        IAimTarget lastLock; int lastGear = -1; float nextWatch;
        void Watch()
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
            string src = RemoteCommands.Running ? "Claude" : tc != null && tc.AutoBattle ? "автобой" : "игрок";
            if (g != null && g.Ship != null)
            {
                Broadside bs = g.Ship.GetComponent<Broadside>();
                if (bs != null && bs.Locked != lastLock) { Line("ЦЕЛЬ " + (bs.Locked == null ? "снята" : bs.Locked is TargetShip ? Name((TargetShip)bs.Locked) : Name(bs.Locked as AllyShip)) + " · " + src); lastLock = bs.Locked; }
                if (tc != null && tc.Gear != lastGear) { if (lastGear >= 0) Line("СТУПЕНЬ " + lastGear + " → " + tc.Gear + " · " + src); lastGear = tc.Gear; }
                Stuck("ФЛАГМАН", g.Ship.transform, g.Ship);
            }
            foreach (TargetShip t in TargetShip.All) if (t != null && t.Alive && !t.Surrendered) Stuck(Name(t), t.transform, t.Controller);
            foreach (AllyShip a in AllyShip.All) if (a != null && a.Alive) Stuck(Name(a), a.transform, a.Controller);
        }
        void Stuck(string who, Transform t, ShipController c)
        {
            if (c == null) return;
            bool s = Mathf.Abs(c.throttle) > 0.25f && Mathf.Abs(c.Speed) < 1.5f;
            float since;
            if (!s) { if (stuckSince.TryGetValue(t, out since) && since < 0f) Line("ВЫБРАЛСЯ " + who); stuckSince.Remove(t); return; }
            if (!stuckSince.TryGetValue(t, out since)) { stuckSince[t] = Time.time; return; }
            if (since > 0f && Time.time - since > 3f)
            {
                stuckSince[t] = -1f;   // уже сообщили
                Line("ЗАСТРЯЛ " + who + " в " + V(t.position) + " рукоять " + F1(c.throttle) + " рядом: " + Nearby(t));
                if (who == "ФЛАГМАН") AutoPhoto("застрял флагман");
            }
        }
        static string Nearby(Transform t)
        {
            StringBuilder b = new StringBuilder();
            Vector3 p = t.position;
            foreach (TargetShip o in TargetShip.All) if (o != null && o.Alive && o.transform != t && (o.transform.position - p).sqrMagnitude < 250f * 250f) b.Append(Name(o)).Append(' ').Append(Mathf.RoundToInt((o.transform.position - p).magnitude)).Append("м; ");
            foreach (AllyShip o in AllyShip.All) if (o != null && o.Alive && o.transform != t && (o.transform.position - p).sqrMagnitude < 250f * 250f) b.Append(Name(o)).Append(' ').Append(Mathf.RoundToInt((o.transform.position - p).magnitude)).Append("м; ");
            foreach (WorldProps.Solid s in WorldProps.Solids) { float d = new Vector2(s.c.x - p.x, s.c.z - p.z).magnitude - s.r; if (d < 150f) b.Append(s.name).Append(' ').Append(Mathf.RoundToInt(d)).Append("м; "); }
            SkyFortress f = SkyFortress.Instance;
            if (f != null) { float d = new Vector2(f.Center.x - p.x, f.Center.z - p.z).magnitude; if (d < f.BoundRadius + 200f) b.Append("крепость ").Append(Mathf.RoundToInt(d)).Append("м от центра; высота стен тут ").Append(Mathf.RoundToInt(f.TopAt(p))).Append("; "); }
            PlayerHull ph = PlayerHull.Instance; if (ph != null && ph.transform != t && (ph.transform.position - p).sqrMagnitude < 250f * 250f) b.Append("ФЛАГМАН ").Append(Mathf.RoundToInt((ph.transform.position - p).magnitude)).Append("м; ");
            return b.Length > 0 ? b.ToString() : "ничего";
        }

        // Автоснимок при важных событиях — не чаще раза в 20 с.
        static float nextAuto;
        public static void AutoPhoto(string why)
        {
            if (Time.unscaledTime < nextAuto || ReportUploader.Instance == null) return;
            nextAuto = Time.unscaledTime + 20f;
            Line("АВТОСНИМОК " + why);
            ReportUploader.Instance.Photo("авто: " + why, true);
        }

        /// <summary>Неподвижное и медленное: крепость, воды, строения, облака, море, погода.</summary>
        void World()
        {
            StringBuilder b = new StringBuilder(16384);
            b.Append("МИР\n");
            if (chunkNo == 0)
            {
                b.Append("  СЕССИЯ сборка ").Append(DebugReport.BuildNo).Append(' ').Append(SystemInfo.deviceModel).Append(' ').Append(SystemInfo.graphicsDeviceName).Append(" экран ").Append(Screen.width).Append('×').Append(Screen.height)
                 .Append(" безоп ").Append(Screen.safeArea).Append(" качество ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append(" цель-кадров ").Append(Application.targetFrameRate).Append('\n');
                b.Append("  КОНФИГ ").Append(JsonUtility.ToJson(RemoteConfig.Current)).Append('\n');
            }
            SkyFortress f = SkyFortress.Instance;
            if (f != null) b.Append("  КРЕПОСТЬ центр ").Append(V(f.Center)).Append(" радиус ").Append(Mathf.RoundToInt(f.BoundRadius)).Append(" причал ").Append(V(f.BerthWorld))
                            .Append(" ремонт ").Append(V(f.RepairCentre)).Append(" овал ").Append(Mathf.RoundToInt(SkyFortress.RepairAlong)).Append('×').Append(Mathf.RoundToInt(SkyFortress.RepairOut))
                            .Append(" зона-крепости ").Append(Mathf.RoundToInt(f.safeRadius)).Append('\n');
            b.Append("  ВОДЫ гарнизон до ").Append(Mathf.RoundToInt(Relations.GarrisonRadius)).Append(" корсары от ").Append(Mathf.RoundToInt(Relations.CorsairRadius)).Append(" (от крепости)\n");
            if (WorldProps.Instance != null)
                foreach (Transform p in WorldProps.Instance.transform)
                {
                    Bounds bb = new Bounds(p.position, Vector3.zero); bool any = false;
                    foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                    b.Append("  СТРОЕНИЕ ").Append(p.name.Replace("Prop ", "")).Append(" основание ").Append(V(p.position)).Append(" курс ").Append(Yaw(p))
                     .Append(" габарит ").Append(V(bb.min)).Append("…").Append(V(bb.max)).Append('\n');
                }
            foreach (WorldProps.Solid s in WorldProps.Solids)
                b.Append("  ТВЕРДЬ ").Append(s.name).Append(" круг ").Append(Mathf.RoundToInt(s.c.x)).Append(',').Append(Mathf.RoundToInt(s.c.z)).Append(" r").Append(F1(s.r))
                 .Append(" y ").Append(Mathf.RoundToInt(s.y0)).Append("…").Append(Mathf.RoundToInt(s.y1)).Append('\n');
            VolumetricClouds vc = FindObjectOfType<VolumetricClouds>();
            if (vc != null) b.Append("  МОРЕ-ОБЛАКОВ объёмное ").Append(vc.isActiveAndEnabled ? "вкл" : "выкл").Append(" низ ").Append(Mathf.RoundToInt(vc.deckBase)).Append(" верх ").Append(Mathf.RoundToInt(vc.deckTop)).Append('\n');
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (g != null && g.World != null) g.World.DumpAll(b);
            WeatherSystem ws = WeatherSystem.Instance;
            if (ws != null) b.Append("  ПОГОДА ").Append(WeatherSystem.Names[(int)ws.Current]).Append('\n');
            Light sun = RenderSettings.sun;
            if (sun != null) b.Append("  СОЛНЦЕ курс ").Append(Yaw(sun.transform)).Append(" высота ").Append(Mathf.RoundToInt(sun.transform.eulerAngles.x)).Append('\n');
            chunk.Append(b);
        }

        void Flush()
        {
            ReportUploader u = ReportUploader.Instance;
            string head = "=== журнал мира · сборка " + DebugReport.BuildNo + " · кусок " + chunkNo + " · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n";
            if (u != null) u.Put("reports/world/w" + slot.ToString("00") + ".txt", Encoding.UTF8.GetBytes(head + chunk.ToString()));
            index.Add("w" + slot.ToString("00") + " · кусок " + chunkNo + " · бой " + Mathf.RoundToInt(chunkStart) + "–" + Mathf.RoundToInt(Time.time) + " с · " + System.DateTime.Now.ToString("HH:mm:ss"));
            if (index.Count > Slots) index.RemoveAt(0);
            if (u != null) u.Put("reports/world/index.txt", Encoding.UTF8.GetBytes(string.Join("\n", index.ToArray()) + "\n"));
            slot = (slot + 1) % Slots; chunkNo++;
            PlayerPrefs.SetInt("SkyTide.WorldSlot", slot);
            chunk.Length = 0; chunkStart = Time.time;
            World();
        }

        void OnApplicationPause(bool p) { Line(p ? "ИГРА СВЁРНУТА" : "ИГРА РАЗВЁРНУТА"); if (p && chunk.Length > 0) Flush(); }
        void OnApplicationFocus(bool f) { Line(f ? "ФОКУС ЕСТЬ" : "ФОКУС ПОТЕРЯН"); }
        void OnApplicationQuit() { if (chunk.Length > 0) Flush(); }
    }
}
