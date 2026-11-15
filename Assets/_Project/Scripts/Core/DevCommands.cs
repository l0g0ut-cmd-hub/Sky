using System.Text;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Команды отладки — одни и те же для меню тестера (панель FPS → «Тест») и для удалённых команд
    /// (Sky-reports/config/commands.json, см. RemoteCommands). Каждая — событие отчёта «ТЕСТ: …» и счётчик «тест-команд».
    /// Строка: «имя [аргументы]». help — список.
    /// </summary>
    public static class DevCommands
    {
        public const string Help =
            "near — ближний враг на траверз 450 м · enemy15 — ближнему врагу 15% · god on|off · time 1|3|0.5 · berth — к причалу · " +
            "repair · hurt 25 — флагману 25% · weather storm|haze|clear · merchant — враг к торговцу · all1km — все враги в 1 км · " +
            "auto on|off — автобой · config — перечитать config.json · cfg поле число — правка баланса на лету · " +
            "shot [почему] — метка со снимком · report — отчёт сейчас · bench — автозамер · " +
            "snap N — снимок каждые N с · burst кадров шаг [подпись] — серия · " +
            "view угол наклон дист [сек] · spin градусов сек — обычная камера · " +
            "play — «В БОЙ» из меню · tap pause|row N|radar|ammo|board|spy|auto|ripple|left|right|fps|gear N|xy 0.5,0.5 — нажатие как пальцем · " +
            "where — координаты всех · pilot goto x z [ход]|heading град|speed|stop|release · go цель [N] [ход] · tp x z [курс] · " +
            "cam orbit цель [N] угол наклон дист|at x y z курс наклон|look x y z цель [N]|fov N|height N|off · hud 0|1 · photo [full] подпись · " +
            "цели: me fortress berth near enemy N ally N merchant · set clouds|fps60|post|crew|sharpen|fortress|fortshadow|lod|fx 0|1 · set shadows 0|1|2 · fps N — замер N с (в отчёт) · say текст — надпись на экране";

        public static float TimeScaleSet = 1f;
        static readonly StringBuilder measures = new StringBuilder();
        public static string Settings()
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            SkyTidePost p = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
            VolumetricClouds vc = g != null ? g.Volumetric : null;
            string vol = vc != null && vc.enabled ? "об(башни " + (vc.debugTowers ? 1 : 0) + " море " + (vc.debugDeck ? 1 : 0) + " имп " + (vc.impostors ? 1 : 0) + ")" : "рис";
            return "облака " + vol + " тени " + QualitySettings.shadows + " пост " + (p != null && p.enabled)
                + " резк " + SkyTidePost.Sharpen + " крепость " + (g != null && g.Fortress != null && g.Fortress.Visible) + " LOD " + ShipLod.Enabled
                + " темп " + (DebugRecorder.Instance != null ? DebugRecorder.Instance.BatteryTemp.ToString("F1") : "?");
        }

        /// <summary>Выполнить команду; вернуть короткий итог для отчёта.</summary>
        public static string Run(string line)
        {
            if (string.IsNullOrEmpty(line)) return "пусто";
            line = line.Trim();
            string[] a = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].ToLowerInvariant();
            string arg = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            string result;
            try { result = Exec(cmd, arg, a, line); }
            catch (System.Exception e) { result = "ошибка: " + e.GetType().Name + " " + e.Message; }
            DebugReport.Event("ТЕСТ: " + line + " → " + result);
            DebugReport.Count("тест-команд");
            if (!ControlBanner.Busy) HudNotice.Show("ТЕСТ: " + line, 0, 2f);   // во время удалённой проверки — табличка, без надписей в кадре
            return result;
        }

        static TargetShip Nearest(Transform me)
        {
            TargetShip near = null; float nd = float.MaxValue;
            if (me == null) return null;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive || !t.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(t.transform.position, me.position);
                if (d < nd) { nd = d; near = t; }
            }
            return near;
        }

        static string Exec(string cmd, string arg, string[] a, string line)
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            Transform me = g != null && g.Ship != null ? g.Ship.transform : null;
            PlayerHull ph = PlayerHull.Instance;
            TargetShip near = Nearest(me);
            WeatherSystem ws = WeatherSystem.Instance;
            switch (cmd)
            {
                case "help": return Help;
                case "near":
                    if (me == null || near == null) return "нет врага";
                    near.transform.SetPositionAndRotation(me.position + me.right * 450f + me.forward * 60f, me.rotation);
                    return near.name + " на траверзе";
                case "enemy15":
                    if (near == null) return "нет врага";
                    near.SetHealth01(0.15f); return near.name + " 15%";
                case "god": PlayerHull.God = arg != "off" && arg != "0" && (arg != "" || !PlayerHull.God); return "бессмертие " + (PlayerHull.God ? "вкл" : "выкл");
                case "time":
                    {
                        float k = 1f; float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out k);
                        k = Mathf.Clamp(k <= 0f ? 1f : k, 0.1f, 5f); TimeScaleSet = k; Time.timeScale = k; return "время ×" + k;
                    }
                case "berth":
                    {
                        SkyFortress f = SkyFortress.Instance;
                        if (me == null || f == null) return "нет крепости";
                        Vector3 o = f.BerthWorld - f.Center; o.y = 0f; o = o.sqrMagnitude > 1f ? o.normalized : Vector3.forward;
                        Vector3 at = f.BerthWorld + o * 30f; at.y = me.position.y; me.SetPositionAndRotation(at, Quaternion.LookRotation(o, Vector3.up));
                        return "у причала";
                    }
                case "repair": if (ph == null) return "нет флагмана"; ph.Repair(ph.maxHealth); return "флагман 100%";
                case "hurt":
                    {
                        if (ph == null) return "нет флагмана";
                        float p = 25f; float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p);
                        ph.OnHitDamageOnly(ph.Health - ph.maxHealth * Mathf.Clamp01(p / 100f)); return "флагман " + Mathf.RoundToInt(ph.Health01 * 100f) + "%";
                    }
                case "weather":
                    if (ws == null) return "нет погоды";
                    ws.Force(arg == "storm" ? WeatherSystem.Kind.Storm : arg == "haze" ? WeatherSystem.Kind.Haze : WeatherSystem.Kind.Clear);
                    return "погода " + WeatherSystem.Names[(int)ws.Current];
                case "merchant":
                    if (near == null) return "нет врага";
                    foreach (AllyShip al in AllyShip.All)
                        if (al != null && al.Alive && al.role == AllyShip.Role.Merchant) { near.transform.position = al.transform.position + al.transform.right * 350f; return near.name + " у торговца"; }
                    return "нет торговца";
                case "all1km":
                    {
                        if (me == null) return "нет флагмана";
                        int k = 0;
                        foreach (TargetShip t in TargetShip.All)
                        {
                            if (t == null || !t.Alive || !t.gameObject.activeInHierarchy) continue;
                            float ang = k++ * 1.1f; t.transform.position = me.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1000f;
                        }
                        return "врагов в 1 км: " + k;
                    }
                case "auto":
                    {
                        TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
                        if (tc == null) return "нет управления";
                        tc.SetAutoBattle(arg != "off" && arg != "0"); return "автобой " + (tc.AutoBattle ? "вкл" : "выкл");
                    }
                case "config": if (RemoteConfig.Instance == null) return "нет конфига"; RemoteConfig.Instance.Reload(); return "перечитываю";
                case "cfg":
                    {
                        if (a.Length < 3) return "cfg поле число";
                        var f = typeof(RemoteConfig.Data).GetField(a[1]);
                        if (f == null) return "нет поля " + a[1];
                        object v;
                        if (f.FieldType == typeof(int)) v = int.Parse(a[2]);
                        else if (f.FieldType == typeof(float)) v = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                        else if (f.FieldType == typeof(bool)) v = a[2] == "1" || a[2].ToLowerInvariant() == "true";
                        else v = a[2];
                        f.SetValue(RemoteConfig.Current, v); RemoteConfig.Apply();
                        RemoteConfig.Status = "конфиг: версия " + RemoteConfig.Current.version + " + правки на лету";
                        return a[1] + " = " + v;
                    }
                case "shot": case "mark":
                    if (ReportUploader.Instance == null) return "нет отправки";
                    ReportUploader.Instance.Mark(line.Length > cmd.Length ? "команда: " + line.Substring(cmd.Length).Trim() : "удалённая команда"); return "метка";
                case "report": if (ReportUploader.Instance == null) return "нет отправки"; ReportUploader.Instance.UploadReport(); return "отчёт отправлен";
                case "bench": if (PerfPanel.Instance == null) return "нет панели"; PerfPanel.Instance.StartBench(); return "автозамер";
                case "fps": if (PerfPanel.Instance == null) return "нет панели"; PerfPanel.Instance.StartCoroutine(Measure(arg)); return "замер";
                case "where": return Where(me);
                case "pilot": return Pilot(arg, a);
                case "go":
                    {
                        Transform t = Find(arg, a.Length > 2 ? a[2] : null);
                        if (t == null) return "не нашёл " + arg;
                        RemotePilot.GoTo(t, Num(a, a.Length > 3 ? 3 : 99, 0.7f), arg); return RemotePilot.State;
                    }
                case "duel":
                    {
                        Transform t = Find(arg == "" ? "near" : arg, a.Length > 2 ? a[2] : null);
                        if (t == null) return "нет цели";
                        RemotePilot.Duel(t, arg == "" ? "near" : arg); return RemotePilot.State;
                    }
                case "tp":
                    {
                        if (me == null || a.Length < 3) return "tp x z [курс]";
                        Vector3 p = new Vector3(Num(a, 1, 0f), me.position.y, Num(a, 2, 0f));
                        me.SetPositionAndRotation(p, Quaternion.Euler(0f, Num(a, 3, me.eulerAngles.y), 0f)); return "флагман в " + p.x + "," + p.z;
                    }
                case "cam": return Cam(arg, a);
                case "hud": TouchControls.HideHud = arg == "0" || arg == "off"; return "HUD " + (TouchControls.HideHud ? "скрыт" : "виден");
                case "photo":
                    {
                        if (ReportUploader.Instance == null) return "нет отправки";
                        bool full = arg == "full";
                        string label = line.Length > 5 ? line.Substring(5).Trim() : "снимок";
                        ReportUploader.Instance.Photo(label, full); return "снимок";
                    }
                case "tap":
                    {
                        TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
                        if (tc == null) return "нет управления";
                        return tc.RemoteTap(arg, a.Length > 2 ? a[2] : null);
                    }
                case "view":   // view угол наклон дистанция [секунды] — обычная игровая камера, как пальцами (плавно)
                    {
                        OrbitCamera oc = g != null ? g.Cam : null;
                        if (oc == null || PerfPanel.Instance == null) return "нет камеры";
                        PerfPanel.Instance.StartCoroutine(View(oc, Num(a, 1, oc.yaw), Num(a, 2, oc.pitch), Num(a, 3, oc.distance), Num(a, 4, 1.5f)));
                        return "камера: угол " + a[1] + (a.Length > 2 ? " наклон " + a[2] : "") + (a.Length > 3 ? " дист " + a[3] : "");
                    }
                case "spin":   // spin градусов секунд — облёт обычной камерой вокруг корабля
                    {
                        OrbitCamera oc = g != null ? g.Cam : null;
                        if (oc == null || PerfPanel.Instance == null) return "нет камеры";
                        PerfPanel.Instance.StartCoroutine(View(oc, oc.yaw + Num(a, 1, 360f), oc.pitch, oc.distance, Num(a, 2, 8f), true));
                        return "облёт " + Num(a, 1, 360f) + "°";
                    }
                case "snap": ReportUploader.SnapEvery = Mathf.Max(0f, Num(a, 1, 0f)); return ReportUploader.SnapEvery > 0f ? "снимок каждые " + ReportUploader.SnapEvery + " с" : "снимки по таймеру выкл";
                case "burst":   // burst кадров шаг_с [подпись] — серия для оценки движения
                    if (ReportUploader.Instance == null) return "нет отправки";
                    ReportUploader.Instance.Burst((int)Num(a, 1, 10f), Num(a, 2, 0.15f), a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "серия");
                    return "серия " + Num(a, 1, 10f) + " кадров";
                case "play": return MainMenu.RemotePlay() ? "меню → В БОЙ" : (MainMenu.Active ? "меню не готово" : "уже в бою");
                case "say": HudNotice.Show(line.Substring(3).Trim(), 0, 6f); return "показано";
                case "set": return Set(arg, a.Length > 2 ? a[2] : "1", g);
            }
            return "нет команды " + cmd;
        }

        static string Set(string what, string val, SkyTideBootstrap g)
        {
            bool on = val != "0" && val != "off";
            switch (what)
            {
                case "clouds": if (g == null) return "нет игры"; g.ChooseVolumetric(on); return "объёмные облака " + on;
                case "fps60": SkyTideBootstrap.ChooseFps60(on); return "60 кадров " + on;
                case "shadows":
                    QualitySettings.shadows = val == "0" ? ShadowQuality.Disable : val == "1" ? ShadowQuality.HardOnly : ShadowQuality.All; return "тени " + QualitySettings.shadows;
                case "post":
                    {
                        SkyTidePost p = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
                        if (p == null) return "нет поста"; p.enabled = on; return "пост " + on;
                    }
                case "sharpen": SkyTidePost.Sharpen = on; return "резкость " + on;
                case "fortress": if (g == null || g.Fortress == null) return "нет крепости"; g.Fortress.Visible = on; return "крепость видна " + on;
                case "fortshadow": SkyFortress.ShadowsFar = on; return "тень крепости издалека " + on;
                case "lod": ShipLod.Enabled = on; return "лёгкие модели " + on;
                case "sea": if (g == null || g.World == null) return "нет мира"; g.World.SetSeaVisible(on); return "рисованное море облаков " + on;
                case "sprites": if (g == null || g.World == null) return "нет мира"; g.World.SetSpriteCloudsVisible(on); return "рисованные облака-спрайты " + on;
                case "far": { float k; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out k)) ShipLod.FarScale = Mathf.Clamp(k, 0.2f, 20f); return "дальний уровень ×" + ShipLod.FarScale; }
                case "cull": { float d; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ShipLod.CullDistance = Mathf.Max(300f, d); return "не рисовать дальше " + ShipLod.CullDistance + " м"; }
                case "fx": if (g == null || g.Fx == null) return "нет эффектов"; g.Fx.debugHidden = !on; return "эффекты " + on;
                case "crew":
                    {
                        ShipCrew c = g != null && g.Ship != null ? g.Ship.GetComponent<ShipCrew>() : null;
                        if (c == null) return "нет экипажа"; c.SetVisible(on); return "экипаж " + on;
                    }
            }
            return "нет настройки " + what;
        }

        /// <summary>Замер FPS и ВК за N секунд (для A/B удалённо: set … → wait → fps 8).</summary>
        static System.Collections.IEnumerator Measure(string arg)
        {
            float s = 8f; float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
            s = Mathf.Clamp(s <= 0f ? 8f : s, 2f, 60f);
            yield return new WaitForSecondsRealtime(1f);
            float t0 = Time.realtimeSinceStartup; int frames = 0; double gpu = 0; int gpuN = 0;
            FrameTiming[] ft = new FrameTiming[1];
            while (Time.realtimeSinceStartup - t0 < s)
            {
                yield return null; frames++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0) { gpu += ft[0].gpuFrameTime; gpuN++; }
            }
            string r = "ЗАМЕР " + s.ToString("F0") + " с: FPS " + (frames / (Time.realtimeSinceStartup - t0)).ToString("F1") + " · ВК " + (gpuN > 0 ? (gpu / gpuN).ToString("F1") : "н/д") + " мс";
            DebugReport.Event(r);
            measures.AppendLine(System.DateTime.Now.ToString("HH:mm:ss") + " " + r + " · " + Settings());
            if (ReportUploader.Instance != null) ReportUploader.Instance.Put("reports/ab.txt", System.Text.Encoding.UTF8.GetBytes("замеры этой сессии (сборка " + DebugReport.BuildNo + "):\n" + measures));
        }

        static float Num(string[] a, int i, float def)
        {
            float v; return i < a.Length && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
        }

        /// <summary>Объект по имени: me, fortress, berth, near (ближний враг), enemy N, ally N, merchant.</summary>
        static Transform Find(string what, string n)
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            Transform me = g != null && g.Ship != null ? g.Ship.transform : null;
            int k; int.TryParse(n, out k);
            switch (what)
            {
                case "me": case "flagship": return me;
                case "fortress": case "berth":
                    {
                        SkyFortress f = SkyFortress.Instance; if (f == null) return null;
                        GameObject marker = GameObject.Find(what == "berth" ? "SkyTide.BerthMarker" : "SkyTide.FortressMarker");
                        if (marker == null) marker = new GameObject(what == "berth" ? "SkyTide.BerthMarker" : "SkyTide.FortressMarker");
                        marker.transform.position = what == "berth" ? f.BerthWorld : f.Center;
                        return marker.transform;
                    }
                case "near": { TargetShip t = Nearest(me); return t != null ? t.transform : null; }
                case "enemy": { TargetShip.Created.RemoveAll(x => x == null); return k >= 1 && k <= TargetShip.Created.Count ? TargetShip.Created[k - 1].transform : null; }
                case "ally": { int i = 0; foreach (AllyShip al in AllyShip.All) if (al != null && ++i == k) return al.transform; return null; }
                case "merchant": foreach (AllyShip al in AllyShip.All) if (al != null && al.role == AllyShip.Role.Merchant) return al.transform; return null;
            }
            return null;
        }

        static string Where(Transform me)
        {
            StringBuilder b = new StringBuilder();
            if (me != null) b.Append("флагман ").Append(V(me.position)).Append(" курс ").Append(Mathf.RoundToInt(me.eulerAngles.y));
            SkyFortress f = SkyFortress.Instance;
            if (f != null) b.Append(" · крепость ").Append(V(f.Center)).Append(" причал ").Append(V(f.BerthWorld));
            int i = 0;
            foreach (TargetShip t in TargetShip.Created) { i++; if (t != null && t.gameObject.activeInHierarchy) b.Append(" · враг ").Append(i).Append(t.Alive ? " " + V(t.transform.position) : " потоплен"); }
            i = 0;
            foreach (AllyShip al in AllyShip.All) { i++; if (al != null) b.Append(" · свой ").Append(i).Append(' ').Append(al.role).Append(' ').Append(V(al.transform.position)); }
            Camera cam = Camera.main;
            if (cam != null) b.Append(" · камера ").Append(V(cam.transform.position)).Append(" (").Append(RemoteCam.State).Append(")");
            return b.ToString();
        }
        static string V(Vector3 p) { return Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z); }

        static string Pilot(string arg, string[] a)
        {
            switch (arg)
            {
                case "goto": if (a.Length < 4) return "pilot goto x z [ход]"; RemotePilot.GoTo(new Vector3(Num(a, 2, 0f), 0f, Num(a, 3, 0f)), Num(a, 4, 0.7f)); return RemotePilot.State;
                case "heading": RemotePilot.Heading(Num(a, 2, 0f), Num(a, 3, 0.6f)); return RemotePilot.State;
                case "speed": RemotePilot.Speed(Num(a, 2, 0.6f)); return "ход " + Num(a, 2, 0.6f);
                case "stop": RemotePilot.Stop(); return "стоп";
                case "release": case "off": RemotePilot.Release(); return "управление владельцу";
            }
            return "pilot goto x z [ход] | heading град [ход] | speed 0..1 | stop | release";
        }

        static string Cam(string arg, string[] a)
        {
            EnsureCam();
            switch (arg)
            {
                case "orbit":   // cam orbit цель [N] угол наклон дистанция
                    {
                        int o = 3; string n = null;
                        if (a.Length > 3 && (a[2] == "enemy" || a[2] == "ally")) { n = a[3]; o = 4; }
                        Transform t = Find(a.Length > 2 ? a[2] : "me", n);
                        if (t == null) return "не нашёл цель";
                        RemoteCam.Target = t; RemoteCam.Yaw = Num(a, o, 200f); RemoteCam.Pitch = Num(a, o + 1, 15f); RemoteCam.Dist = Num(a, o + 2, 120f);
                        RemoteCam.M = RemoteCam.Mode.Orbit; RemoteCam.State = "орбита " + a[2]; return RemoteCam.State;
                    }
                case "at":      // cam at x y z курс наклон
                    RemoteCam.Pos = new Vector3(Num(a, 2, 0f), Num(a, 3, 80f), Num(a, 4, 0f)); RemoteCam.Yaw = Num(a, 5, 0f); RemoteCam.Pitch = Num(a, 6, 10f);
                    RemoteCam.M = RemoteCam.Mode.At; RemoteCam.State = "точка"; return RemoteCam.State;
                case "look":    // cam look x y z цель [N]
                    {
                        RemoteCam.Pos = new Vector3(Num(a, 2, 0f), Num(a, 3, 80f), Num(a, 4, 0f));
                        RemoteCam.Target = Find(a.Length > 5 ? a[5] : "me", a.Length > 6 ? a[6] : null);
                        RemoteCam.M = RemoteCam.Mode.Look; RemoteCam.State = "взгляд"; return RemoteCam.State;
                    }
                case "fov": RemoteCam.Fov = Mathf.Clamp(Num(a, 2, 0f), 0f, 120f); return "обзор " + RemoteCam.Fov;
                case "height": RemoteCam.Height = Num(a, 2, 10f); return "высота взгляда " + RemoteCam.Height;
                case "off": case "follow": RemoteCam.Off(); return "камера обычная";
            }
            return "cam orbit цель угол наклон дист | at x y z курс наклон | look x y z цель | fov N | off";
        }

        static void EnsureCam()
        {
            Camera c = Camera.main;
            if (c != null && c.GetComponent<RemoteCam>() == null) c.gameObject.AddComponent<RemoteCam>();
        }

        static System.Collections.IEnumerator View(OrbitCamera oc, float yaw, float pitch, float dist, float sec, bool unwrapped = false)
        {
            float y0 = oc.yaw, p0 = oc.pitch, d0 = oc.distance, t = 0f;
            float dy = unwrapped ? yaw - y0 : Mathf.DeltaAngle(y0, yaw);
            pitch = Mathf.Clamp(pitch, oc.minPitch, oc.maxPitch); dist = Mathf.Clamp(dist, oc.minDistance, oc.maxDistance);
            oc.CancelRecenter();
            while (t < 1f)
            {
                t = sec <= 0.01f ? 1f : Mathf.Min(1f, t + Time.unscaledDeltaTime / sec);
                float e = t * t * (3f - 2f * t);
                oc.yaw = y0 + dy * e; oc.pitch = Mathf.Lerp(p0, pitch, e); oc.distance = Mathf.Lerp(d0, dist, e);
                yield return null;
            }
            oc.yaw = Mathf.Repeat(oc.yaw + 180f, 360f) - 180f;
        }

        public static string List()
        {
            StringBuilder b = new StringBuilder("команды: ");
            b.Append(Help); return b.ToString();
        }
    }
}
