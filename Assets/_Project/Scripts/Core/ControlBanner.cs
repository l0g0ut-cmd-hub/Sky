using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Табличка на время удалённой проверки: «УПРАВЛЯЕТ CLAUDE · ЖДИ M:SS» и «коснись экрана — отменить».
    /// Время — оценка партии команд (сумма wait/fps/…). На снимках (photo/shot) табличка прячется.
    /// </summary>
    public class ControlBanner : MonoBehaviour
    {
        public static float BusyUntil = -1f;
        public static int HiddenCount;
        public static bool HiddenForShot { get { return HiddenCount > 0; } set { HiddenCount = Mathf.Max(0, HiddenCount + (value ? 1 : -1)); } }
        public static bool Cancelled;
        GUIStyle big, small; Texture2D bg;

        public static bool Busy { get { return Time.realtimeSinceStartup < BusyUntil || RemotePilot.Active || RemoteCam.M != RemoteCam.Mode.Off; } }
        /// <summary>Партия закончилась, но снимки ещё уходят: «ОТПРАВКА N» → «ГОТОВО — МОЖНО СВОРАЧИВАТЬ» (15.11: владелец уходил
        /// в чат раньше, игра вставала на паузу, отправка обрывалась).</summary>
        public static bool AwaitUpload;
        float doneUntil;

        void Update()
        {
            if (Busy && Input.touchCount > 0 && Time.realtimeSinceStartup < BusyUntil)
            {
                Cancelled = true; BusyUntil = -1f; RemotePilot.Release(); RemoteCam.Off(); TouchControls.HideHud = false;
                DebugReport.Event("удалённая проверка отменена касанием владельца");
            }
        }

        void OnGUI()
        {
            if (HiddenForShot) return;
            ReportUploader up = ReportUploader.Instance;
            int pend = up != null ? up.Pending : 0;
            // Очередь больше 3 файлов — всегда видно, сколько осталось и сколько ждать (владелец 15.11).
            if (!Busy && (AwaitUpload || pend > 3 || Time.realtimeSinceStartup < doneUntil))
            {
                if (AwaitUpload && pend == 0) { AwaitUpload = false; doneUntil = Time.realtimeSinceStartup + 8f; }
                if (pend > 0) Strip("ОСТАЛОСЬ ОТПРАВИТЬ: " + pend + " · ~" + Eta(up) + " · НЕ СВОРАЧИВАЙ", "");
                else Strip("ГОТОВО — МОЖНО СВОРАЧИВАТЬ", "");
                return;
            }
            if (!Busy) return;
            float left = BusyUntil - Time.realtimeSinceStartup;
            string t = left > 0f ? "ЖДИ " + Mathf.FloorToInt(left / 60f) + ":" + Mathf.FloorToInt(left % 60f).ToString("00") : "ИДЁТ ПРОВЕРКА";
            Strip("УПРАВЛЯЕТ CLAUDE · " + t + (pend > 3 ? " · В ОЧЕРЕДИ " + pend : ""), "коснись — отменить");
        }

        static string Eta(ReportUploader up)
        {
            float m = up != null ? up.PendingMinutes : 0f;
            return m < 1f ? Mathf.Max(5, Mathf.RoundToInt(m * 60f / 5f) * 5) + " с" : Mathf.CeilToInt(m) + " мин";
        }

        void Strip(string main, string hint)
        {
            float u = Mathf.Min(Screen.width, Screen.height);
            if (big == null)
            {
                bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.08f, 0.05f, 0.02f, 0.72f)); bg.Apply();
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(u * 0.038f) };
                big.normal.textColor = new Color(1f, 0.85f, 0.45f);
                small = new GUIStyle(big) { fontSize = Mathf.RoundToInt(u * 0.028f), fontStyle = FontStyle.Normal };
                small.normal.textColor = Color.white;
            }
            // Узкая полоса у верхнего края (владелец: «сверху, чтобы весь экран не закрывала»).
            float w = Screen.width * 0.56f, h = u * 0.075f;
            // Под полосой корпуса (на 21% высоты): у края она закрывала компас курса (скриншот владельца 15.11).
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.21f, w, h);
            GUI.DrawTexture(r, bg);
            GUI.Label(new Rect(r.x, r.y, hint == "" ? w : w * 0.62f, h), main, big);
            if (hint != "") GUI.Label(new Rect(r.x + w * 0.62f, r.y, w * 0.38f, h), hint, small);
        }
    }
}
