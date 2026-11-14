using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Камера на орбите вокруг корабля. yaw — поворот относительно курса корабля (0 = сзади),
    /// pitch — угол сверху, distance — расстояние. Всё это двигает TouchControls (палец/мышь/клавиши).
    /// Камера мягко догоняет корабль и поворачивается вместе с ним. На полном ходу чуть отъезжает и берёт шире;
    /// у крепости не проходит сквозь стены и пирс (поднимается над низким, подъезжает к высокому);
    /// Recenter — плавный возврат за корму.
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
        /// <summary>На сколько метров опущена точка взгляда (корабль игрока тонет — камера провожает его вниз).</summary>
        [System.NonSerialized] public float lookDrop;

        float smoothHeading;
        bool snapped;
        ShipController shipCtrl; Transform shipCtrlFor;
        Camera lens; float baseFov = -1f;
        float speedPull, wallDistance = -1f, wallLift, recenter;
        bool wallHeld;
        public const float DefaultPitch = 14f;
        /// <summary>Плавно вернуть камеру за корму (двойное касание правой половины экрана).</summary>
        public void Recenter() { recenter = 1f; }
        public void CancelRecenter() { recenter = 0f; }
        Vector2 recoil,recoilVelocity;
        public float RecoilAngle {get {return recoil.magnitude;}}
        public void SalvoImpulse(int side)
        {
            float strength=Mathf.Clamp01(55f/Mathf.Max(27f,distance));
            recoilVelocity+=new Vector2(5f,side*3f)*strength;
        }
        /// <summary>Встряска от попадания по своему кораблю: случайное направление, сила — по калибру ядра.</summary>
        public void HitImpulse(float power)
        {
            float strength=Mathf.Clamp01(55f/Mathf.Max(27f,distance))*Mathf.Min(2.2f,0.7f+0.4f*power);
            recoilVelocity+=new Vector2(Random.Range(-4f,4f),Random.Range(-4f,4f))*strength;
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
            wallDistance = -1f; wallLift = 0f;
        }

        void LateUpdate(){long __pt=PerfTimers.Start();LateUpdateTimed();PerfTimers.Stop("Камера",__pt);}
        void LateUpdateTimed()
        {
            Apply(Time.deltaTime);
        }

        /// <summary>Ставит камеру на орбиту. Публичный — для кадров в CI.</summary>
        public void Apply(float dt)
        {
            if (target == null) return;
            if (recenter > 0f && dt > 0f)
            {
                float back = 1f - Mathf.Exp(-7f * dt);
                yaw = Mathf.LerpAngle(yaw, 0f, back);
                pitch = Mathf.Lerp(pitch, DefaultPitch, back);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 0.5f) { yaw = 0f; recenter = 0f; }
            }
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

            Vector3 pivot = target.position + Vector3.up * (lookHeight - lookDrop);
            Quaternion rot = Quaternion.Euler(pitch, smoothHeading + yaw, 0f);
            // Ход: на полном камера на 14% дальше и обзор на 5° шире (только в игре — кадры CI не меняются).
            if (target != shipCtrlFor) { shipCtrlFor = target; shipCtrl = target.GetComponent<ShipController>(); }
            float speed01 = shipCtrl != null && shipCtrl.maxSpeed > 0f ? Mathf.Clamp01(shipCtrl.Speed / shipCtrl.maxSpeed) : 0f;
            speedPull = Mathf.Lerp(speedPull, speed01, 1f - Mathf.Exp(-1.2f * dt));
            float reach = distance * (1f + 0.14f * speedPull);
            if (Application.isPlaying)
            {
                if (lens == null) lens = GetComponent<Camera>();
                if (lens != null) { if (baseFov < 0f) baseFov = lens.fieldOfView; lens.fieldOfView = baseFov + 5f * speedPull; }
            }
            // Крепость: по карте высот вдоль луча «корабль → камера». Низкое (пирс, парапет до 14 м над лучом) —
            // камера поднимается над ним; высокое (стена, башня) — подъезжает к кораблю, не входя в камень.
            Vector3 backDir = -(rot * Vector3.forward);
            float allowed = reach, lift = 0f;
            SkyFortress fort = Application.isPlaying ? SkyFortress.Instance : null;
            if (fort != null)
            {
                Vector3 fc = fort.Center; float far = fort.BoundRadius + reach + 20f;
                float fdx = pivot.x - fc.x, fdz = pivot.z - fc.z;
                if (fdx * fdx + fdz * fdz < far * far)
                {
                    const int steps = 20;
                    for (int i = 1; i <= steps; i++)
                    {
                        Vector3 q = pivot + backDir * (reach * i / steps);
                        float over = fort.TopAt(q) + 2f - q.y;
                        if (over > 14f) { allowed = Mathf.Max(16f, reach * (i - 1) / steps - 1f); break; }
                        if (over > lift) lift = over;
                    }
                }
            }
            // К стене и вверх — быстро (за ~0.15 с, без рывка); от стены и вниз — плавно; без стены расстояние
            // идёт за пальцами без задержки.
            float ease = 1f - Mathf.Exp(-3f * dt), quick = 1f - Mathf.Exp(-14f * dt);
            bool blocked = allowed < reach - 0.01f;
            if (wallDistance < 0f || (!blocked && !wallHeld)) wallDistance = allowed;
            else wallDistance = Mathf.Lerp(wallDistance, allowed, allowed < wallDistance ? quick : ease);
            wallHeld = blocked || Mathf.Abs(wallDistance - allowed) > 0.5f;
            wallLift = Mathf.Lerp(wallLift, lift, lift > wallLift ? quick : ease);
            if (fort == null) { wallDistance = reach; wallLift = 0f; }
            Vector3 desired = pivot + backDir * wallDistance + Vector3.up * wallLift;

            // Позиция идёт за кораблём жёстко (без отставания на ходу), сглаживается только поворот за курсом.
            transform.position = desired;
            snapped = true;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            StepRecoil(dt);
            if(recoil.sqrMagnitude>1e-10f)transform.rotation*=Quaternion.Euler(recoil.x,recoil.y,recoil.y*.35f);
        }
    }
}
