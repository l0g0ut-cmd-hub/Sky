using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Управление для телефона (и мыши/клавиатуры в редакторе).
    /// Левая половина экрана — плавающий джойстик: появляется там, где коснулись; вверх — ход вперёд,
    /// вниз — назад/тормоз, в стороны — поворот. Правая половина — вращение камеры вокруг корабля,
    /// двумя пальцами — приближение. Мышь: левая кнопка как палец, колесо — приближение.
    /// Клавиатура: WASD/стрелки — корабль.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public ShipController ship;
        public OrbitCamera cam;

        [Tooltip("Радиус хода джойстика в долях от меньшей стороны экрана")]
        public float stickRadius = 0.12f;
        [Tooltip("Градусов поворота камеры на ширину экрана")]
        public float orbitDegreesPerScreen = 220f;

        enum Owner { Stick, Camera }

        readonly Dictionary<int, Owner> owners = new Dictionary<int, Owner>();
        readonly Dictionary<int, Vector2> lastPos = new Dictionary<int, Vector2>();
        int stickId = int.MinValue;
        Vector2 stickOrigin;
        Vector2 stickPos;
        Vector2 stickValue;
        float lastPinch = -1f;
        float hintAlpha = 1f;
        bool touchedOnce;
        bool legacyInputOk = true;

        Texture2D disc;
        Texture2D ring;
        GUIStyle hintStyle;

        void Awake()
        {
            Input.simulateMouseWithTouches = false;
            disc = MakeDisc(128, false);
            ring = MakeDisc(128, true);
        }

        void Update()
        {
            if (!legacyInputOk) return;
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

            if (touchedOnce) hintAlpha = Mathf.MoveTowards(hintAlpha, 0f, Time.deltaTime * 0.5f);
        }

        void ReadPointers()
        {
            float h = Screen.height;
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
                if (p.x < Screen.width * 0.5f && stickId == int.MinValue)
                {
                    owners[id] = Owner.Stick;
                    stickId = id;
                    stickOrigin = p;
                    stickPos = p;
                }
                else
                {
                    owners[id] = Owner.Camera;
                }
                lastPos[id] = p;
                return;
            }

            Owner o;
            if (!owners.TryGetValue(id, out o)) return;

            if (o == Owner.Stick) stickPos = p;
            else if (cam != null && CameraFingers() == 1)
            {
                Vector2 d = p - lastPos[id];
                float perPixel = orbitDegreesPerScreen / Mathf.Max(1f, Screen.width);
                cam.yaw += d.x * perPixel;
                cam.pitch += d.y * perPixel;
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

        void ApplyToShip()
        {
            float r = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) * stickRadius);
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

            float kx = 0f, ky = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kx -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kx += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ky += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ky -= 1f;

            if (ship != null)
            {
                ship.turn = Mathf.Clamp(stickValue.x + kx, -1f, 1f);
                ship.throttle = Mathf.Clamp(stickValue.y + ky, -1f, 1f);
            }
        }

        static float DeadZone(float x)
        {
            const float dz = 0.12f;
            float a = Mathf.Abs(x);
            if (a < dz) return 0f;
            return Mathf.Sign(x) * (a - dz) / (1f - dz);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float u = Mathf.Min(Screen.width, Screen.height);
            float r = u * stickRadius;

            Color prev = GUI.color;
            if (stickId != int.MinValue)
            {
                Vector2 v = stickPos - stickOrigin;
                if (v.magnitude > r) v = v.normalized * r;
                DrawCircle(ring, stickOrigin, r * 1.15f, new Color(1f, 0.85f, 0.55f, 0.55f));
                DrawCircle(disc, stickOrigin, r * 1.1f, new Color(0.08f, 0.06f, 0.05f, 0.3f));
                DrawCircle(disc, stickOrigin + v, r * 0.45f, new Color(0.95f, 0.75f, 0.4f, 0.85f));
            }
            else
            {
                // Подсказка, где джойстик
                Vector2 home = new Vector2(u * 0.2f, Screen.height - u * 0.22f);
                DrawCircle(ring, home, r * 1.15f, new Color(1f, 0.85f, 0.55f, 0.25f));
                DrawCircle(disc, home, r * 0.45f, new Color(0.95f, 0.75f, 0.4f, 0.3f));
            }

            if (hintAlpha > 0.01f)
            {
                if (hintStyle == null)
                {
                    hintStyle = new GUIStyle(GUI.skin.label);
                    hintStyle.alignment = TextAnchor.MiddleCenter;
                    hintStyle.wordWrap = true;
                }
                hintStyle.fontSize = Mathf.RoundToInt(u * 0.035f);
                Rect rr = new Rect(Screen.width * 0.1f, u * 0.04f, Screen.width * 0.8f, u * 0.1f);
                string text = "Левая половина экрана — штурвал   •   Правая — вращать камеру, двумя пальцами — приблизить";
                GUI.color = new Color(0f, 0f, 0f, 0.6f * hintAlpha);
                GUI.Label(new Rect(rr.x + 2f, rr.y + 2f, rr.width, rr.height), text, hintStyle);
                GUI.color = new Color(1f, 0.92f, 0.8f, hintAlpha);
                GUI.Label(rr, text, hintStyle);
            }
            GUI.color = prev;
        }

        static void DrawCircle(Texture2D tex, Vector2 center, float radius, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), tex);
        }

        static Texture2D MakeDisc(int size, bool ringOnly)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a;
                    if (ringOnly) a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) / 0.05f);
                    else a = Mathf.Clamp01((1f - d) / 0.04f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
