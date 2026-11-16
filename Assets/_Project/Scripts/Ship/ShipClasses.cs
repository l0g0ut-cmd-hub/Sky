using UnityEngine;

namespace SkyTide
{
    /// <summary>Классы кораблей (владелец 16.11): у каждого своя модель Meshy (Models/Fleet/Ship_&lt;id&gt;), свой размер
    /// (запечён в модель: от 20 м у катера до 135 м у адмиральского; Корсар — 38 м) и своя огневая мощь.
    /// Числа — база для рядового брига (ход 20, поворот 24, 30 попаданий, 420 м, перезарядка 6 с, как прежний враг);
    /// конфиг Sky-reports (enemySpeed/Yaw/Hits/Range/Cooldown) масштабирует все классы разом относительно брига.</summary>
    public enum ShipClass { None = -1, Cutter, Sloop, Corvette, Fireship, Brig, Ketch, Frigate, HeavyFrigate, ShipOfLine, Admiral }

    public struct ShipClassInfo
    {
        public string id, name;
        public float length;            // м — для справки (размер уже в модели)
        public int guns, hits;
        public float speed, yaw, range, cooldown;
        public ShipClassInfo(string id, string name, float length, int guns, int hits, float speed, float yaw, float range, float cooldown)
        { this.id = id; this.name = name; this.length = length; this.guns = guns; this.hits = hits; this.speed = speed; this.yaw = yaw; this.range = range; this.cooldown = cooldown; }
    }

    public static class ShipClasses
    {
        public const int Count = 10;
        //                                         id              имя              длина пушек прочн. ход  поворот дальн. перезар.
        public static readonly ShipClassInfo[] Info =
        {
            new ShipClassInfo("cutter",       "КАТЕР",          20f,  1,   8, 30f, 40f, 260f, 4.0f),
            new ShipClassInfo("sloop",        "ШЛЮП",           26f,  2,  12, 28f, 36f, 300f, 4.5f),
            new ShipClassInfo("corvette",     "КОРВЕТ",         32f,  3,  20, 25f, 30f, 340f, 5.0f),
            new ShipClassInfo("fireship",     "БРАНДЕР",        24f,  1,  10, 26f, 30f, 200f, 30f),
            new ShipClassInfo("brig",         "БРИГ",           40f,  4,  30, 20f, 24f, 420f, 6.0f),
            new ShipClassInfo("ketch",        "КЕЧ",            38f,  2,  26, 17f, 20f, 560f, 9.0f),
            new ShipClassInfo("frigate",      "ФРЕГАТ",         58f,  6,  55, 18f, 20f, 460f, 6.5f),
            new ShipClassInfo("heavyfrigate", "ТЯЖЁЛЫЙ ФРЕГАТ", 74f,  8,  75, 16f, 17f, 480f, 7.0f),
            new ShipClassInfo("ship_of_line", "ЛИНЕЙНЫЙ",      100f, 10, 120, 13f, 13f, 520f, 8.0f),
            new ShipClassInfo("admiral",      "АДМИРАЛЬСКИЙ",  135f, 14, 180, 11f, 10f, 560f, 9.0f),
        };

        public static ShipClassInfo Get(ShipClass c) { return Info[Mathf.Clamp((int)c, 0, Count - 1)]; }

        /// <summary>Состав 18 кораблей корсаров по порядку создания (владелец: «больше классов»): 1 адмиральский, 1 линейный,
        /// тяжёлый фрегат, 3 фрегата-ведущих, 6 бригов, 3 корвета, кеч, 2 шлюпа. Брандеры — только в набегах (позже).
        /// Порядок такой, чтобы ближние (первые) были рядовыми, а крупные — на дальних кругах.</summary>
        static readonly ShipClass[] Roster =
        {
            ShipClass.Brig, ShipClass.Sloop, ShipClass.Corvette, ShipClass.Brig, ShipClass.Frigate, ShipClass.Sloop,
            ShipClass.Brig, ShipClass.Corvette, ShipClass.Ketch, ShipClass.Frigate, ShipClass.Brig, ShipClass.HeavyFrigate,
            ShipClass.Corvette, ShipClass.Brig, ShipClass.Frigate, ShipClass.ShipOfLine, ShipClass.Brig, ShipClass.Admiral,
        };
        public static ShipClass ForEnemy(int index) { return index < Roster.Length ? Roster[index] : (index % 3 == 2 ? ShipClass.Corvette : ShipClass.Brig); }
    }
}
