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
        public float minDistance = 27f;
        public float maxDistance = 140f;
        public float lookHeight = 10f;
        public float followSharpness = 6f;

        float smoothHeading;
        bool snapped;
        Vector2 recoil,recoilVelocity;
        public float RecoilAngle {get {return recoil.magnitude;}}
        public void SalvoImpulse(int side)
        {
            float strength=Mathf.Clamp01(55f/Mathf.Max(27f,distance));
            recoilVelocity+=new Vector2(5f,side*3f)*strength;
        }
        void OnDisable(){recoil=recoilVelocity=Vector2.zero;}
        void StepRecoil(float dt)
        {
            if(dt<=0)return;
            const float w=16f;float fade=Mathf.Exp(-w*dt);Vector2 b=recoilVelocity+w*recoil;
            recoil=(recoil+b*dt)*fade;recoilVelocity=(recoilVelocity-w*b*dt)*fade;
        }

        public void Snap()
        {
            snapped = false;
        }

        void LateUpdate()
        {
            Apply(Time.deltaTime);
        }

        /// <summary>Ставит камеру на орбиту. Публичный — для кадров в CI.</summary>
        public void Apply(float dt)
        {
            if (target == null) return;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            float heading = target.eulerAngles.y;
            float k = 1f - Mathf.Exp(-followSharpness * dt);
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
            StepRecoil(dt);
            if(recoil.sqrMagnitude>1e-10f)transform.rotation*=Quaternion.Euler(recoil.x,recoil.y,recoil.y*.35f);
        }
    }
}
