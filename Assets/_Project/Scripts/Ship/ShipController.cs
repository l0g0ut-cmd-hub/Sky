using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Движение корабля: ход вперёд (throttle) и поворот (turn), высота постоянная.
    /// Корпус (Visual) кренится в повороте, клюёт носом при разгоне и слегка покачивается.
    /// </summary>
    public class ShipController : MonoBehaviour
    {
        [Header("Ход")]
        public float maxSpeed = 32f;
        public float maxReverseSpeed = 7f;
        public float acceleration = 7f;
        public float deceleration = 9f;

        [Header("Поворот")]
        public float maxYawRate = 42f;
        public float turnResponse = 4.5f;

        [Header("Граница мира")]
        public float arenaRadius = 1800f;

        [Header("Ввод (-1..1)")]
        public float throttle;
        public float turn;

        public Transform visual;

        public float Speed { get; private set; }

        float smoothTurn;
        float bobPhase;
        float pitch;
        float roll;
        float prevSpeed;
        float kick, kickVel;

        /// <summary>Отдача залпа: крен от стреляющего борта (пружина, гаснет за пару секунд).</summary>
        public void Recoil(float side)
        {
            kickVel += side * 3.2f;
        }

        void Start()
        {
            bobPhase = Random.value * 10f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float thr = Mathf.Clamp(throttle, -1f, 1f);
            float targetSpeed = thr >= 0f ? thr * maxSpeed : thr * maxReverseSpeed;
            float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(Speed) ? acceleration : deceleration;
            Speed = Mathf.MoveTowards(Speed, targetSpeed, rate * dt);

            smoothTurn = Mathf.MoveTowards(smoothTurn, Mathf.Clamp(turn, -1f, 1f), turnResponse * dt);
            // Корабль поворачивает и на месте (рулевые винты), но на ходу — заметно быстрее.
            float steer = 0.35f + 0.65f * Mathf.Clamp01(Mathf.Abs(Speed) / maxSpeed);
            transform.Rotate(0f, smoothTurn * maxYawRate * steer * dt, 0f, Space.World);

            Vector3 pos = transform.position + transform.forward * Speed * dt;
            Vector2 flat = new Vector2(pos.x, pos.z);
            if (flat.magnitude > arenaRadius)
            {
                flat = flat.normalized * arenaRadius;
                pos.x = flat.x;
                pos.z = flat.y;
            }
            transform.position = pos;

            if (visual != null)
            {
                bobPhase += dt;
                float accel = (Speed - prevSpeed) / dt;
                float targetRoll = -smoothTurn * steer * 6f;
                float targetPitch = Mathf.Clamp(-accel * 0.8f, -3f, 3f);
                float k = 1f - Mathf.Exp(-3f * dt);
                roll = Mathf.Lerp(roll, targetRoll, k);
                pitch = Mathf.Lerp(pitch, targetPitch, k);
                float swayRoll = Mathf.Sin(bobPhase * 0.6f) * 1.2f;
                float swayPitch = Mathf.Sin(bobPhase * 0.45f + 1.3f) * 0.8f;
                // Пружина отдачи: ω² = 6, затухание 2.
                kickVel += (-6f * kick - 2f * kickVel) * dt;
                kick += kickVel * dt;
                visual.localRotation = Quaternion.Euler(pitch + swayPitch, 0f, roll + swayRoll + kick);
                visual.localPosition = Vector3.up * Mathf.Sin(bobPhase * 0.8f) * 0.5f;
            }
            prevSpeed = Speed;
        }
    }
}
