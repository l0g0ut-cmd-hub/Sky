using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Короткая вибрация на Android (длительность в мс) — та же схема, что в «Пути клинка». По умолчанию выключена,
    /// включается строкой «ВИБРАЦИЯ» в меню паузы. В редакторе и на других платформах — пусто.
    /// </summary>
    public static class Haptics
    {
        public const string Key = "SkyTide.Haptics";
        public static bool Enabled;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static bool tried;
#endif
        static float nextAllowed;

        public static void Pulse(long ms)
        {
            if (!Enabled || Time.unscaledTime < nextAllowed) return;
            nextAllowed = Time.unscaledTime + 0.08f;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!tried)
                {
                    tried = true;
                    using (AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        AndroidJavaObject act = up.GetStatic<AndroidJavaObject>("currentActivity");
                        vibrator = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                }
                if (vibrator != null) vibrator.Call("vibrate", ms);
            }
            catch (System.Exception) { vibrator = null; }
#endif
        }

        // Ссылка на Handheld.Vibrate нужна только затем, чтобы Unity добавил в манифест разрешение VIBRATE.
        public static void LongBuzz() { if (Enabled) Handheld.Vibrate(); }
    }
}
