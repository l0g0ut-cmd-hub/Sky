using UnityEngine;

namespace SkyTide
{
    /// <summary>Короткое сообщение о событии боя для малой плашки HUD (руль повреждён, попадания залпа, враг в порту).</summary>
    public static class HudNotice
    {
        public static string Text = "";
        public static float At = -100f, Hold = 3f;
        public static int Icon = 1;
        public static void Show(string text, int icon = 1, float hold = 3f)
        {
            if (!Application.isPlaying) return;
            Text = text; Icon = icon; Hold = hold; At = Time.time;
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
    }
}
