using System.Collections;
using System.Text;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Вылеты (crash reporting). Unity не видит падений на уровне движка/драйвера и убийств системой — процесс просто
    /// исчезает. При следующем запуске:
    ///   1) Android 11+: ActivityManager.getHistoricalProcessExitReasons — почему прошлые процессы игры завершились
    ///      (вылет нативный/Java, ANR, нехватка памяти, убит системой, пользователем), с памятью процесса; для
    ///      нативного вылета и ANR — начало дампа (tombstone / трейс).
    ///   2) logcat своего приложения (только наши строки): ошибки Unity, CRASH, AndroidRuntime — хвост перед вылетом.
    /// Итог — строка в отчёте, подробности в полном отчёте и в Sky-reports/reports/crash.txt (если был вылет).
    /// </summary>
    public class CrashWatch : MonoBehaviour
    {
        public static string Summary = "", Details = "";
        const string SeenKey = "SkyTide.ExitSeen";
        static readonly string[] Reasons = { "?", "выход", "сигнал", "нехватка памяти", "вылет Java", "вылет нативный", "ANR (завис)", "ошибка запуска",
            "лишние ресурсы", "отказ зависимости", "другое", "заморожен", "пакет удалён", "пакет обновлён", "пользователь (остановлен)", "пользователь (смахнул)" };

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(3f);
#if UNITY_ANDROID && !UNITY_EDITOR
            StringBuilder d = new StringBuilder();
            bool crash = false;
            try { crash = ExitReasons(d); } catch (System.Exception e) { d.AppendLine(" причины завершения: недоступно (" + e.GetType().Name + ")"); }
            if (crash) { try { Logcat(d); } catch (System.Exception e) { d.AppendLine(" logcat: " + e.GetType().Name); } }
            Details = d.ToString();
            if (crash)
            {
                DebugReport.Event("ПРОШЛАЯ СЕССИЯ ЗАВЕРШИЛАСЬ АВАРИЙНО — " + Summary);
                yield return new WaitForSecondsRealtime(6f);
                if (ReportUploader.Instance != null) ReportUploader.Instance.Put("reports/crash.txt", Encoding.UTF8.GetBytes(
                    "сборка " + DebugReport.BuildNo + " · " + SystemInfo.deviceModel + " · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + Summary + "\n" + Details));
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject Activity()
        {
            using (AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return up.GetStatic<AndroidJavaObject>("currentActivity");
        }

        /// <summary>Причины завершения прошлых процессов (новые с прошлого запуска). true — был вылет/ANR/нехватка памяти.</summary>
        static bool ExitReasons(StringBuilder d)
        {
            using (AndroidJavaClass ver = new AndroidJavaClass("android.os.Build$VERSION"))
                if (ver.GetStatic<int>("SDK_INT") < 30) { Summary = "вылеты: Android < 11, только logcat"; return false; }
            long seen = long.Parse(PlayerPrefs.GetString(SeenKey, "0"));
            long newest = seen; bool bad = false; int shown = 0;
            using (AndroidJavaObject act = Activity())
            using (AndroidJavaObject am = act.Call<AndroidJavaObject>("getSystemService", "activity"))
            using (AndroidJavaObject list = am.Call<AndroidJavaObject>("getHistoricalProcessExitReasons", act.Call<string>("getPackageName"), 0, 8))
            {
                int n = list.Call<int>("size");
                for (int i = 0; i < n; i++)
                {
                    using (AndroidJavaObject info = list.Call<AndroidJavaObject>("get", i))
                    {
                        long ts = info.Call<long>("getTimestamp");
                        if (ts <= seen) continue;
                        if (ts > newest) newest = ts;
                        int reason = info.Call<int>("getReason");
                        string rs = reason >= 0 && reason < Reasons.Length ? Reasons[reason] : "код " + reason;
                        string desc = info.Call<string>("getDescription");
                        long pss = info.Call<long>("getPss") / 1024, rss = info.Call<long>("getRss") / 1024;
                        string when = System.DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("MM-dd HH:mm:ss");
                        d.AppendLine(" " + when + " · " + rs + (string.IsNullOrEmpty(desc) ? "" : " · " + desc) + " · память PSS " + pss + " МБ, RSS " + rss + " МБ · статус " + info.Call<int>("getStatus"));
                        bool isBad = reason == 3 || reason == 4 || reason == 5 || reason == 6 || reason == 7;
                        if (isBad && !bad) { bad = true; Summary = "ПРОШЛЫЙ ЗАПУСК: " + rs + " (" + when + ", память " + rss + " МБ)"; }
                        if ((reason == 5 || reason == 6) && shown++ == 0) Trace(info, d);
                    }
                }
            }
            PlayerPrefs.SetString(SeenKey, newest.ToString()); PlayerPrefs.Save();
            if (!bad) Summary = "вылеты: прошлые запуски завершились нормально";
            return bad;
        }

        /// <summary>Начало дампа вылета/ANR (до 80 строк).</summary>
        static void Trace(AndroidJavaObject info, StringBuilder d)
        {
            using (AndroidJavaObject stream = info.Call<AndroidJavaObject>("getTraceInputStream"))
            {
                if (stream == null) { d.AppendLine("  дамп: нет"); return; }
                using (AndroidJavaObject reader = new AndroidJavaObject("java.io.BufferedReader", new AndroidJavaObject("java.io.InputStreamReader", stream)))
                {
                    d.AppendLine("  дамп:");
                    for (int i = 0; i < 80; i++)
                    {
                        string line = reader.Call<string>("readLine");
                        if (line == null) break;
                        if (line.Length > 200) line = line.Substring(0, 200);
                        d.AppendLine("   " + line);
                    }
                    reader.Call("close");
                }
            }
        }

        /// <summary>Свой logcat: ошибки Unity, CRASH, AndroidRuntime, libc — последние 60 строк.</summary>
        static void Logcat(StringBuilder d)
        {
            using (AndroidJavaClass rt = new AndroidJavaClass("java.lang.Runtime"))
            using (AndroidJavaObject runtime = rt.CallStatic<AndroidJavaObject>("getRuntime"))
            using (AndroidJavaObject proc = runtime.Call<AndroidJavaObject>("exec", "logcat -d -v time -t 3000 Unity:W CRASH:V AndroidRuntime:E libc:F DEBUG:F *:S"))
            using (AndroidJavaObject reader = new AndroidJavaObject("java.io.BufferedReader", new AndroidJavaObject("java.io.InputStreamReader", proc.Call<AndroidJavaObject>("getInputStream"))))
            {
                System.Collections.Generic.Queue<string> tail = new System.Collections.Generic.Queue<string>();
                for (int i = 0; i < 3000; i++)
                {
                    string line = reader.Call<string>("readLine");
                    if (line == null) break;
                    if (line.Length > 220) line = line.Substring(0, 220);
                    tail.Enqueue(line); while (tail.Count > 60) tail.Dequeue();
                }
                reader.Call("close");
                d.AppendLine(" logcat (своё, хвост):");
                foreach (string s in tail) d.AppendLine("   " + s);
            }
        }
#endif
    }
}
