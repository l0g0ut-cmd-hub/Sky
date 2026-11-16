using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Сообщения боя (малая плашка HUD в правом верхнем углу). Владелец 16.11: «непонятно "враг бьёт" — какой враг, почему
    /// и главное где; "цель уничтожена" — кто-то уничтожил, а это мне посередине; знатно посмотри логику уведомлений».
    /// Правила:
    /// • Каждое сообщение о корабле — «КТО ЧТО» в плашке и вторая строка под ней «ПОЧЕМУ · ГДЕ» (сторона относительно
    ///   флагмана и дальность), а на самом корабле на время сообщения — метка (кольцо в кадре, стрелка у края за кадром).
    /// • Важность: Critical (тебя касается прямо: огонь по тебе, вне закона, набег на тебя, свой просит прекратить огонь) &gt;
    ///   Combat (твой бой: попадания, повреждения цели, сдача, твои победы) &gt; Info (что рядом: свой под огнём, чужой бой
    ///   ближе 1,5 км, погода) &gt; Hint (подсказки). Более важное не перебивается менее важным — то ждёт в очереди
    ///   (до 4, устаревает за 6 с). Один и тот же ключ во время показа — обновляет текст, а не мигает заново; повтор того же
    ///   ключа реже чем раз в cooldown — не показывается.
    /// • Чужие события дальше 1,5 км (порт, бой корсара со сторожевым у края карты) — только в журнал отчёта.
    /// • Центр экрана — только твоё: твоя победа/приз, твоя гибель, тревога крепости по тебе (ShipHud).
    /// </summary>
    public static class HudNotice
    {
        public enum Prio { Hint = 0, Info = 1, Combat = 2, Critical = 3 }

        public static string Text = "", Sub = "";
        public static float At = -100f, Hold = 3f;
        public static int Icon = 1;
        public static Prio Level = Prio.Info;
        /// <summary>Корабль, о котором сообщение: HUD держит на нём метку, пока сообщение видно.</summary>
        public static Transform Focus;
        public static Color FocusColor = new Color(1f, 0.4f, 0.3f, 1f);
        static string key = "";

        struct Item { public string text, sub, key; public int icon; public float hold, at; public Prio prio; public Transform focus; public Color color; }
        static readonly List<Item> queue = new List<Item>();
        /// <summary>Очередь ожидающих сообщений (журнал мира).</summary>
        public static string QueueText() { System.Text.StringBuilder b = new System.Text.StringBuilder(); foreach (Item q in queue) b.Append(" [").Append(q.prio).Append("] ").Append(q.text); return b.ToString(); }
        static readonly Dictionary<string, float> lastByKey = new Dictionary<string, float>();

        /// <summary>Совместимость: простое сообщение (Info, без «где»).</summary>
        public static void Show(string text, int icon = 1, float hold = 3f) { Post(text, "", icon, hold, Prio.Info, null, null); }

        /// <summary>Сообщение с важностью, второй строкой (почему · где), ключом повтора и кораблём для метки.</summary>
        public static void Post(string text, string sub, int icon, float hold, Prio prio, string k, Transform focus, float cooldown = 0f, Color? focusColor = null)
        {
            if (!Application.isPlaying) return;
            k = string.IsNullOrEmpty(k) ? text : k;
            float last;
            bool showingSame = Alpha > 0f && key == k;
            if (!showingSame && cooldown > 0f && lastByKey.TryGetValue(k, out last) && Time.time - last < cooldown) { WorldLog.Raw("УВЕДОМЛЕНИЕ ПОДАВЛЕНО (повтор) «" + text + "»"); return; }
            lastByKey[k] = Time.time;
            DebugReport.Event(sub.Length > 0 ? text + " — " + sub : text);
            Item it = new Item { text = text, sub = sub ?? "", key = k, icon = icon, hold = hold, at = Time.time, prio = prio, focus = focus, color = focusColor ?? DefaultColor(icon) };
            if (showingSame) { Apply(it, false); return; }                       // тот же ключ — обновить, не мигать
            if (Alpha > 0.05f && Level > prio) { Enqueue(it); WorldLog.Raw("УВЕДОМЛЕНИЕ В ОЧЕРЕДЬ «" + text + "» (на экране важнее)"); return; }          // важнее на экране — подождать
            Apply(it, true);
        }
        static Color DefaultColor(int icon) { return icon == 0 ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(1f, 0.4f, 0.3f, 1f); }
        static void Apply(Item it, bool restart)
        {
            Text = it.text; Sub = it.sub; Icon = it.icon; Level = it.prio; Focus = it.focus; FocusColor = it.color; key = it.key;
            if (restart) { At = Time.time; Hold = it.hold; }
            else Hold = Mathf.Max(Hold, Time.time - At + it.hold * 0.6f);
        }
        static void Enqueue(Item it)
        {
            queue.RemoveAll(q => q.key == it.key);
            queue.Add(it);
            queue.Sort((a, b) => b.prio.CompareTo(a.prio));
            if (queue.Count > 4) queue.RemoveAt(queue.Count - 1);
        }
        /// <summary>Каждый кадр из HUD: показ закончился — следующий из очереди (не старше 6 с).</summary>
        public static void Pump()
        {
            if (!Application.isPlaying || Alpha > 0.02f) return;
            while (queue.Count > 0)
            {
                Item it = queue[0]; queue.RemoveAt(0);
                if (Time.time - it.at > 6f) continue;
                Apply(it, true); return;
            }
            Focus = null;
        }

        /// <summary>0..1 — видимость сейчас (плавное появление и уход).</summary>
        public static float Alpha
        {
            get
            {
                if (!Application.isPlaying) return 0f;
                float t = Time.time - At;
                if (t < 0f || t > Hold) return 0f;
                return Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((Hold - t) / 0.5f);
            }
        }

        // ---------- «кто» и «где» ----------
        /// <summary>Имя корабля для сообщений: класс (БРИГ, АДМИРАЛЬСКИЙ…) или роль своего (СТОРОЖЕВОЙ…).</summary>
        public static string Who(TargetShip t) { return t == null ? "ВРАГ" : t.Class != ShipClass.None ? ShipClasses.Get(t.Class).name : "КОРСАР"; }
        public static string Who(AllyShip a) { return a == null ? "СВОЙ" : AllyShip.RoleNames[(int)a.role] + (a.Class != ShipClass.None && a.role == AllyShip.Role.Harbor ? " " + ShipClasses.Get(a.Class).name : ""); }
        /// <summary>Где относительно флагмана: «ПО НОСУ / СПРАВА ПО НОСУ / СПРАВА / СПРАВА ПО КОРМЕ / ПО КОРМЕ» и дальность.</summary>
        public static string Where(Vector3 p)
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (g == null || g.Ship == null) return "";
            Transform me = g.Ship.transform;
            Vector3 d = p - me.position; d.y = 0f;
            float dist = d.magnitude;
            float a = Vector3.SignedAngle(Flat(me.forward), d, Vector3.up);
            string side = a > 0f ? "СПРАВА" : "СЛЕВА";
            float aa = Mathf.Abs(a);
            string dir = aa < 25f ? "ПО НОСУ" : aa < 70f ? side + " ПО НОСУ" : aa < 110f ? side : aa < 155f ? side + " ПО КОРМЕ" : "ПО КОРМЕ";
            return dir + " " + Dist(dist);
        }
        public static string Dist(float m) { return m >= 1000f ? (m / 1000f).ToString("0.0").Replace('.', ',') + " КМ" : Mathf.RoundToInt(m / 10f) * 10 + " М"; }
        public static float DistToPlayer(Vector3 p)
        {
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (g == null || g.Ship == null) return float.MaxValue;
            Vector3 d = p - g.Ship.transform.position; d.y = 0f; return d.magnitude;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>Причина враждебности для второй строки — по-человечески.</summary>
        public static string Reason(string why)
        {
            if (string.IsNullOrEmpty(why)) return "";
            if (why.StartsWith("попадание флагмана")) return "ТЫ ПО НЕМУ ПОПАЛ";
            if (why.StartsWith("попадание гарнизона")) return "БОЙ С ГАРНИЗОНОМ";
            if (why.StartsWith("не ушёл из наших вод") || why.StartsWith("флагман в водах")) return "ТЫ В ИХ ВОДАХ";
            if (why.StartsWith("держал на прицеле")) return "ТЫ ДЕРЖАЛ ЕГО НА ПРИЦЕЛЕ";
            if (why.StartsWith("набег")) return "НАБЕГ — ЗЛОБА ЗА ПОТОПЛЕННЫХ";
            if (why.StartsWith("зов о помощи")) return "ИДЁТ НА ПОМОЩЬ СВОИМ";
            if (why.StartsWith("дурень")) return "ДУРЕНЬ ЛЕЗЕТ В ДРАКУ";
            if (why.StartsWith("столкновение")) return "СТОЛКНОВЕНИЕ";
            if (why.StartsWith("загнан")) return "ЗАГНАН — ДЕРЁТСЯ";
            if (why.StartsWith("отказ уйти")) return "ОТКАЗАЛСЯ УЙТИ ИЗ ВОД ГАРНИЗОНА";
            if (why.StartsWith("мораль")) return "";
            return "";
        }
    }
}
