using UnityEngine;

namespace SkyTide
{
    /// <summary>Воды карты: безопасная зона крепости, воды гарнизона, нейтральная полоса, воды корсаров.</summary>
    public enum Waters { Safe, Garrison, Neutral, Corsair }
    /// <summary>Отношение корабля к гарнизону (к флагману и своим): спокоен, настороже (борт к тебе, без огня),
    /// враждебен (бой), бежит, сдался (стоит, флаг спущен).</summary>
    public enum Attitude { Calm, Wary, Hostile, Flee, Surrender }
    /// <summary>Нрав капитана: трус бежит рано, осторожный — по силам, лихой дерётся и иногда стреляет первым.</summary>
    public enum Temper { Coward, Cautious, Bold }

    /// <summary>
    /// Политика отношений (владелец 16.11: «не должно быть тупого рубилова… конфликты редкие и только когда прям нарушение
    /// интересов»). По умолчанию мир: корсары ходят своими водами, нейтральную полосу проходят, не трогая. Повод для боя —
    /// только нарушение: вторжение в чужие воды и отказ уйти, долгий прицел в упор, выстрел/попадание/таран/абордаж, набег
    /// по злобе (единственный конфликт без повода с твоей стороны). Кого атаковали — решает сам: отпор, бегство, сдача.
    /// Свои: огонь по своим разрешён — первый раз «ПРЕКРАТИТЬ ОГОНЬ», дальше отбиваются или бегут, потопил — ты вне закона.
    /// </summary>
    public static class Relations
    {
        // Пояса от центра крепости (безопасная зона — её safeRadius, 1,4 км). Карта — круг 7,2 км (владелец 17.11: «вдвое»),
        // крепость в 2 км от центра; корабли держатся в FieldRadius.
        public static float GarrisonRadius = 2300f, CorsairRadius = 3300f;
        public const float FieldRadius = 6900f;
        public static float WarnSeconds = 30f, AimSeconds = 6f, AimRange = 400f, CalmAfter = 45f;
        // Злоба корсаров за потопленных и за вторжения; остывает. Высокая — набег (не чаще RaidEvery).
        public static float Grudge, RaidGrudge = 40f, RaidEvery = 600f, GrudgeCoolPerMin = 3f;
        public static float LastRaidAt = -1000f;
        /// <summary>Флагман вне закона у гарнизона (потопил своего): крепость бьёт, ведомый уходит.</summary>
        public static float OutlawUntil = -1f, OutlawSeconds = 300f;
        public static bool PlayerOutlaw { get { return Time.time < OutlawUntil; } }

        public static Waters At(Vector3 p)
        {
            SkyFortress f = SkyFortress.Instance;
            if (f == null) return Waters.Neutral;
            if (SkyFortress.InSafeZone(p)) return Waters.Safe;
            Vector3 d = p - f.Center; d.y = 0f;
            float r = d.magnitude;
            return r < GarrisonRadius ? Waters.Garrison : r > CorsairRadius ? Waters.Corsair : Waters.Neutral;
        }
        public static string Name(Waters w) { return w == Waters.Safe ? "безопасная зона" : w == Waters.Garrison ? "воды гарнизона" : w == Waters.Corsair ? "воды корсаров" : "нейтральные воды"; }
        public static string Name(Attitude a) { return a == Attitude.Calm ? "СПОКОЕН" : a == Attitude.Wary ? "НАСТОРОЖЕ" : a == Attitude.Hostile ? "ВРАЖДЕБЕН" : a == Attitude.Flee ? "БЕЖИТ" : "СДАЛСЯ"; }

        /// <summary>Точка в водах корсаров (для дозора, возрождения и бегства): кольцом вокруг крепости со всех сторон.</summary>
        public static Vector3 CorsairPoint(float y)
        {
            SkyFortress f = SkyFortress.Instance;
            Vector3 c = f != null ? f.Center : Vector3.zero; c.y = 0f;
            Vector3 away = -c; away.y = 0f; if (away.sqrMagnitude < 1f) away = Vector3.forward; away.Normalize();
            for (int i = 0; i < 16; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(CorsairRadius + 150f, CorsairRadius + 3200f);
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (new Vector2(p.x, p.z).magnitude < FieldRadius) { p.y = y; return p; }
            }
            Vector3 q = away * 3000f; q.y = y; return q;
        }

