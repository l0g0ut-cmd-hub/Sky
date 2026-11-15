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
        /// <summary>Прицел бортом (как в Black Flag): пока игрок держит кнопку борта, камера уходит за этот борт —
        /// смотрит поверх корабля в сторону залпа, ниже и чуть ближе. 0 — нет, ±1 — борт.</summary>
        [System.NonSerialized] public int SideAim; float sideBlend, sideDir = 1f;
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
        float camLean;
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

        /// <summary>Камера не входит в чужие корабли: удалённая проверка 15.11 — при враге в 70 м весь кадр был его корпусом.
        /// Сфера ~1.5 радиуса корпуса вокруг центра (с баллонами); камера выталкивается по направлению от центра, плавно.</summary>
        Vector3 shipPush;
        Vector3 AvoidShips(Vector3 desired)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < ShipController.All.Count; i++)
            {
                ShipController s = ShipController.All[i];
                if (s == null || s.transform == target || !s.gameObject.activeInHierarchy) continue;
                Vector3 c = s.transform.position + Vector3.up * 8f * s.transform.lossyScale.y;
                float r = s.HullRadius * 1.5f;
                Vector3 d = desired - c;
                float m = d.magnitude;
                if (m < r) push += (m > 0.01f ? d / m : Vector3.up) * (r - m);
            }
            shipPush = Vector3.Lerp(shipPush, push, 1f - Mathf.Exp(-12f * Time.deltaTime));
            return desired + shipPush;
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
            if (SideAim != 0) sideDir = SideAim;
            sideBlend = Mathf.MoveTowards(sideBlend, SideAim != 0 ? 1f : 0f, dt * 2.6f);
            float sb = Mathf.SmoothStep(0f, 1f, sideBlend);
            Quaternion rot = Quaternion.Euler(Mathf.Lerp(pitch, 9f, sb), smoothHeading + yaw + sideDir * 82f * sb, 0f);
            // Ход: на полном камера на 14% дальше и обзор на 5° шире (только в игре — кадры CI не меняются).
            if (target != shipCtrlFor) { shipCtrlFor = target; shipCtrl = target.GetComponent<ShipController>(); }
            float speed01 = shipCtrl != null && shipCtrl.maxSpeed > 0f ? Mathf.Clamp01(shipCtrl.Speed / shipCtrl.maxSpeed) : 0f;
            speedPull = Mathf.Lerp(speedPull, speed01, 1f - Mathf.Exp(-1.2f * dt));
            float reach = distance * (1f + 0.14f * speedPull) * (1f - 0.12f * sb);
            if (Application.isPlaying)
            {
                if (lens == null) lens = GetComponent<Camera>();
                scopeBlend = Mathf.MoveTowards(scopeBlend, ScopeOn ? 1f : 0f, dt * 3f);
                if (lens != null) { if (baseFov < 0f) baseFov = lens.fieldOfView; lens.fieldOfView = Mathf.Lerp(baseFov + 5f * speedPull, baseFov * scopeZoom, scopeBlend); }
                // Дальность теней — по удалению камеры: вблизи та же карта теней ложится на меньший участок (тени резче),
                // издалека — хватает до корабля. Шаг 10 м, чтобы тени не «дышали» при каждом движении пальцев.
                float shadowReach = Mathf.Clamp(Mathf.Ceil((reach + 55f) / 10f) * 10f, 90f, 200f);
                if (!Mathf.Approximately(QualitySettings.shadowDistance, shadowReach)) QualitySettings.shadowDistance = shadowReach;
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
            if (Application.isPlaying) desired = AvoidShips(desired);

            // Позиция идёт за кораблём жёстко (без отставания на ходу), сглаживается только поворот за курсом.
            transform.position = desired;
            snapped = true;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            // Камера кренится вместе с кораблём на треть его крена (не больше 7°) — только в игре.
            if (Application.isPlaying && shipCtrl != null && shipCtrl.visual != null)
            {
                float lean = Mathf.Clamp(Mathf.DeltaAngle(0f, shipCtrl.visual.localEulerAngles.z) * 0.33f, -7f, 7f);
                camLean = Mathf.Lerp(camLean, lean, 1f - Mathf.Exp(-4f * dt));
                transform.rotation *= Quaternion.Euler(0f, 0f, camLean);
            }
            // Подзорная труба: с юта своего корабля прямо на цель (ближайший враг), обзор ×0.28. Цель погибла — труба убирается.
            if (Application.isPlaying && scopeBlend > 0.001f)
            {
                if (ScopeOn && (scopeTarget == null || !scopeTarget.Alive)) ScopeOn = false;
                // Над баллонами своего корабля (на ~25 м): с юта на 14 м труба смотрела сквозь свои паруса и баллон
                // (удалённая проверка 15.11, снимок «труба» — весь кадр в парусине).
                Vector3 deck = target.position + Vector3.up * 26f * target.lossyScale.y;
                Vector3 look = scopeTarget != null && scopeTarget.Alive ? scopeTarget.Center : deck + target.forward * 300f;
                // Увеличение по дальности: корабль цели занимает около трети кадра. Вблизи (20–60 м) труба почти не
                // приближает — на ×0,28 при враге в 20 м кадр был изнанкой его корпуса (удалённая проверка 15.11).
                if (scopeTarget != null && baseFov > 0f)
                {
                    float dist = Mathf.Max(1f, Vector3.Distance(deck, look));
                    float wantFov = 2f * Mathf.Atan(45f * scopeTarget.transform.lossyScale.x / dist) * Mathf.Rad2Deg;
                    scopeZoom = Mathf.Lerp(scopeZoom, Mathf.Clamp(wantFov / baseFov, 0.2f, 1f), 1f - Mathf.Exp(-6f * dt));
                }
                deck = AvoidShips(deck);   // точка трубы тоже не входит в чужие корабли
                Quaternion scopeRot = Quaternion.LookRotation(look - deck, Vector3.up);
                float k2 = scopeBlend * scopeBlend * (3f - 2f * scopeBlend);
                transform.position = Vector3.Lerp(transform.position, deck, k2);
                transform.rotation = Quaternion.Slerp(transform.rotation, scopeRot, k2);
            }
            StepRecoil(dt);
            if(recoil.sqrMagnitude>1e-10f)transform.rotation*=Quaternion.Euler(recoil.x,recoil.y,recoil.y*.35f);
        }

        public bool ScopeOn { get; private set; }
        float scopeBlend, scopeZoom = 0.5f; TargetShip scopeTarget;
        /// <summary>Подзорная труба вкл/выкл (кнопка рядом с FPS): наводится на ближайшего живого врага.</summary>
        public void ToggleScope()
        {
            if (ScopeOn) { ScopeOn = false; return; }
            scopeTarget = null; float best = float.MaxValue;
            // Цель бортов — в первую очередь (на неё и наводится прицел); иначе ближайший враг дальше 60 м, иначе просто ближайший.
            Broadside guns = target != null ? target.GetComponentInChildren<Broadside>() : null;
            if (guns != null && guns.AimTargetShip != null && guns.AimTargetShip.Alive) scopeTarget = guns.AimTargetShip;
            if (scopeTarget == null && target != null)
                foreach (TargetShip t in TargetShip.All)
                {
                    if (t == null || !t.Alive) continue;
                    float d = (t.transform.position - target.position).sqrMagnitude;
                    if (d < 60f * 60f) d += 1e8f;   // вплотную — в последнюю очередь
                    if (d < best) { best = d; scopeTarget = t; }
                }
            ScopeOn = scopeTarget != null;
            if (!ScopeOn) HudNotice.Show("ВРАГОВ НЕ ВИДНО", 1, 1.5f);
        }
    }
}
