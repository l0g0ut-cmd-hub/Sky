using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Управление для телефона (и мыши/клавиатуры в редакторе).
    /// Левая половина экрана — плавающий джойстик: появляется там, где коснулись; в стороны — поворот,
    /// вверх/вниз — рукоять хода на ступень (НАЗАД · СТОП · МАЛЫЙ · СРЕДНИЙ · ПОЛНЫЙ): ход держится сам,
    /// палец можно убрать. Двойное касание правой половины — камера за корму. Правая половина — вращение камеры вокруг корабля,
    /// двумя пальцами — приближение. Мышь: левая кнопка как палец, колесо — приближение.
    /// Клавиатура: WASD/стрелки — корабль.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public ShipController ship;
        public OrbitCamera cam;
        public SkyTideBootstrap game;

        [Tooltip("Радиус хода джойстика в долях от меньшей стороны экрана")]
        public float stickRadius = 0.12f;
        [Tooltip("Градусов поворота камеры на ширину экрана")]
        public float orbitDegreesPerScreen = 220f;

        enum Owner { Stick, Camera, Button }

        readonly Dictionary<int, Owner> owners = new Dictionary<int, Owner>();
        readonly Dictionary<int, Vector2> lastPos = new Dictionary<int, Vector2>();
        int stickId = int.MinValue;
        Vector2 stickOrigin;
        Vector2 stickPos;
        Vector2 stickValue;
        float lastPinch = -1f;
        // Рукоять хода (машинный телеграф): ступень остаётся после отпускания джойстика.
        static readonly float[] GearThrottle = { -1f, 0f, 0.35f, 0.68f, 1f };
        public static readonly string[] GearNames = { "НАЗАД", "СТОП", "МАЛЫЙ", "СРЕДНИЙ", "ПОЛНЫЙ" };
        int gear = 1; bool gearArmed = true; float gearRepeatAt;
        public int Gear { get { return gear; } }
        public void SetGear(int g) { gear = Mathf.Clamp(g, 0, GearThrottle.Length - 1); gearArmed = true; }
        float lastCamTap = -10f; Vector2 lastCamTapPos;
        float hintAlpha = 1f;
        bool touchedOnce;
        bool legacyInputOk = true;
        float fps = 60f;

        public Shader hudShader;
        public Font hudFont;
        readonly ShipHud hud = new ShipHud();

        void Awake()
        {
            Input.simulateMouseWithTouches = false;
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Ввод",__pt);}
        void UpdateTimed()
        {
            if (!legacyInputOk) return;
            // Заставка студии и главное меню закрывают экран — касания им, а не кораблю.
            if (StudioSplash.Active || MainMenu.Active) { if (owners.Count > 0 || stickId != int.MinValue) ResetInteraction(); return; }
            try
            {
                ReadPointers();
                ApplyToShip();
            }
            catch (System.InvalidOperationException e)
            {
                // Включена только новая Input System — старый Input недоступен. BuildScript переключает это для сборки.
                legacyInputOk = false;
                Debug.LogError("SkyTide: старый Input Manager выключен. Project Settings → Player → Active Input Handling = Input Manager (Old) или Both. " + e.Message);
            }

            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (touchedOnce) hintAlpha = Mathf.MoveTowards(hintAlpha, 0f, Time.deltaTime * 0.5f);
        }

        void ReadPointers()
        {
            float h = ViewHeight;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    Vector2 p = new Vector2(t.position.x, h - t.position.y);
                    bool began = t.phase == TouchPhase.Began;
                    bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                    Pointer(t.fingerId, p, began, ended);
                }
            }
            else
            {
                Vector2 mp = Input.mousePosition;
                Vector2 p = new Vector2(mp.x, h - mp.y);
                if (Input.GetMouseButtonDown(0)) Pointer(-1, p, true, false);
                else if (Input.GetMouseButton(0)) Pointer(-1, p, false, false);
                else if (Input.GetMouseButtonUp(0)) Pointer(-1, p, false, true);

                float wheel = Input.mouseScrollDelta.y;
                if (cam != null && Mathf.Abs(wheel) > 0.01f) cam.distance *= Mathf.Pow(0.9f, wheel);
            }

            Pinch();
        }

        void Pointer(int id, Vector2 p, bool began, bool ended)
        {
            if (began)
            {
                touchedOnce = true;
                if (game != null && game.ships != null && game.ships.Length > 1 && ShipButtonRect().Contains(p))
                {
                    owners[id] = Owner.Button;
                    lastPos[id] = p;
                    game.NextShip();
                    return;
                }
                if (PerfPanel.Open && PerfPanel.Area.Contains(p)) { owners[id] = Owner.Button; lastPos[id] = p; return; }   // касания панели — ей
                if (FpsButtonRect().Contains(p))
                {
                    owners[id] = Owner.Button;
                    lastPos[id] = p;
                    PerfPanel.Toggle();
                    return;
                }
                // Кнопки «облака» на экране нет (просьба владельца): переключатель — в панели FPS.
                Broadside bs = Guns();
                if (bs != null && VolleyModeRect().Contains(p)) { owners[id] = Owner.Button; lastPos[id] = p; bs.ripple = !bs.ripple; return; }
                if (bs != null && VolleyRect(-1).Contains(p)) { owners[id] = Owner.Button; lastPos[id] = p; bs.Fire(-1); return; }
                if (bs != null && VolleyRect(1).Contains(p)) { owners[id] = Owner.Button; lastPos[id] = p; bs.Fire(1); return; }
                if (p.x < ViewWidth * 0.5f && stickId == int.MinValue)
                {
                    owners[id] = Owner.Stick;
                    stickId = id;
                    stickOrigin = p;
                    stickPos = p;
                }
                else
                {
                    owners[id] = Owner.Camera;
                    // Двойное касание одним пальцем — камера возвращается за корму.
                    float tapNow = Time.unscaledTime, tapR = Mathf.Min(ViewWidth, ViewHeight) * 0.09f;
                    if (cam != null && CameraFingers() == 1 && tapNow - lastCamTap < 0.32f && (p - lastCamTapPos).sqrMagnitude < tapR * tapR) { cam.Recenter(); lastCamTap = -10f; }
                    else { lastCamTap = tapNow; lastCamTapPos = p; }
                }
                lastPos[id] = p;
                return;
            }

            Owner o;
            if (!owners.TryGetValue(id, out o)) return;

            if (o == Owner.Stick) stickPos = p;
            else if (o == Owner.Camera && cam != null && CameraFingers() == 1)
            {
                Vector2 d = p - lastPos[id];
                float perPixel = orbitDegreesPerScreen / Mathf.Max(1f, ViewWidth);
                cam.yaw += d.x * perPixel;
                cam.pitch += d.y * perPixel;
                if (d.sqrMagnitude > 4f) cam.CancelRecenter();   // игрок сам повёл камеру — возврат за корму отменяется
            }
            lastPos[id] = p;

            if (ended)
            {
                owners.Remove(id);
                lastPos.Remove(id);
                if (id == stickId) stickId = int.MinValue;
            }
        }

        int CameraFingers()
        {
            int n = 0;
            foreach (KeyValuePair<int, Owner> kv in owners) if (kv.Value == Owner.Camera) n++;
            return n;
        }

        void Pinch()
        {
            if (cam == null || CameraFingers() < 2)
            {
                lastPinch = -1f;
                return;
            }
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int k = 0;
            foreach (KeyValuePair<int, Owner> kv in owners)
            {
                if (kv.Value != Owner.Camera) continue;
                if (k == 0) a = lastPos[kv.Key]; else if (k == 1) b = lastPos[kv.Key];
                k++;
            }
            float dist = Vector2.Distance(a, b);
            if (lastPinch > 0f && dist > 1f) cam.distance *= lastPinch / dist;
            lastPinch = dist;
        }

        void ApplyToShip(bool readKeyboard=true)
        {
            float r = Mathf.Max(1f, Layout(ViewWidth, ViewHeight).radius);
            if (stickId != int.MinValue)
            {
                Vector2 v = (stickPos - stickOrigin) / r;
                if (v.magnitude > 1f) v = v.normalized;
                // Экран: y вниз → вверх по экрану = вперёд.
                stickValue = new Vector2(DeadZone(v.x), DeadZone(-v.y));
            }
            else
            {
                stickValue = Vector2.zero;
            }

            // Рукоять хода: отклонение джойстика вверх/вниз за порог — на ступень; держишь — ещё ступень каждые 0.45 с.
            float sy = stickValue.y, gearNow = Time.unscaledTime;
            if (Mathf.Abs(sy) > 0.55f)
            {
                if (gearArmed || gearNow >= gearRepeatAt)
                {
                    gear = Mathf.Clamp(gear + (sy > 0f ? 1 : -1), 0, GearThrottle.Length - 1);
                    gearRepeatAt = gearNow + 0.45f; gearArmed = false;
                }
            }
            else if (Mathf.Abs(sy) < 0.3f) gearArmed = true;

            float kx = 0f;
            if(readKeyboard)
            {
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kx -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kx += 1f;
                if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) SetGear(gear + 1);
                if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) SetGear(gear - 1);
                if (Input.GetKeyDown(KeyCode.Tab) && game != null) game.NextShip();
                Broadside guns = Guns();
                if (guns != null && Input.GetKeyDown(KeyCode.Q)) guns.Fire(-1);
                if (guns != null && Input.GetKeyDown(KeyCode.E)) guns.Fire(1);
                if (guns != null && Input.GetKeyDown(KeyCode.R)) guns.ripple = !guns.ripple;
            }

            if (ship != null)
            {
                ship.turn = Mathf.Clamp(stickValue.x + kx, -1f, 1f);
                ship.throttle = GearThrottle[gear];
            }
        }

        Rect ShipButtonRect() { return Layout(ViewWidth,ViewHeight).ship; }
        /// <summary>Плашка FPS — касание открывает/закрывает отладочную панель.</summary>
        Rect FpsButtonRect() { return Layout(ViewWidth,ViewHeight).fps; }

        Rect CloudButtonRect() { return Layout(ViewWidth,ViewHeight).clouds; }
        Rect VolleyModeRect() { return Layout(ViewWidth,ViewHeight).mode; }
        Rect VolleyRect(int side) { HudLayout l=Layout(ViewWidth,ViewHeight); return side<0?l.port:l.starboard; }

        public Broadside Guns()
        {
            if(ship==null)return null;
            Broadside b=ship.GetComponent<Broadside>();
            return b!=null&&b.HasCannons?b:null;
        }

        static float DeadZone(float x)
        {
            const float dz = 0.12f;
            float a = Mathf.Abs(x);
            if (a < dz) return 0f;
            return Mathf.Sign(x) * (a - dz) / (1f - dz);
        }

        string buildText;
        public string BuildText
        {
            get
            {
                if(buildText==null)
                {
                    TextAsset bn=Resources.Load<TextAsset>("BuildNumber");
                    buildText=bn!=null?"СБОРКА "+bn.text.Trim():"РЕДАКТОР";
                }
                return buildText;
            }
        }
        public float Fps { get {return fps;} }
        public float HintAlpha {get {return hintAlpha;} }
        public bool StickActive {get {return stickId!=int.MinValue;} }
        public Vector2 StickOrigin {get {return stickOrigin;} }
        public int HudDrawCount {get {return hud.DrawCount;} }
        public int HudMissingGlyphs {get {return hud.MissingGlyphs;} }
        public int HudGlyphsDrawn {get {return hud.GlyphsDrawn;} }
        public int HudTextIssues {get {return hud.TextIssues;} }
        public string HudFirstTextIssue {get {return hud.FirstTextIssue;} }
        public int HudMissingCharacter {get {return hud.MissingCharacter;} }
        public Vector2 StickOffset(float radius)
        {
            return StickActive?Vector2.ClampMagnitude(stickPos-stickOrigin,radius):Vector2.zero;
        }
        public bool Pressed(Rect rect)
        {
            foreach(KeyValuePair<int,Owner> kv in owners)
                if(kv.Value==Owner.Button&&rect.Contains(lastPos[kv.Key]))return true;
            return false;
        }
        int ViewWidth
        {
            get {
#if UNITY_EDITOR
                if(captureVisible)return captureWidth;
#endif
                return Screen.width;
            }
        }
        int ViewHeight
        {
            get {
#if UNITY_EDITOR
                if(captureVisible)return captureHeight;
#endif
                return Screen.height;
            }
        }
        public HudLayout Layout(int width,int height)
        {
#if UNITY_EDITOR
            if(captureVisible)return HudLayout.Build(width,height,captureSafe,stickRadius);
#endif
            Rect s=Screen.safeArea;
            float sw=Mathf.Max(1,Screen.width),sh=Mathf.Max(1,Screen.height);
            Rect safe=new Rect(s.x/sw*width,(sh-s.yMax)/sh*height,s.width/sw*width,s.height/sh*height);
            return HudLayout.Build(width,height,safe,stickRadius);
        }
        public void DrawHud(RenderTexture destination,int width,int height)
        {
            if(!isActiveAndEnabled||game==null||ship==null)return;
#if UNITY_EDITOR
            if(!Application.isPlaying&&!captureVisible)return;
#endif
            hud.Draw(this,destination,width,height,hudShader,hudFont);
        }
        public void ResetInteraction()
        {
            owners.Clear();lastPos.Clear();stickId=int.MinValue;stickValue=Vector2.zero;lastPinch=-1f;gear=1;gearArmed=true;
            if(ship!=null){ship.turn=0f;ship.throttle=0f;}
        }
        void OnApplicationFocus(bool focused) {if(!focused)ResetInteraction();}
        void OnApplicationPause(bool paused) {if(paused)ResetInteraction();}
        void OnDisable() {ResetInteraction();}
        void OnDestroy() {hud.Dispose();}
#if UNITY_EDITOR
        bool captureVisible;
        int captureWidth,captureHeight;
        Rect captureSafe;
        public void SetHudCapture(bool visible,int width=800,int height=380,Rect? safe=null)
        {
            captureVisible=visible;captureWidth=width;captureHeight=height;captureSafe=safe??new Rect(0,0,width,height);
            ResetInteraction();
        }
        public void PointerCapture(int id,Vector2 p,bool began,bool ended) {Pointer(id,p,began,ended);}
        public bool CapturedAsCamera(int id) {Owner o;return owners.TryGetValue(id,out o)&&o==Owner.Camera;}
        public int ActivePointersCapture {get {return owners.Count;} }
        public void PinchCapture() {Pinch();}
        public void ApplyStickCapture() {ApplyToShip(false);}
#endif
    }
}
