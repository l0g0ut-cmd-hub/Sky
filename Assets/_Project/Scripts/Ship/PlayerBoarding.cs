using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Абордаж: враг ниже 20% прочности, ближе 85 м и идёт почти тем же ходом — на HUD кнопка «НА АБОРДАЖ».
    /// Нажал — 5 с схватки: оба корабля в дрейфе борт о борт (свой подтягивается к врагу), между бортами вспышки
    /// и щепа. Разошлись дальше 120 м или свой корабль подбит — абордаж сорван. Успех — враг взят призом
    /// (не тонет, а уходит; через обычную паузу приходит новый), флагману ремонт +25% (трофейные материалы).
    /// </summary>
    public class PlayerBoarding : MonoBehaviour
    {
        public float range = 85f, breakRange = 120f, duration = 5f;
        /// <summary>Конфиг: абордаж включён.</summary>
        public static bool Allowed = true;
        PlayerHull hull; ShipController ship;
        TargetShip candidate, boarding;
        float progress, nextFx;

        public TargetShip Candidate { get { return boarding == null ? candidate : null; } }
        public bool Active { get { return boarding != null; } }
        public float Progress01 { get { return Mathf.Clamp01(progress / duration); } }

        void Update()
        {
            if (hull == null) { hull = GetComponent<PlayerHull>(); ship = GetComponent<ShipController>(); }
            if (hull == null || ship == null) return;
            if (!hull.Alive) { Abort(); candidate = null; return; }
            if (boarding != null) { Step(Time.deltaTime); return; }
            candidate = null; float best = range;
            if (!Allowed) return;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive || t.Health01 > 0.2f || t.Controller == null) continue;
                float d = Vector3.Distance(t.transform.position, transform.position) - Mathf.Max(0f, t.Length * 0.5f - 19f);   // крупные классы: от борта, а не от центра
                float dv = (t.transform.forward * t.Controller.Speed - transform.forward * ship.Speed).magnitude;
                if (d < best && dv < 14f) { best = d; candidate = t; }
            }
        }

        public void Begin()
        {
            if (candidate == null || boarding != null) return;
            boarding = candidate; candidate = null; progress = 0f;
            boarding.BeingBoarded = true;
            HudNotice.Post("АБОРДАЖ!", "", 0, 2f, HudNotice.Prio.Combat, null, null);
            DebugReport.Count("абордажей начато");
            Haptics.Pulse(60);
        }

        void Abort()
        {
            if (boarding != null) { boarding.BeingBoarded = false; HudNotice.Post("АБОРДАЖ СОРВАН", "", 1, 2.5f, HudNotice.Prio.Combat, null, null); DebugReport.Count("абордажей сорвано"); }
            boarding = null; progress = 0f;
        }

        void Step(float dt)
        {
            if (!boarding.Alive) { boarding.BeingBoarded = false; boarding = null; return; }
            Vector3 to = boarding.transform.position - transform.position; to.y = 0f;
            float d = to.magnitude, extra = Mathf.Max(0f, boarding.Length * 0.5f - 19f);
            if (d - extra > breakRange) { Abort(); return; }
            // Свой корабль в дрейфе подтягивается к борту врага (на 35 м — дальше не лезем, корпуса упрутся).
            ship.throttle = 0f; ship.turn = 0f;
            if (d > 35f + extra * 0.5f) transform.position += to / d * Mathf.Min(d - 35f - extra * 0.5f, 8f * dt);
            progress += dt;
            if (Time.time >= nextFx)
            {
                nextFx = Time.time + Random.Range(0.25f, 0.5f);
                FxSystem fx = FxSystem.Instance;
                Vector3 mid = (transform.position + boarding.transform.position) * 0.5f + Vector3.up * 6f + Random.insideUnitSphere * 5f;
                if (fx != null) { fx.Scrape(mid, to / Mathf.Max(d, 1f)); if (Random.value < 0.4f) fx.Explosion(mid, 0.35f); }
            }
            if (progress >= duration)
            {
                TargetShip prize = boarding; boarding = null;
                prize.Capture();
                hull.Repair(hull.maxHealth * 0.25f);
                HudNotice.Post("ПРИЗ ВЗЯТ · РЕМОНТ +25%", "", 0, 3.5f, HudNotice.Prio.Combat, null, null);
                Haptics.Pulse(80);
            }
        }
    }
}
