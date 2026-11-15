using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Удалённая камера (DevCommands cam ...): после обычной камеры (порядок 10000) ставит её туда, куда нужно ассистенту.
    /// orbit — вокруг объекта на угол/наклон/дистанцию (следует за ним); at — неподвижная точка с курсом и наклоном;
    /// look — неподвижная точка, взгляд на объект. Касание экрана — камера возвращается.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class RemoteCam : MonoBehaviour
    {
        public enum Mode { Off, Orbit, At, Look }
        public static Mode M = Mode.Off;
        public static Transform Target;
        public static Vector3 Pos;
        public static float Yaw, Pitch = 15f, Dist = 120f, Fov = 0f, Height = 10f;
        public static string State = "обычная";
        Camera cam; float baseFov;

        public static void Off() { M = Mode.Off; Target = null; Fov = 0f; State = "обычная"; }

        void Awake() { cam = GetComponent<Camera>(); if (cam != null) baseFov = cam.fieldOfView; }

        void LateUpdate()
        {
            if (M == Mode.Off || cam == null) { if (cam != null && Fov > 0f) { } return; }
            if (Input.touchCount > 0) { Off(); if (cam != null) cam.fieldOfView = baseFov; HudNotice.Show("камера возвращена", 0, 2f); return; }
            switch (M)
            {
                case Mode.Orbit:
                    if (Target == null) { Off(); return; }
                    Vector3 c = Target.position + Vector3.up * Height;
                    Quaternion q = Quaternion.Euler(Pitch, Yaw, 0f);
                    transform.SetPositionAndRotation(c - q * Vector3.forward * Dist, q);
                    break;
                case Mode.At:
                    transform.SetPositionAndRotation(Pos, Quaternion.Euler(Pitch, Yaw, 0f));
                    break;
                case Mode.Look:
                    transform.position = Pos;
                    if (Target != null) transform.rotation = Quaternion.LookRotation(Target.position + Vector3.up * Height - Pos, Vector3.up);
                    break;
            }
            if (Fov > 0f) cam.fieldOfView = Fov;
        }
    }
}
