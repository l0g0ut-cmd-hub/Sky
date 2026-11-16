using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Удалённое управление (команды pilot/cam/photo — DevCommands): ассистент сам ведёт флагман и ставит камеру,
    /// чтобы проверять картинку и поведение прямо на телефоне владельца, без его участия.
    ///   Флагман: точка (goto), объект (go), курс (heading), ход (speed), стоп, отпустить.
    ///   Камера: орбита вокруг объекта (cam orbit), свободная точка (cam at), смотреть на объект (cam look), FOV, вернуть.
    /// Касание экрана владельцем — сразу отдаёт управление (pilot release / cam off).
    /// </summary>
    public static class RemotePilot
    {
        public static bool Active;
        static Vector3 point; static bool hasPoint;
        static float heading = float.NaN, throttle = 0.6f;
        static Transform follow;
        public static string State = "выкл";

        public static void Release() { duel = false; Active = false; hasPoint = false; follow = null; heading = float.NaN; State = "выкл"; Ram = false; }

        public static void GoTo(Vector3 p, float thr) { Active = true; point = p; hasPoint = true; follow = null; heading = float.NaN; throttle = thr; State = "к точке " + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.z); }
        public static void GoTo(Transform t, float thr, string name) { Active = true; follow = t; hasPoint = false; heading = float.NaN; throttle = thr; State = "к " + name; }
        public static void Heading(float deg, float thr) { Active = true; heading = deg; hasPoint = false; follow = null; throttle = thr; State = "курс " + Mathf.RoundToInt(deg); }
        public static void Speed(float thr) { Active = true; throttle = Mathf.Clamp(thr, -0.3f, 1f); if (State == "выкл") State = "ход " + throttle; }
        /// <summary>Бой как человек: держать цель на траверзе 180–320 м (а не подойти и встать — 15.11 «go near» стоял в упор
        /// на 7 м/с). Борт ближе к цели, ход 0.85; ближе 180 — отворот, дальше 320 — доворот на цель.</summary>
        static bool duel;
        public static void Duel(Transform t, string name) { Active = true; follow = t; duel = true; hasPoint = false; heading = float.NaN; throttle = 0.85f; State = "бой с " + name; }

        public static void Stop() { Active = true; hasPoint = false; follow = null; heading = float.NaN; throttle = 0f; State = "стоп"; }

        /// <summary>Вызывается из TouchControls вместо ручного управления.</summary>
        public static void Drive(ShipController ship)
        {
            // Отмена — только кнопкой «ОТМЕНИТЬ» на табличке (ControlBanner): случайное касание больше не сбрасывает управление.
            Vector3 pos = ship.transform.position;
            Vector3 dir = Vector3.zero; float thr = throttle;
            if (follow != null && duel)
            {
                Vector3 to = follow.position - pos; to.y = 0f; float d = to.magnitude;
                float side = Vector3.Dot(ship.transform.right, to) >= 0f ? 1f : -1f;   // цель справа — держим её правым бортом
                float off = d > 320f ? 45f : d < 180f ? 135f : 90f;                    // угол между курсом и направлением на цель
                dir = Quaternion.Euler(0f, -side * off, 0f) * to;
                thr = throttle;
            }
            else if (follow != null) { dir = follow.position - pos; float d = dir.magnitude; if (d < 220f) thr = Mathf.Min(thr, d < 120f ? 0f : 0.3f); }
            else if (hasPoint)
            {
                dir = point - pos; float d = new Vector2(dir.x, dir.z).magnitude;
                if (d < 40f) { thr = 0f; State = "на точке"; } else if (d < 160f) thr = Mathf.Min(thr, 0.3f);
            }
            else if (!float.IsNaN(heading)) dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            dir.y = 0f;
            float turn = 0f;
            if (dir.sqrMagnitude > 1f) turn = Mathf.Clamp(Vector3.SignedAngle(ship.transform.forward, dir, Vector3.up) / 20f, -1f, 1f);
            ship.turn = turn; ship.throttle = thr;
            // Удалённый пилот обходит корабли и строения, как живой игрок (17.11: на проверке врезался во всё и застревал).
            // pilot ram — без обхода (проверка столкновений).
            if (!Ram) Traffic.Avoid(ship, 19f, follow);
        }
        public static bool Ram;
    }
}
