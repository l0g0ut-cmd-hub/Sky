using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Камера на орбите вокруг корабля. yaw — поворот относительно курса корабля (0 = сзади),
    /// pitch — угол сверху, distance — расстояние. Всё это двигает TouchControls (палец/мышь/клавиши).
    /// Камера мягко догоняет корабль и поворачивается вместе с ним.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float yaw;
        public float pitch = 14f;
        public float distance = 62f;
        public float minPitch = -25f;
        public float maxPitch = 75f;
        public float minDistance = 35f;
        public float maxDistance = 140f;
        public float lookHeight = 10f;
        public float followSharpness = 6f;

        float smoothHeading;
        bool snapped;

        public void Snap()
        {
            snapped = false;
        }

        void LateUpdate()
        {
            if (target == null) return;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            float heading = target.eulerAngles.y;
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            if (!snapped)
            {
                smoothHeading = heading;
            }
            else
            {
                smoothHeading = Mathf.LerpAngle(smoothHeading, heading, k * 0.5f);
            }

            Vector3 pivot = target.position + Vector3.up * lookHeight;
            Quaternion rot = Quaternion.Euler(pitch, smoothHeading + yaw, 0f);
            Vector3 desired = pivot - rot * Vector3.forward * distance;

            // Позиция идёт за кораблём жёстко (без отставания на ходу), сглаживается только поворот за курсом.
            transform.position = desired;
            snapped = true;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        }
    }
}
