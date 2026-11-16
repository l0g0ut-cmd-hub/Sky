using UnityEngine;

namespace SkyTide
{
    /// <summary>Классы кораблей (владелец 16.11): у каждого своя модель Meshy (Models/Fleet/Ship_&lt;id&gt;), свой размер
    /// (запечён в модель: от 16 м у катера до 260 м у адмиральского; Корсар — 38 м) и своя огневая мощь.
    /// Числа — база для рядового брига (ход 20, поворот 24, 30 попаданий, 420 м, перезарядка 6 с, как прежний враг);
    /// конфиг Sky-reports (enemySpeed/Yaw/Hits/Range/Cooldown) масштабирует все классы разом относительно брига.</summary>
    public enum ShipClass { None = -1, Cutter, Sloop, Corvette, Fireship, Brig, Ketch, Frigate, HeavyFrigate, ShipOfLine, Admiral }

    public struct ShipClassInfo
    {
        public string id, name;
        public float length;            // м — размер запечён в модель (tools/convert_fleet.py), здесь — для расчётов
        public int guns, hits;          // пушек на борт; прочность (попаданий обычным ядром)
        public float speed, yaw, accel; // ход м/с, поворот °/с, разгон м/с² (торможение — ×1,2)
        public float range, cooldown;   // дальность огня, м; перезарядка борта, с (скорострельность)
        public float caliber, muzzle;   // урон ядра (×обычного), скорость ядра м/с
        public float accuracy;          // множитель ошибки наводки: меньше — точнее
        public ShipClassInfo(string id, string name, float length, int guns, int hits, float speed, float yaw, float accel,
                             float range, float cooldown, float caliber, float muzzle, float accuracy)
        {
            this.id = id; this.name = name; this.length = length; this.guns = guns; this.hits = hits; this.speed = speed; this.yaw = yaw; this.accel = accel;
            this.range = range; this.cooldown = cooldown; this.caliber = caliber; this.muzzle = muzzle; this.accuracy = accuracy;
        }
        /// <summary>Огневая мощь: урон бортового залпа в минуту (пушки × калибр × 60 / перезарядка).</summary>
        public float DamagePerMinute { get { return guns * caliber * 60f / Mathf.Max(0.5f, cooldown); } }
    }

    public static class ShipClasses
    {
        public const int Count = 10;
        //                                         id              имя              длина пушек прочн. ход  поворот дальн. перезар.
        // Владелец 16.11: «адмиральские прям гигантские, и вообще сильно всё по размеру отличалось; большой размер — не скорость,
        // а прочность, малая манёвренность и ударная мощь; у всех своя скорострельность». Логика линейки:
        // • мелочь (катер, шлюп, корвет) — быстрая, вёрткая, частая стрельба лёгкими ядрами, слабая прочность, мажет чаще;
        // • бриг — середина, эталон конфига (ход 20, поворот 24, 30 попаданий, 420 м, 6 с);
        // • кеч — одна мортира на борт: редкий тяжёлый навес издалека (калибр ×3, перезарядка 12 с), неточный, вблизи беззащитен;
        // • фрегат → тяжёлый фрегат → линейный → адмиральский: длина ×1,75 → ×6,5, прочность ×2,5 → ×12, пушек 7 → 20 на борт,
        //   калибр ×1,5 → ×2,5, но разгон, поворот и перезарядка всё хуже; точнее (опытные канониры).
        // Залп в минуту (пушки×калибр×60/перезарядка): катер 20, шлюп 34, корвет 45, бриг 40, кеч 15, фрегат 90, тяж. фрегат 131,
        // линейный 168, адмиральский 250 — у адмиральского борт в 6 раз тяжелее брига.
        //                                       id              имя              длина пушки прочн. ход поворот разгон дальн. перезар. калибр ядро  точность
        public static readonly ShipClassInfo[] Info =
        {
            new ShipClassInfo("cutter",       "КАТЕР",           16f,  1,   6, 32f, 46f, 10f, 240f,  3.0f, 1.0f,  80f, 1.30f),
            new ShipClassInfo("sloop",        "ШЛЮП",            22f,  2,  10, 30f, 40f,  9f, 280f,  3.5f, 1.0f,  82f, 1.20f),
            new ShipClassInfo("corvette",     "КОРВЕТ",          30f,  3,  18, 26f, 32f,  8f, 330f,  4.0f, 1.0f,  84f, 1.05f),
            new ShipClassInfo("fireship",     "БРАНДЕР",         20f,  1,   8, 28f, 30f,  9f, 200f, 30.0f, 1.0f,  80f, 1.50f),
            new ShipClassInfo("brig",         "БРИГ",            40f,  4,  30, 20f, 24f,  6f, 420f,  6.0f, 1.0f,  85f, 1.00f),
            new ShipClassInfo("ketch",        "КЕЧ",             42f,  1,  34, 17f, 18f,  5f, 650f, 12.0f, 3.0f,  95f, 1.60f),
            new ShipClassInfo("frigate",      "ФРЕГАТ",          70f,  7,  75, 18f, 15f,  4.5f, 480f, 7.0f, 1.5f,  88f, 0.90f),
            new ShipClassInfo("heavyfrigate", "ТЯЖЁЛЫЙ ФРЕГАТ", 100f, 10, 120, 15f, 11f,  3.5f, 520f, 8.0f, 1.75f, 90f, 0.85f),
            new ShipClassInfo("ship_of_line", "ЛИНЕЙНЫЙ",       160f, 14, 200, 12f,  8f,  2.5f, 570f, 10.0f, 2.0f, 93f, 0.80f),
            new ShipClassInfo("admiral",      "АДМИРАЛЬСКИЙ",   260f, 20, 360, 10f,  5f,  1.6f, 620f, 12.0f, 2.5f, 95f, 0.75f),
        };

        public static ShipClassInfo Get(ShipClass c) { return Info[Mathf.Clamp((int)c, 0, Count - 1)]; }

        /// <summary>Состав корсаров (26 по умолчанию; первые 18 — ниже по порядку, ещё 8 — бриги, шлюп, корветы, фрегат, кеч) по порядку создания (владелец: «больше классов»): 1 адмиральский, 1 линейный,
        /// тяжёлый фрегат, 3 фрегата-ведущих, 6 бригов, 3 корвета, кеч, 2 шлюпа. Брандеры — только в набегах (позже).
        /// Порядок такой, чтобы ближние (первые) были рядовыми, а крупные — на дальних кругах.</summary>
        static readonly ShipClass[] Roster =
        {
            ShipClass.Brig, ShipClass.Sloop, ShipClass.Corvette, ShipClass.Brig, ShipClass.Frigate, ShipClass.Sloop,
            ShipClass.Brig, ShipClass.Corvette, ShipClass.Ketch, ShipClass.Frigate, ShipClass.Brig, ShipClass.HeavyFrigate,
            ShipClass.Corvette, ShipClass.Brig, ShipClass.Frigate, ShipClass.ShipOfLine, ShipClass.Brig, ShipClass.Admiral,
            ShipClass.Brig, ShipClass.Sloop, ShipClass.Corvette, ShipClass.Brig, ShipClass.Frigate, ShipClass.Ketch, ShipClass.Brig, ShipClass.Corvette,
        };
        public static ShipClass ForEnemy(int index) { return index < Roster.Length ? Roster[index] : (index % 3 == 2 ? ShipClass.Corvette : ShipClass.Brig); }
    }
}
