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
        /// <summary>Множитель хода, разгона и поворота (автобой флагмана — 1,5; владелец 17.11).</summary>
        [System.NonSerialized] public float boost = 1f;
        public float turnResponse = 4.5f;

        [Header("Граница мира")]
        public float arenaRadius = 3600f;   // крепость (центр в 2 км, радиус до ~1 км) можно обойти кругом

        [Header("Ввод (-1..1)")]
        public float throttle;
        public float turn;

        public Transform visual;

        public float Speed { get; private set; }
        /// <summary>Все корабли в мире (свой, враги, союзники, торговец) — для расталкивания и выбора целей.</summary>
        public static readonly System.Collections.Generic.List<ShipController> All = new System.Collections.Generic.List<ShipController>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        /// <summary>Радиус корпуса в плане для расталкивания (м): у корабля ×0.72 — меньше.</summary>
        public float HullRadius { get { return 15f * transform.lossyScale.x; } }
        public bool IsPlayer { get { return PlayerHull.Instance != null && PlayerHull.Instance.gameObject == gameObject; } }
        /// <summary>0..1 — насколько корабль зашёл в последние 250 м перед границей района (HUD показывает предупреждение).</summary>
        public float EdgeWarning { get; private set; }

        // Повреждения: ниже 50% прочности корабль теряет 15% хода, ниже 30% — 30% хода и 20% поворотливости.
        float damageSpeed = 1f, damageTurn = 1f;
        public void SetDamage(float health01)
        {
            damageSpeed = health01 < 0.3f ? 0.7f : health01 < 0.5f ? 0.85f : 1f;
            damageTurn = health01 < 0.3f ? 0.8f : 1f;
        }

        // Попадания по месту: руль (корма) и баллон (верх) — временная потеря поворотливости и хода.
        float rudderUntil = -1f, balloonUntil = -1f;
        public bool RudderHit { get { return Time.time < rudderUntil; } }
        public bool BalloonHit { get { return Time.time < balloonUntil; } }
        public void HitRudder(float seconds) { rudderUntil = Mathf.Max(rudderUntil, Time.time + seconds); }
        public void HitBalloon(float seconds) { balloonUntil = Mathf.Max(balloonUntil, Time.time + seconds); }
        public void ClearCrits() { rudderUntil = balloonUntil = -1f; }
        float CritSpeed { get { return BalloonHit ? 0.65f : 1f; } }
        float CritTurn { get { return RudderHit ? 0.5f : 1f; } }

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

        /// <summary>Упёрлись в препятствие (крепость): сдвиг и потеря хода.</summary>
        public void Block(Vector3 push, float keepSpeed)
        {
            transform.position += push;
            Speed *= keepSpeed;
        }

        void Start()
        {
            bobPhase = Random.value * 10f;
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Управление кораблём",__pt);}
        void UpdateTimed()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float thr = Mathf.Clamp(throttle, -1f, 1f);
            float wind = Application.isPlaying ? WindField.SpeedFactor(transform.forward) : 1f;
            float targetSpeed = thr >= 0f ? thr * maxSpeed * boost * damageSpeed * CritSpeed * wind : thr * maxReverseSpeed;
            float rate = (Mathf.Abs(targetSpeed) > Mathf.Abs(Speed) ? acceleration : deceleration) * boost;
            Speed = Mathf.MoveTowards(Speed, targetSpeed, rate * dt);

            // Край района: вместо невидимой стены руль мягко отводит корабль обратно, если он идёт наружу.
            float steerIn = 0f;
            Vector3 fromCentre = new Vector3(transform.position.x, 0f, transform.position.z);
            float out01 = Mathf.Clamp01((fromCentre.magnitude - (arenaRadius - 250f)) / 250f);
            EdgeWarning = out01;
            if (out01 > 0f)
            {
                Vector3 outward = fromCentre.normalized;
                if (Vector3.Dot(transform.forward, outward) > -0.3f)
                    steerIn = (Vector3.Cross(transform.forward, -outward).y >= 0f ? 1f : -1f) * out01;
            }
            smoothTurn = Mathf.MoveTowards(smoothTurn, Mathf.Clamp(turn + steerIn, -1f, 1f), turnResponse * dt);
            // Корабль поворачивает и на месте (рулевые винты), но на ходу — заметно быстрее.
            float steer = 0.35f + 0.65f * Mathf.Clamp01(Mathf.Abs(Speed) / (maxSpeed * boost));
            transform.Rotate(0f, smoothTurn * maxYawRate * boost * steer * damageTurn * CritTurn * dt, 0f, Space.World);

            Vector3 pos = transform.position + transform.forward * Speed * dt;
            Vector2 flat = new Vector2(pos.x, pos.z);
            if (flat.magnitude > arenaRadius)
            {
                flat = flat.normalized * arenaRadius;
                pos.x = flat.x;
                pos.z = flat.y;
            }
            transform.position = pos;
            // Корабли не проходят друг сквозь друга: ближе суммы радиусов — каждый отходит на свою половину (без урона;
            // удар и щепа при столкновении со своим кораблём — в TargetShip.CollideWithPlayer).
            if (Application.isPlaying)
                for (int i = 0; i < All.Count; i++)
                {
                    ShipController o = All[i];
                    if (o == null || o == this || !o.isActiveAndEnabled) continue;
                    // Свой корабль с врагом сталкивается по-настоящему (удар, щепа, таран — TargetShip.CollideWithPlayer).
                    if ((IsPlayer && o.GetComponent<TargetShip>() != null) || (o.IsPlayer && GetComponent<TargetShip>() != null)) continue;
                    Vector3 d = transform.position - o.transform.position; d.y = 0f;
                    float min = HullRadius + o.HullRadius;
                    float m2 = d.sqrMagnitude;
                    if (m2 >= min * min || m2 < 1e-4f) continue;
                    float m = Mathf.Sqrt(m2);
                    transform.position += d / m * ((min - m) * 0.5f);
                    Speed *= 1f - Mathf.Min(0.5f, dt * 2f);
                }

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
