using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Разметка атласа Art/CloudAtlas.png. Сгенерировано tools/build_cloud_atlas.py — не править руками.
    /// UV-прямоугольники, начало снизу слева.
    /// </summary>
    public static class CloudAtlas
    {
        /// <summary>Кучевые облака: [i] = (против солнца, нейтральное, в тени) одной формы.</summary>
        public static readonly Rect[][] Puffs =
        {
            new Rect[] { new Rect(0.25586f, 0.11426f, 0.11719f, 0.08398f), new Rect(0.09131f, 0.10400f, 0.16309f, 0.09424f), new Rect(0.08545f, 0.20166f, 0.16455f, 0.10352f) },
            new Rect[] { new Rect(0.12964f, 0.43896f, 0.12280f, 0.16064f), new Rect(0.58545f, 0.81543f, 0.15112f, 0.18457f), new Rect(0.42505f, 0.81201f, 0.15894f, 0.18799f) },
            new Rect[] { new Rect(0.37451f, 0.11475f, 0.07764f, 0.08350f), new Rect(0.00000f, 0.10352f, 0.08984f, 0.09473f), new Rect(0.87061f, 0.20801f, 0.09961f, 0.09717f) },
            new Rect[] { new Rect(0.25391f, 0.44434f, 0.10571f, 0.15527f), new Rect(0.00000f, 0.42480f, 0.12817f, 0.17480f), new Rect(0.73804f, 0.82178f, 0.13159f, 0.17822f) },
            new Rect[] { new Rect(0.82520f, 0.31689f, 0.14209f, 0.10498f), new Rect(0.00000f, 0.30811f, 0.17993f, 0.11377f), new Rect(0.63501f, 0.48438f, 0.18335f, 0.11523f) },
            new Rect[] { new Rect(0.79443f, 0.20654f, 0.07471f, 0.09863f), new Rect(0.00000f, 0.20117f, 0.08398f, 0.10400f), new Rect(0.45557f, 0.31201f, 0.09082f, 0.10986f) },
        };
        /// <summary>Длинные полосы и дымка.</summary>
        public static readonly Rect[] Strips =
        {
            new Rect(0.45361f, 0.12695f, 0.27466f, 0.07129f),
            new Rect(0.52344f, 0.20605f, 0.26953f, 0.09912f),
            new Rect(0.36108f, 0.48389f, 0.27246f, 0.11572f),
            new Rect(0.54785f, 0.31641f, 0.27588f, 0.10547f),
            new Rect(0.25146f, 0.20410f, 0.27051f, 0.10107f),
            new Rect(0.18140f, 0.31152f, 0.27271f, 0.11035f),
        };
        /// <summary>Грозовые башни (дальний план).</summary>
        public static readonly Rect[] Towers =
        {
            new Rect(0.20142f, 0.60986f, 0.22217f, 0.39014f),
            new Rect(0.00000f, 0.60254f, 0.19995f, 0.39746f),
        };
        public const float Aspect = 2.0f;   // ширина / высота атласа
    }
}