        /// <summary>Сила корабля — оставшаяся прочность × пушки на борт (по классу). Флагман — по своему корпусу.</summary>
        public static float Power(TargetShip t)
        {
            if (t == null || !t.Alive) return 0f;
            EnemyGunner g = t.GetComponent<EnemyGunner>();
            int guns = g != null ? Mathf.Max(1, g.gunsPerSide) : 4;
            return t.hitsToSink * t.Health01 * guns * (g != null ? Mathf.Max(1f, g.caliber) : 1f);
        }
        public static float PlayerPower()
        {
            PlayerHull ph = PlayerHull.Instance;
            if (ph == null || !ph.Alive) return 0f;
            return 90f * ph.Health01 * 8f * 2f * FxSystem.PlayerDamageMul * 0.5f;   // флагман: ~90 попаданий, 8 пушек на борт, урон ядра ×2
        }

        /// <summary>Злоба: потоплен корсар (+12), добит сдавшийся (+30), вторжение, отказ уйти (+4); отпущен сдавшийся (−6).</summary>
        public static void AddGrudge(float v, string why)
        {
            Grudge = Mathf.Clamp(Grudge + v, 0f, 150f);
            if (Application.isPlaying) DebugReport.Event("злоба корсаров " + (v >= 0 ? "+" : "") + v.ToString("F0") + " (" + why + ") → " + Grudge.ToString("F0"));
        }

        public static void DeclarePlayerOutlaw(string why)
        {
            OutlawUntil = Time.time + OutlawSeconds;
            DebugReport.Count("флагман вне закона"); DebugReport.Event("флагман вне закона у гарнизона: " + why);
            HudNotice.Post("ТЫ ВНЕ ЗАКОНА", "ПОТОПИЛ СВОЕГО · ГАРНИЗОН И КРЕПОСТЬ БЬЮТ ПО ТЕБЕ " + Mathf.RoundToInt(OutlawSeconds / 60f) + " МИН", 1, 6f, HudNotice.Prio.Critical, "outlaw", null);
            SkyFortress.DeclareOutlaw(OutlawSeconds);
        }
        public static void Pardon() { OutlawUntil = -1f; foreach (AllyShip a in AllyShip.All) if (a != null) a.ForgivePlayer(); }

        static float nextThink;
        /// <summary>Раз в секунду (из SkyTideBootstrap.Update): злоба остывает, набег — когда злоба высока и давно не было.</summary>
        public static void Tick()
        {
            if (!Application.isPlaying || Time.time < nextThink) return;
            nextThink = Time.time + 1f;
            Grudge = Mathf.Max(0f, Grudge - GrudgeCoolPerMin / 60f);
            PlayerHull ph = PlayerHull.Instance;
            if (ph == null || !ph.Alive || Grudge < RaidGrudge || Time.time - LastRaidAt < RaidEvery) return;
            if (At(ph.transform.position) == Waters.Safe) return;   // под пушками крепости не нападают
            // Набег: группа вокруг самого сильного спокойного корсара (крупнее брига), что ближе 2,5 км, — ведущий и до двух соседей.
            TargetShip lead = null; float best = 0f;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive || t.Mood != Attitude.Calm || t.InPort) continue;
                float d = Vector3.Distance(t.transform.position, ph.transform.position);
                if (d > 2500f) continue;
                float score = Power(t) / (1f + d / 1000f);
                if (score > best) { best = score; lead = t; }
            }
            if (lead == null) return;
            LastRaidAt = Time.time;
            int n = 0;
            lead.StartRaid(); n++;
            foreach (TargetShip t in TargetShip.All)
            {
                if (n >= 3) break;
                if (t == null || t == lead || !t.Alive || t.Mood != Attitude.Calm) continue;
                if ((t.transform.position - lead.transform.position).sqrMagnitude < 900f * 900f) { t.StartRaid(); n++; }
            }
            Vector3 dir = lead.transform.position - ph.transform.position; dir.y = 0f;
            HudNotice.Post("НАБЕГ: " + HudNotice.Who(lead) + (n > 1 ? " И ЕЩЁ " + (n - 1) : ""), "ЗЛОБА ЗА ПОТОПЛЕННЫХ · " + HudNotice.Where(lead.transform.position), 1, 6f, HudNotice.Prio.Critical, "raid", lead.transform);
            DebugReport.Count("набегов корсаров"); DebugReport.Event("набег: " + n + " корабля во главе с " + lead.name + " (" + ShipClasses.Get(lead.Class).name + "), злоба " + Grudge.ToString("F0"));
            Grudge *= 0.5f;
        }
        static string Side(Vector3 d)
        {
            float a = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
            string[] s = { "С СЕВЕРА", "С СЕВЕРО-ВОСТОКА", "С ВОСТОКА", "С ЮГО-ВОСТОКА", "С ЮГА", "С ЮГО-ЗАПАДА", "С ЗАПАДА", "С СЕВЕРО-ЗАПАДА" };
            return s[Mathf.RoundToInt(a / 45f) % 8];
        }
    }
}
