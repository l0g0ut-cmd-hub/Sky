using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Чёткий экранный HUD после тонмаппинга. Общий путь игры и Camera.Render в CI.</summary>
    public sealed class ShipHud
    {
        struct Label { public Rect rect; public string text; public int size; public Color color; }
        readonly List<Label> labels = new List<Label>(24);
        Material material;
        Font font;
        readonly Color gold = new Color(0.79f, 0.61f, 0.35f);
        readonly Color cream = new Color(0.96f, 0.9f, 0.77f);
        readonly Color muted = new Color(0.64f, 0.65f, 0.66f);
        public int MissingGlyphs { get; private set; }
        public int GlyphsDrawn { get; private set; }
        public int DrawCount { get; private set; }
        public int MissingCharacter { get; private set; }
        /// <summary>Проверка текста (CI): надписи мельче читаемого или за пределами экрана; первая — с текстом.</summary>
        public int TextIssues { get; private set; }
        public string FirstTextIssue { get; private set; }
        float minGlyph; Rect screenRect;
        /// <summary>Внутреннее поле плашки набора: без рамки и углового орнамента (рамка ~11/103, орнамент ~38/103 по ширине).</summary>
        static Rect Content(Rect r)
        {
            float h = r.height;
            return new Rect(r.x + h * 0.39f, r.y + h * 0.15f, Mathf.Max(0f, r.width - h * 0.78f), h * 0.7f);
        }
        /// <summary>Надпись в плашке: строка (доли внутреннего поля по высоте) — всегда внутри поля.</summary>
        void PlateText(Rect plate, float top, float height, string text, float size, Color color)
        {
            // Единая шкала: size — кегль, заглавные = 0,7 кегля; ужимается только если не влезает в поле (по высоте 62% строки,
            // по ширине — поле между орнаментами с запасом 8%). Одинаковый size → одинаковый текст на всех плашках.
            Rect c = Content(plate); float rowH = c.height * height, cy = c.y + c.height * top + rowH * 0.5f;
            float capH = Mathf.Min(size * 0.7f, rowH * 0.62f, c.width * 0.92f / Mathf.Max(0.01f, CapRatio(text)));
            CapText(c.center.x, cy, text, capH, color, true);
        }

        /// <summary>Надпись со значком (icons2) слева в поле плашки; текст — по центру оставшегося места.</summary>
        void IconText(Rect plate, int icon, string text, float size, Color color)
        {
            // Значок + текст одной группой по центру поля между угловыми орнаментами; размер — по высоте плашки,
            // с проверкой ширины (CapText — без ужатия).
            Rect c = Content(plate); float h = plate.height;
            float x0 = c.x + h * 0.12f, x1 = c.xMax - h * 0.12f, fw = x1 - x0;
            float capH = Mathf.Min(size * 0.7f, c.height * 0.5f), ratio = CapRatio(text);
            capH = Mathf.Min(capH, fw * 0.92f / Mathf.Max(0.01f, ratio + 2.1f));
            float ic = capH * 1.75f, g = capH * 0.35f, tw = ratio * capH, total = ic + g + tw, gx = plate.center.x - total * 0.5f;
            Icon2(icon, new Rect(gx, plate.center.y - ic * 0.5f, ic, ic), Color.white);
            CapText(gx + ic + g, plate.center.y, text, capH, color, false);
        }
        void Tint(Color c) { GL.Color(QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c); }
        static void Vertex(Vector2 p, Vector2 uv) { GL.TexCoord2(uv.x, uv.y); GL.Vertex3(p.x, p.y, 0f); }
        static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vertex(a, ua); Vertex(b, ub); Vertex(c, uc);
            Vertex(a, ua); Vertex(c, uc); Vertex(d, ud);
        }
        void Box(Rect r, Color c)
        {
            Tint(c);
            Quad(new Vector2(r.x,r.y),new Vector2(r.xMax,r.y),new Vector2(r.xMax,r.yMax),new Vector2(r.x,r.yMax),Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
        }
        // ---------- нарисованный набор HUD (Resources/UI/hud_kit.png, 1254×1254, координаты в пикселях листа) ----------
        struct Spr { public Vector2 a, b, c, d, ua, ub, uc, ud; public Color col; public Texture2D tex; }
        readonly List<Spr> sprites = new List<Spr>(64);
        Texture2D kit; bool kitTried;
        const float Kit = 1254f;
        static readonly Rect[] PanelN = { new Rect(21,80,575,104), new Rect(619,80,345,102), new Rect(986,80,248,102) };
        static readonly Rect[] PanelL = { new Rect(12,218,594,123), new Rect(607,218,365,123), new Rect(975,218,268,124) };
        static readonly Rect[] PanelP = { new Rect(21,370,575,105), new Rect(620,370,343,105), new Rect(986,370,248,105) };
        static readonly Rect KWheel = new Rect(24,503,422,422), KRadar = new Rect(447,498,407,437), KArrow = new Rect(872,606,72,81),
            KGem = new Rect(870,722,73,84), KCannonL = new Rect(960,546,271,174), KCannonR = new Rect(966,740,270,175),
            KKnob = new Rect(61,930,159,163), KGroove = new Rect(280,977,442,67), KFill = new Rect(758,977,439,67);
        bool HasKit { get { if (!kitTried) { kitTried = true; kit = Resources.Load<Texture2D>("UI/hud_kit"); } return kit != null; } }
        static Vector2 UV(float x, float y) { return new Vector2(x / Kit, 1f - y / Kit); }
        void Sprite(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Rect src, Color col)
        {
            sprites.Add(new Spr { a = a, b = b, c = c, d = d, ua = UV(src.x, src.y), ub = UV(src.xMax, src.y), uc = UV(src.xMax, src.yMax), ud = UV(src.x, src.yMax), col = col, tex = kit });
        }
        void Sprite(Rect dst, Rect src, Color col)
        {
            Sprite(new Vector2(dst.x, dst.y), new Vector2(dst.xMax, dst.y), new Vector2(dst.xMax, dst.yMax), new Vector2(dst.x, dst.yMax), src, col);
        }
        void SpriteRot(Vector2 center, Vector2 size, float angle, Rect src, Color col)
        {
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            Vector2 ex = new Vector2(cs, sn) * size.x * 0.5f, ey = new Vector2(-sn, cs) * size.y * 0.5f;
            Sprite(center - ex - ey, center + ex - ey, center + ex + ey, center - ex + ey, src, col);
        }
        /// <summary>Растяжение по 9 частям: углы с орнаментом не искажаются.</summary>
        void Slice9(Rect dst, Rect src, float bs, float bd, Color col)
        {
            float[] dx = { dst.x, dst.x + bd, dst.xMax - bd, dst.xMax }, dy = { dst.y, dst.y + bd, dst.yMax - bd, dst.yMax };
            float[] sx = { src.x, src.x + bs, src.xMax - bs, src.xMax }, sy = { src.y, src.y + bs, src.yMax - bs, src.yMax };
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
                Sprite(new Rect(dx[i], dy[j], dx[i + 1] - dx[i], dy[j + 1] - dy[j]), new Rect(sx[i], sy[j], sx[i + 1] - sx[i], sy[j + 1] - sy[j]), col);
        }
        void KitPanel(Rect r, bool lit, bool pressed)
        {
            float asp = r.width / Mathf.Max(1f, r.height);
            int k = asp > 4.6f ? 0 : asp > 2.9f ? 1 : 2;
            float sc = r.height / PanelN[k].height;
            if (lit)
            {
                float g = 10f * sc;
                Slice9(new Rect(r.x - g, r.y - g, r.width + g * 2f, r.height + g * 2f), PanelL[k], 40f, 40f * sc, Color.white);
            }
            else Slice9(r, pressed ? PanelP[k] : PanelN[k], 30f, 30f * sc, Color.white);
        }
        /// <summary>Отдельная картинка целиком (радар), повёрнутая вокруг центра.</summary>
        void Image(Texture2D tex, Vector2 center, float size, float angle, Color col)
        {
            if (tex == null) return;
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            Vector2 ex = new Vector2(cs, sn) * size * 0.5f, ey = new Vector2(-sn, cs) * size * 0.5f;
            sprites.Add(new Spr { a = center - ex - ey, b = center + ex - ey, c = center + ex + ey, d = center - ex + ey,
                ua = new Vector2(0, 1), ub = new Vector2(1, 1), uc = new Vector2(1, 0), ud = new Vector2(0, 0), col = col, tex = tex });
        }
        void FlushSprites()
        {
            int i = 0;
            try
            {
                while (i < sprites.Count)
                {
                    Texture2D t = sprites[i].tex;
                    int j = i; while (j < sprites.Count && sprites[j].tex == t) j++;
                    if (t != null)
                    {
                        material.SetTexture("_MainTex", t);
                        if (material.SetPass(2))
                        {
                            GL.Begin(GL.TRIANGLES);
                            try { for (int k = i; k < j; k++) { Spr q = sprites[k]; Tint(q.col); Quad(q.a, q.b, q.c, q.d, q.ua, q.ub, q.uc, q.ud); } }
                            finally { GL.End(); }
                        }
                    }
                    i = j;
                }
            }
            finally { sprites.Clear(); }
        }

        // ---------- радар (Resources/UI/radar_*, 1024²: рамка, стекло, луч; 128²: свой корабль, цель, цель за пределом) ----------
        Texture2D rFrame, rFace, rSweep, rShip, rEnemy, rFar, rAlly, rMerchant, rFort, rBerth; bool radarTried;
        bool HasRadar
        {
            get
            {
                if (!radarTried)
                {
                    radarTried = true;
                    rFrame = Resources.Load<Texture2D>("UI/radar_frame"); rFace = Resources.Load<Texture2D>("UI/radar_face");
                    rSweep = Resources.Load<Texture2D>("UI/radar_sweep"); rShip = Resources.Load<Texture2D>("UI/radar_ship");
                    rEnemy = Resources.Load<Texture2D>("UI/radar_enemy"); rFar = Resources.Load<Texture2D>("UI/radar_enemy_far");
                    rAlly = Resources.Load<Texture2D>("UI/radar_ally"); rMerchant = Resources.Load<Texture2D>("UI/radar_merchant");
                    rFort = Resources.Load<Texture2D>("UI/radar_fort"); rBerth = Resources.Load<Texture2D>("UI/radar_berth");
                }
                return rFrame != null && rFace != null;
            }
        }
        const float SweepPeriod = 3f;
        bool moduleFrame;
        /// <summary>Текст во вкладках модуля радара: враг (дальность · % корпуса), причал, масштаб радара, ход.</summary>
        void ModuleTexts(TouchControls input, HudLayout l)
        {
            float best = float.MaxValue, bestHealth = 1f;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive || (Application.isPlaying && !t.AtWar)) continue;   // вкладка «ВРАГ» — только враждебные
                Vector3 d = t.transform.position - input.ship.transform.position; d.y = 0f;
                if (d.magnitude < best) { best = d.magnitude; bestHealth = t.Health01; }
            }
            float fs = l.unit * 0.03f;   // кегль вкладок = кегль остальных плашек
            Color amber = new Color(1f, 0.74f, 0.55f, 1f), grey = new Color(0.78f, 0.79f, 0.82f, 1f);
            if (best < float.MaxValue)
            {
                string a = "ВРАГ " + Range(best);
                Color ac = best <= GunRange(input) ? amber : grey;   // серым — дальше, чем достают свои пушки
                if (bestHealth < 0.995f) TabPair(l.tabEnemy, a + " · ", ac, Mathf.RoundToInt(bestHealth * 100f) + "%", new Color(1f, 0.35f, 0.28f, 1f), fs);
                else TabText(l.tabEnemy, a, fs, ac);
            }
            else TabText(l.tabEnemy, Application.isPlaying ? "ВРАЖДЕБНЫХ НЕТ" : "ВРАГ —", fs, grey);
            SkyFortress fort = SkyFortress.Instance;
            if (fort != null) { Vector3 d = fort.BerthWorld - input.ship.transform.position; d.y = 0f; TabText(l.tabBerth, "ПРИЧАЛ " + Range(d.magnitude), fs, fort.Hostile ? new Color(1f, 0.55f, 0.45f, 1f) : cream); }
            else TabText(l.tabBerth, "ПРИЧАЛ —", fs, grey);
            TabText(l.tabRadar, "РАДАР " + Range(input.RadarRange), fs, cream);
            TabText(l.speed, Cached(3, input.Gear * 1000 + Mathf.RoundToInt(Mathf.Abs(input.ship.Speed)), v => TouchControls.GearNames[v / 1000] + " · " + (v % 1000) + " м/с"), fs, cream);
        }
        void TabText(Rect r, string t, float size, Color c) { float capH = Mathf.Min(size * 0.7f, r.height * 0.5f, r.width * 0.9f / Mathf.Max(0.01f, CapRatio(t))); CapText(r.center.x, r.center.y, t, capH, c, true); }
        void TabPair(Rect r, string a, Color ca, string b, Color cb, float size)
        {
            float ra = CapRatio(a), rb = CapRatio(b), capH = Mathf.Min(size * 0.7f, r.height * 0.5f, r.width * 0.9f / Mathf.Max(0.01f, ra + rb));
            float x = r.center.x - (ra + rb) * capH * 0.5f;
            CapText(x, r.center.y, a, capH, ca, false); CapText(x + ra * capH, r.center.y, b, capH, cb, false);
        }
        /// <summary>Две части строки разным цветом, вместе — по центру прямоугольника.</summary>
        void TextPair(Rect r, string a, Color ca, string b, Color cb, float size)
        {
            float wa = TextWidth(a, size), wb = TextWidth(b, size), x = r.center.x - (wa + wb) * 0.5f, pad = r.height * 0.3f + 4f;
            // Поля DrawLabel (0,3 высоты с боков) — снаружи: прямоугольник шире слова, центр — по середине слова.
            Text(new Rect(x - pad, r.y, wa + pad * 2f, r.height), a, size, ca);
            Text(new Rect(x + wa - pad, r.y, wb + pad * 2f, r.height), b, size, cb);
        }
        // Точная вёрстка: DrawLabel подгоняет текст под 80% высоты прямоугольника и ширину без полей. Здесь размер задаётся
        // высотой заглавной буквы (capH) — прямоугольник строится под неё, ширина считается заранее, ничего не ужимается.
        float CapRatio(string t)   // ширина строки на 1 px высоты заглавной
        {
            if (font == null || string.IsNullOrEmpty(t)) return 0f;
            float known; if (ratioCache.TryGetValue(t, out known)) return known;   // замер строки — один раз, а не каждый кадр
            if (ratioCache.Count > 600) ratioCache.Clear();
            float measured = MeasureRatio(t); ratioCache[t] = measured; return measured;
        }
        readonly Dictionary<string, float> ratioCache = new Dictionary<string, float>();
        float MeasureRatio(string t)
        {
            const int sz = 48; font.RequestCharactersInTexture(t, sz, FontStyle.Normal); font.RequestCharactersInTexture("Н", sz, FontStyle.Normal);
            CharacterInfo c, cap; if (!font.GetCharacterInfo('Н', out cap, sz, FontStyle.Normal) || cap.maxY <= cap.minY) return 0.6f * t.Length;
            float w = 0f; for (int i = 0; i < t.Length; i++) if (font.GetCharacterInfo(t[i], out c, sz, FontStyle.Normal)) w += c.advance;
            return w / (cap.maxY - cap.minY);
        }
        /// <summary>Строка с заглавными высотой capH; x — левый край текста (или центр при centered).</summary>
        void CapText(float x, float cy, string t, float capH, Color color, bool centered)
        {
            float w = CapRatio(t) * capH, h = capH / 0.8f, side = Mathf.Max(6f, h * 0.3f);
            float left = centered ? x - w * 0.5f : x;
            Text(new Rect(left - side - 1f, cy - h * 0.5f, w + side * 2f + 2f, h), t, capH * 1.4f, color);
        }
        float TextWidth(string t, float size)
        {
            if (font == null || string.IsNullOrEmpty(t)) return 0f;
            int sz = Mathf.Max(9, Mathf.RoundToInt(size)); font.RequestCharactersInTexture(t, sz, FontStyle.Normal);
            float w = 0f; CharacterInfo c;
            for (int i = 0; i < t.Length; i++) if (font.GetCharacterInfo(t[i], out c, sz, FontStyle.Normal)) w += c.advance;
            return w;
        }
        void RadarArt(TouchControls input, HudLayout l, Vector2 c, float R, float range)
        {
            float size = R * 2f * 1024f / 900f;           // рамка (с выступами) — 900 из 1024 пикселей по ширине
            float face = size * (250f / 1024f);            // внутренний край кольца рамки
            float t = Time.realtimeSinceStartup;
            float sweep = Mathf.Repeat(t / SweepPeriod, 1f) * Mathf.PI * 2f;
            const float edge0 = -60f * Mathf.Deg2Rad;     // передний край луча на картинке (вверх-вправо)
            // Радар — «нос вверх»: отметки уже в осях корабля, а теперь и стекло со своей картой поворачивается вслед за
            // курсом, и стороны света (С/В/Ю/З) бегут по кругу — видно, что радар вращается вместе с кораблём.
            float hdg = input.ship.transform.eulerAngles.y * Mathf.Deg2Rad;
            Image(rFace, c, size, -hdg, Color.white);
            {
                string[] cards = { "С", "В", "Ю", "З" };
                for (int k = 0; k < 0; k++)   // стороны света на радаре убраны совсем (владелец 15.11: «С убери») — они на компасе курса
                {
                    Vector3 wd = Quaternion.Euler(0f, k * 90f, 0f) * Vector3.forward;
                    Vector3 ld = input.ship.transform.InverseTransformDirection(wd);
                    Vector2 sd = new Vector2(ld.x, -ld.z); if (sd.sqrMagnitude < 1e-4f) continue; sd.Normalize();
                    Vector2 cp = c + sd * face * 0.74f; float cs = R * 0.3f;
                    Text(new Rect(cp.x - cs, cp.y - cs * 0.5f, cs * 2f, cs), cards[k], R * 0.16f, k == 0 ? new Color(1f, 0.5f, 0.38f, 0.95f) : new Color(0.93f, 0.85f, 0.65f, 0.75f));
                }
            }
            if (rSweep != null) Image(rSweep, c, size, sweep, new Color(1f, 1f, 1f, 0.9f));
            // Порядок на радаре (владелец 15.11: «каша»): при 18 врагах у обода лежали все дальние. Теперь за краем —
            // только 2 ближайших дальних (стрелкой-направлением), остальные не рисуются; внутри — все.
            float far1 = float.MaxValue, far2 = float.MaxValue;
            foreach (TargetShip ts in TargetShip.All)
            {
                if (ts == null || !ts.Alive) continue;
                float dd = Vector3.Distance(ts.transform.position, input.ship.transform.position);
                if (dd <= range) continue;
                if (dd < far1) { far2 = far1; far1 = dd; } else if (dd < far2) far2 = dd;
            }
            foreach (TargetShip ts in TargetShip.All)
            {
                if (ts == null || !ts.Alive) continue;
                Vector3 local = input.ship.transform.InverseTransformPoint(ts.transform.position);
                Vector2 v = new Vector2(local.x, -local.z); float dist = v.magnitude; bool outside = dist > range;
                if (outside && dist > far2 + 0.01f) continue;
                Vector2 dir = dist > 1e-3f ? v / dist : Vector2.up;
                Vector2 p = c + dir * Mathf.Min(dist / range, 1f) * face * (outside ? 0.93f : 0.88f);
                // Отметка вспыхивает, когда по ней проходит луч, и гаснет до следующего оборота.
                float beta = Mathf.Atan2(dir.y, dir.x);
                float since = Mathf.Repeat(edge0 + sweep - beta, Mathf.PI * 2f) / (Mathf.PI * 2f) * SweepPeriod;
                float glow = 0.55f + 0.45f * Mathf.Exp(-since * 1.6f);
                bool isTarget = input.Guns() != null && input.Guns().AimTargetShip == ts;
                // Мельче (было 0.24 R): при 18 врагах отметки сливались; цель борта — крупнее, с курсом.
                float g = R * (outside ? 0.2f : isTarget ? 0.2f : 0.13f) * (0.9f + 0.15f * Mathf.Exp(-since * 3f));
                float rel = (ts.transform.eulerAngles.y - input.ship.transform.eulerAngles.y) * Mathf.Deg2Rad;
                // Политика отношений: цвет — по отношению к нам, а не по флагу. Красная стрелка — враждебен; светлая — спокоен,
                // жёлтая — настороже, оранжевая — бежит, серая — сдался. Размер — по классу (адмиральский заметно крупнее).
                if (ts.Class != ShipClass.None) g *= 0.75f + 0.28f * Mathf.Sqrt(ShipClasses.Get(ts.Class).length / 40f);
                bool hostile = !Application.isPlaying || ts.AtWar;
                if (outside && rFar != null) Image(rFar, p, g * 0.8f, beta + Mathf.PI * 0.5f, hostile ? new Color(1f, 1f, 1f, glow) : new Color(0.6f, 0.6f, 0.6f, glow * 0.7f));
                else if (hostile && rEnemy != null) Image(rEnemy, p, g * 1.15f, rel, new Color(1f, 1f, 1f, glow));
                else if (rShip != null)
                {
                    Color mc = ts.Mood == Attitude.Wary ? new Color(1f, 0.85f, 0.3f, glow) : ts.Mood == Attitude.Flee ? new Color(1f, 0.58f, 0.3f, glow)
                             : ts.Mood == Attitude.Surrender ? new Color(0.55f, 0.55f, 0.55f, glow) : new Color(0.92f, 0.9f, 0.84f, glow * 0.85f);
                    Image(rShip, p, g * 1.1f, rel, mc);
                }
                if (!outside && isTarget) { float rr = g * 0.95f; for (int q = 0; q < 24; q += 2) { float a0 = q * Mathf.PI / 12f, a1 = (q + 1) * Mathf.PI / 12f; Line(p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rr, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rr, Mathf.Max(1.2f, R * 0.02f), new Color(1f, 0.82f, 0.45f, glow)); } }   // цель борта — пунктирное кольцо
            }
            // Союзники (сторожевой крепости): зелёная отметка-кораблик, за краем радара — у обода.
            if (rShip != null) foreach (AllyShip ally in AllyShip.All)
            {
                if (ally == null) continue;
                Vector3 al = input.ship.transform.InverseTransformPoint(ally.transform.position);
                Vector2 av = new Vector2(al.x, -al.z); float ad = av.magnitude;
                Vector2 adir = ad > 1e-3f ? av / ad : Vector2.up;
                float arel = (ally.transform.eulerAngles.y - input.ship.transform.eulerAngles.y) * Mathf.Deg2Rad;
                if (!ally.Alive) continue;
                if (ad > range && ally.role != AllyShip.Role.Merchant && !ally.UnderAttack) continue;   // дальние свои — только торговец и те, кто под огнём
                Color ac = ally.role == AllyShip.Role.Merchant ? new Color(1f, 0.92f, 0.45f) : new Color(0.55f, 1f, 0.65f);
                if (ally.UnderAttack && Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.5f) ac = new Color(1f, 0.35f, 0.25f);   // под огнём — мигает красным
                ac.a = ad > range ? 0.6f : 1f;
                Vector2 ap = c + adir * Mathf.Min(ad / range, 1f) * face * 0.9f;
                bool merch = ally.role == AllyShip.Role.Merchant;
                Color tint = ally.UnderAttack && Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.5f ? new Color(1f, 0.45f, 0.4f, ac.a) : new Color(1f, 1f, 1f, ac.a);
                if (merch && rMerchant != null) Image(rMerchant, ap, R * 0.15f, 0f, tint);
                else if (!merch && rAlly != null) Image(rAlly, ap, R * 0.15f, arel, tint);
                else Image(rShip, ap, R * 0.14f, arel, ac);
            }
            // Крепость-причал: значок «ангар» (лист иконок), за краем радара — прижат к ободу.
            SkyFortress fort = SkyFortress.Instance;
            if (fort != null)
            {
                LoadBanners();
                Vector3 local = input.ship.transform.InverseTransformPoint(fort.BerthWorld);   // значок — на причале: туда и идти
                Vector2 v = new Vector2(local.x, -local.z); float dist = v.magnitude;
                Vector2 dir = dist > 1e-3f ? v / dist : Vector2.up;
                Vector2 p = c + dir * Mathf.Min(dist / range, 1f) * face * 0.9f;
                float g = R * 0.3f;
                // Граница безопасной зоны — пунктир на циферблате (красный при тревоге).
                float zk = face * 0.9f / range; Vector3 zl = input.ship.transform.InverseTransformPoint(fort.Center);
                Vector2 zc = c + new Vector2(zl.x, -zl.z) * zk; float zr = fort.safeRadius * zk, lim = face * 0.94f;
                Color zcol = fort.Hostile ? new Color(1f, 0.45f, 0.35f, 0.8f) : new Color(0.62f, 0.88f, 0.69f, 0.75f);
                const int zn = 72;
                // Граница зоны на радаре — только при тревоге (в мире её теперь видно зелёным туманом).
                for (int i = 0; i < (fort.Hostile ? zn : 0); i += 2)
                {
                    float a0 = i * Mathf.PI * 2f / zn, a1 = (i + 1) * Mathf.PI * 2f / zn;
                    Vector2 q0 = zc + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * zr, q1 = zc + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * zr;
                    if ((q0 - c).sqrMagnitude > lim * lim || (q1 - c).sqrMagnitude > lim * lim) continue;
                    Line(q0, q1, Mathf.Max(1.2f, R * 0.025f), zcol);
                }
                Texture2D ft = fort.Hostile ? rFort : rBerth;   // причал — якорь; при тревоге — башня крепости, красным
                if (ft != null) Image(ft, p, g * 0.75f, 0f, fort.Hostile ? new Color(1f, 0.55f, 0.45f, 1f) : Color.white);
                else Icon(9, new Rect(p.x - g * 0.5f, p.y - g * 0.5f, g, g), fort.Hostile ? new Color(1f, 0.45f, 0.35f, 1f) : Color.white);
            }
            if (!moduleFrame) Image(rFrame, c, size, 0f, Color.white);   // у модуля свой обод
            // Ветер: голубая стрелка у обода убрана (владелец 15.11 не понял, что это; лишняя деталь в каше). Ветер — в отчёте.
            if (false)
            {
                Vector3 wl = input.ship.transform.InverseTransformDirection(WindField.Dir);
                Vector2 wd = new Vector2(wl.x, -wl.z); if (wd.sqrMagnitude > 1e-4f) wd.Normalize();
                Vector2 tail = c - wd * face * 0.98f, head = tail + wd * face * (0.25f + 0.25f * WindField.Strength);
                Color wc = new Color(0.62f, 0.86f, 1f, 0.95f); float lw = Mathf.Max(1.5f, R * 0.04f);
                Line(tail, head, lw, wc);
                Vector2 n = new Vector2(-wd.y, wd.x);
                Line(head, head - wd * R * 0.12f + n * R * 0.08f, lw, wc); Line(head, head - wd * R * 0.12f - n * R * 0.08f, lw, wc);
            }
            if (rShip != null) Image(rShip, c, R * 0.3f, 0f, Color.white);
        }

        void Panel(Rect r, float u, bool lit = false, bool pressed = false)
        {
            LoadBanners(); if (tThin != null) { ThinPanel(r, lit, pressed); return; }
            if (HasKit) { KitPanel(r, lit, pressed); return; }
            Box(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), new Color(0f,0f,0f,0.25f));
            Box(r, lit ? new Color(0.16f,0.1f,0.055f,0.9f) : new Color(0.055f,0.062f,0.073f,0.86f));
            if (pressed) Box(r, new Color(0.7f,0.43f,0.14f,0.24f));
            float t = Mathf.Max(1f, u * 0.0025f);
            Color edge = lit ? gold : new Color(0.42f,0.37f,0.29f,0.9f);
            Box(new Rect(r.x,r.y,r.width,t),edge); Box(new Rect(r.x,r.yMax-t,r.width,t),edge);
            Box(new Rect(r.x,r.y,t,r.height),edge); Box(new Rect(r.xMax-t,r.y,t,r.height),edge);
            Box(new Rect(r.x+t,r.y+t,r.width-t*2f,t),new Color(1f,0.85f,0.6f,0.12f));
            float k = Mathf.Min(r.height * 0.18f, u * 0.012f);
            Box(new Rect(r.x+t*2,r.y+t*2,k,t),gold); Box(new Rect(r.xMax-k-t*2,r.y+t*2,k,t),gold);
            Box(new Rect(r.x+t*2,r.yMax-t*3,k,t),gold); Box(new Rect(r.xMax-k-t*2,r.yMax-t*3,k,t),gold);
        }
        void Ring(Vector2 center, float r, float width, Color color)
        {
            Tint(color);
            const int n = 64;
            for (int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2f/n,b=(i+1)*Mathf.PI*2f/n;
                Vector2 x=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),y=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Quad(center+x*r,center+y*r,center+y*(r-width),center+x*(r-width),Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
            }
        }
        void Disc(Vector2 p, float r, Color color)
        {
            Tint(color);
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI*2f/48,b=(i+1)*Mathf.PI*2f/48;
                Vertex(p,Vector2.zero); Vertex(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,Vector2.zero); Vertex(p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,Vector2.zero);
            }
        }
        void Text(Rect r,string text,float size,Color color)
        {
            if(r.width<=0f||string.IsNullOrEmpty(text)) return;
            labels.Add(new Label {rect=r,text=text,size=Mathf.Max(9,Mathf.RoundToInt(size)),color=color});
        }
        /// <summary>Кнопка борта: сверху во всю ширину поля — название; ниже — пушка (наружу) и состояние; внизу — полоса заряда.
        /// Всё внутри поля плашки, мимо рамки и угловых орнаментов.</summary>
        readonly bool[] volleyWasReady={true,true};
        readonly float[] volleyReadyAt={-10f,-10f};
        /// <summary>Кнопка борта из картинок владельца (1024×256): обычная, «в прицеле» (раскалённая), перезарядка (тёмная).
        /// Режется 5×3: углы и заклёпки не тянутся. Название — сверху по центру поля, состояние с пушкой — под ним,
        /// заряд — в канавке картинки.</summary>
        void VolleyArt(TouchControls input,HudLayout l,Rect r,int side,float readiness,float cooldown,bool aimed,bool ready,string idle)
        {
            Texture2D tex=!ready?tSideReload:aimed?tSideAim:tSide;
            float k=r.height/256f; bool pressed=input.Pressed(r);
            Color tint=pressed?new Color(0.85f,0.85f,0.85f,1f):Color.white;
            if(tex==tSideAim&&tSideGlow!=null)   // ореол раскалённой кнопки — позади, за краями рамки (рамка в нём: x 51…2036 из 2083, y 57…417 из 481)
                ImageRect(tSideGlow,new Rect(r.x-r.width*15f/858f,r.y-r.height*60f/288f,r.width*887f/858f,r.height*408f/288f),Whole,tint);   // ореол: рамка в нём x 15…873 из 887, y 60…348 из 408
            float TW=768f, cw=128f;   // исходная ширина (импорт NPOT меняет tex.width)   // атлас владельца: все три кнопки 768×256 одной формы
            float[] sx={0f,cw,TW*0.5f-32f,TW*0.5f+32f,TW-cw,TW}, sy={0f,60f,180f,256f};
            float[] dx={r.x,r.x+cw*k,0f,0f,r.xMax-cw*k,r.xMax}; float mid=(dx[1]+dx[4])*0.5f; dx[2]=mid-32f*k; dx[3]=mid+32f*k;
            float[] dy={r.y,r.y+60f*k,r.yMax-76f*k,r.yMax};
            for(int j=0;j<3;j++)for(int i=0;i<5;i++)
                ImageRect(tex,new Rect(dx[i],dy[j],dx[i+1]-dx[i],dy[j+1]-dy[j]),new Rect(sx[i]/TW,sy[j]/256f,(sx[i+1]-sx[i])/TW,(sy[j+1]-sy[j])/256f),tint);
            // Поле под текст: между угловыми орнаментами, от верхней кромки до канавки.
            float fx0=r.x+110f*k, fx1=r.xMax-110f*k, fy0=r.y+40f*k, fy1=r.yMax-(256f-188f)*k;
            float fh=fy1-fy0, fw=fx1-fx0;
            string tl=side<0?"ЛЕВЫЙ БОРТ":"ПРАВЫЙ БОРТ";
            string st=ready?(aimed?"В ПРИЦЕЛЕ":idle):Cached(side<0?10:11,Mathf.RoundToInt((1f-readiness)*cooldown*10f),v=>(v/10)+","+(v%10)+" с");
            Color sc=ready?(aimed?new Color(1f,0.93f,0.7f):gold):new Color(0.86f,0.82f,0.76f);
            // Один размер для обеих строк: заглавные — 26% высоты поля, но обе строки (вторая — с пушкой) влезают в поле
            // с запасом 6%; выравнивание — по центру поля, без ужатия отдельных строк (владелец 16.11).
            Rect src=side<0?KCannonL:KCannonR;
            // Раскладка зеркальная и неизменная: пушка — у края поля со стороны борта (левая — слева, правая — справа),
            // состояние — по центру оставшегося места. Размер шрифта — один на обе кнопки и все состояния, по самым
            // длинным строкам (владелец 16.11: разный размер, пушки на разном расстоянии, текст на рамке).
            float ar=src.width/src.height; bool cannon=false;   // пушки в строке состояния нет: с ней текст выходил мельче общего кегля (16.11)
            float wTitle=CapRatio("ПРАВЫЙ БОРТ"), wMax=Mathf.Max(CapRatio("ЖДЁМ ЦЕЛЬ"),Mathf.Max(CapRatio("В ПРИЦЕЛЕ"),CapRatio("ДАЛЕКО")));
            float capH=Mathf.Min(fh*0.27f,l.unit*0.03f*0.7f);   // не крупнее общего кегля плашек
            capH=Mathf.Min(capH,fw*0.92f/Mathf.Max(0.01f,wTitle));
            capH=Mathf.Min(capH,fw*0.92f/Mathf.Max(0.01f,wMax+(cannon?1.8f*ar+0.5f:0f)));
            float iconH=capH*1.8f, iconW=cannon?iconH*ar:0f, gap=cannon?capH*0.5f:0f;
            float y1=fy0+fh*0.27f, y2=fy0+fh*0.74f, cx=(fx0+fx1)*0.5f;
            CapText(cx,y1,tl,capH,ready?cream:new Color(0.78f,0.74f,0.68f,1f),true);
            float rx0=fx0+fw*0.04f, rx1=fx1-fw*0.04f;   // строка состояния — с тем же запасом 4% с краёв
            if(cannon)
            {
                Rect ir=side<0?new Rect(rx0,y2-iconH*0.55f,iconW,iconH):new Rect(rx1-iconW,y2-iconH*0.55f,iconW,iconH);
                Sprite(ir,src,new Color(1f,1f,1f,ready?1f:0.55f));
                if(side<0) rx0+=iconW+gap; else rx1-=iconW+gap;
            }
            CapText((rx0+rx1)*0.5f,y2,st,capH,sc,true);
            // Заряд — в канавке картинки (x 114…909, y 180…205); у раскалённой кнопки канавки нет — борт готов.
            if(tFill!=null)   // заряд в канавке; готов — полная
            {
                float f=ready?1f:Mathf.Clamp01(readiness);
                Rect gr=new Rect(r.x+101f*k,r.yMax-(256f-195f)*k,(r.xMax-103f*k)-(r.x+101f*k),15f*k);   // канавка атласа (замер): x 98…668 из 768, y 194…210
                if(f>0.001f) ImageRect(tFill,new Rect(gr.x,gr.y,gr.width*f,gr.height),new Rect(0f,0f,f,1f),Color.white);
            }
        }

        void Volley(TouchControls input,HudLayout l,Rect r,int side,float readiness,float cooldown,bool aimed)
        {
            // Почему борт не «в прицеле»: враг дальше предела пушек — «ДАЛЕКО», в пределах, но не на траверзе — «ЖДЁМ ЦЕЛЬ» (владелец 16.11: вместо «ДОВЕРНИ»).
            string idle="ГОТОВ"; Broadside vg=input.Guns();
            if(Application.isPlaying&&vg!=null&&vg.AimSide==side&&!aimed)
            {
                float nearest=float.MaxValue;
                foreach(TargetShip ts in TargetShip.All){if(ts==null||!ts.Alive)continue;float d=Vector3.Distance(ts.transform.position,input.ship.transform.position);if(d<nearest)nearest=d;}
                if(nearest<float.MaxValue)idle=nearest>vg.MaxRange?"ДАЛЕКО":"ЖДЁМ ЦЕЛЬ";
            }
            bool ready=readiness>=0.9999f;
            LoadBanners();
            if(tSide!=null&&tSideAim!=null&&tSideReload!=null){VolleyArt(input,l,r,side,readiness,cooldown,aimed,ready,idle);return;}
            Panel(r,l.unit,ready,input.Pressed(r));
            // Заливка перезарядки: кнопка наполняется янтарём слева направо, в момент готовности вспыхивает;
            // борт, чьи ядра по прицельной дуге ложатся в корпус врага, светится и пишет «В ПРИЦЕЛЕ».
            int vk=side<0?0:1; float vNow=Time.unscaledTime;
            if(ready&&!volleyWasReady[vk]&&Application.isPlaying)volleyReadyAt[vk]=vNow;
            volleyWasReady[vk]=ready;
            Rect body=Content(r); body=new Rect(body.x-r.height*0.12f,body.y,body.width+r.height*0.24f,body.height);   // только поле плашки: раньше заливка ложилась прямоугольником поверх рамки и орнаментов
            if(!ready)Solid(new Rect(body.x,body.y,body.width,body.height),new Color(0.02f,0.015f,0.01f,0.35f));   // перезарядка: кнопка приглушена, заряд — только в полосе снизу (сплошная заливка поверх кнопки выглядела убого, владелец 15.11)
            float pop=Mathf.Clamp01(1f-(vNow-volleyReadyAt[vk])/0.45f);
            if(ready&&pop>0f)Solid(body,new Color(1f,0.9f,0.6f,0.28f*pop));   // вспышка готовности слабее: на 0,55 выбеливала надпись
            if(aimed)   // «В ПРИЦЕЛЕ» — пульсирующий огненный кант по полю кнопки, без заливки (заливка выглядела плоской бежевой плашкой, владелец 16.11)
            {
                float ga=(ready?0.85f:0.45f)*(0.65f+0.35f*Mathf.Sin(vNow*6f)), gw=Mathf.Max(2f,r.height*0.035f);
                Color gc=new Color(1f,0.55f,0.18f,ga), gs=new Color(1f,0.55f,0.18f,ga*0.25f);
                Solid(new Rect(body.x,body.y,body.width,gw),gc); Solid(new Rect(body.x,body.yMax-gw,body.width,gw),gc);
                Solid(new Rect(body.x,body.y,gw,body.height),gc); Solid(new Rect(body.xMax-gw,body.y,gw,body.height),gc);
                Solid(new Rect(body.x+gw,body.y+gw,body.width-gw*2f,gw*1.6f),gs); Solid(new Rect(body.x+gw,body.yMax-gw*2.6f,body.width-gw*2f,gw*1.6f),gs);
            }
            Rect c=Content(r);
            Rect title=new Rect(c.x,c.y,c.width,c.height*0.36f);
            Text(title,side<0?"ЛЕВЫЙ БОРТ":"ПРАВЫЙ БОРТ",c.height*0.3f,cream);   // размер — от кнопки (кнопки стали меньше)
            Rect row=new Rect(c.x,c.y+c.height*0.38f,c.width,c.height*0.5f);
            if(HasKit)
            {
                Rect src=side<0?KCannonL:KCannonR; float ih=row.height, iw=ih*src.width/src.height;
                Rect ir=side<0?new Rect(row.x,row.y,iw,ih):new Rect(row.xMax-iw,row.y,iw,ih);
                Sprite(ir,src,new Color(1f,1f,1f,ready?1f:0.6f));
                float gapX=ih*0.15f;
                row=side<0?new Rect(row.x+iw+gapX,row.y,row.width-iw-gapX,row.height):new Rect(row.x,row.y,row.width-iw-gapX,row.height);
            }
            Text(row,ready?(aimed?"В ПРИЦЕЛЕ":idle):((1f-readiness)*cooldown).ToString("F1")+" с",c.height*0.3f,ready?(aimed?new Color(1f,0.72f,0.35f):gold):cream);   // секунды — светлым: на раскалённой заливке серые не читались
            float bh=Mathf.Max(3f,c.height*0.13f);   // полоса заряда толще: в ней теперь вся индикация перезарядки
            Rect bar=new Rect(c.x,c.yMax-bh,c.width,bh);
            if(HasKit)
            {
                Sprite(bar,KGroove,Color.white);
                float f=Mathf.Clamp01(readiness);
                if(f>0.001f)
                {
                    LoadBanners();
                    if(!ready&&tFill!=null){Rect fb=new Rect(bar.x+bar.height*0.35f,bar.y+bar.height*0.2f,(bar.width-bar.height*0.7f)*f,bar.height*0.6f);ImageRect(tFill,fb,new Rect(0f,0f,f,1f),Color.white);}   // раскалённый заряд в канавке полосы
                    else Sprite(new Rect(bar.x,bar.y,bar.width*f,bar.height),new Rect(KFill.x,KFill.y,KFill.width*f,KFill.height),Color.white);
                }
            }
            else
            {
                Box(bar,new Color(0.015f,0.02f,0.026f,1f));
                Box(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(readiness),bar.height),ready?gold:new Color(0.51f,0.56f,0.6f));
            }
        }

        // Строки HUD с числами собираются заново, только когда число изменилось: раньше каждая плашка каждый кадр
        // создавала новую строку — мусор для сборщика и рывки кадра.
        readonly Dictionary<int, KeyValuePair<int, string>> textCache = new Dictionary<int, KeyValuePair<int, string>>();
        string Cached(int slot, int value, System.Func<int, string> make)
        {
            KeyValuePair<int, string> e;
            if (textCache.TryGetValue(slot, out e) && e.Key == value) return e.Value;
            string s = make(value); textCache[slot] = new KeyValuePair<int, string>(value, s); return s;
        }
        static string Range(float dist) { return dist >= 1000f ? (dist / 1000f).ToString("F1") + " км" : Mathf.RoundToInt(dist / 10f) * 10 + " м"; }
        /// <summary>Под радаром — дальности двумя строками: ближайший враг и причал (у отметок подписи ложились на обод).</summary>
        static float GunRange(TouchControls input) { Broadside g = input.Guns(); return g != null ? g.MaxRange : 0f; }
        void RadarLines(TouchControls input, HudLayout l)
        {
            Rect r = l.radar, ri = l.radarInfo; float lh = l.unit * 0.036f, fs = l.unit * 0.027f;
            float best = float.MaxValue, bestHealth = 1f;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                Vector3 d = t.transform.position - input.ship.transform.position; d.y = 0f;
                if (d.magnitude < best) { best = d.magnitude; bestHealth = t.Health01; }
            }
            SkyFortress fort = SkyFortress.Instance;
            int rows = 1 + (best < float.MaxValue ? 1 : 0) + (fort != null ? 1 : 0);
            InfoPlate(ri, lh, Color.white);   // плашка во всю ширину левой колонки, всегда на три строки
            float y = ri.y + l.unit * 0.008f + (3 - rows) * lh * 0.5f;   // строки — по центру плашки
            Text(new Rect(ri.x, y, ri.width, lh), "РАДАР " + Range(input.RadarRange), fs, new Color(Mathf.Lerp(muted.r, 1f, 0.4f), Mathf.Lerp(muted.g, 1f, 0.4f), Mathf.Lerp(muted.b, 1f, 0.4f), 1f)); y += lh;
            if (best < float.MaxValue) { Text(new Rect(ri.x, y, ri.width, lh), "ВРАГ " + Range(best) + (bestHealth < 0.995f ? " · " + Mathf.RoundToInt(bestHealth * 100f) + "%" : ""), fs, best <= GunRange(input) ? new Color(1f, 0.72f, 0.55f, 1f) : new Color(0.72f, 0.74f, 0.78f, 1f)); y += lh; }   // серым — дальше, чем достают свои пушки
            if (fort != null)
            {
                Vector3 d = fort.BerthWorld - input.ship.transform.position; d.y = 0f;
                Text(new Rect(ri.x, y, ri.width, lh), "ПРИЧАЛ " + Range(d.magnitude), fs, fort.Hostile ? new Color(1f, 0.55f, 0.45f, 1f) : cream);
            }
        }

        static readonly string[] Cardinals = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
        /// <summary>Курс — компасная лента: румбы и деления едут под неподвижной меткой по центру плашки.</summary>
        void Compass(TouchControls input, HudLayout l)
        {
            LoadBanners();
            float heading = input.ship.transform.eulerAngles.y;
            Rect cr = l.course;
            if (tCompass == null) { Panel(cr, l.unit); return; }
            // Латунная лента (ui_compass.png 2131×107): тянется один период между большими рисками (x 297–568) — это 45°,
            // лента «едет» под неподвижной стрелкой; буквы румбов — над большими рисками.
            const float TW = 2131f, TH = 107f, P0 = 297f, P1 = 568f, CAP = 30f;
            float sh = l.unit * 0.042f, k = sh / TH;
            Rect strip = new Rect(cr.x, cr.yMax - sh, cr.width, sh);
            float capW = CAP * k, x0 = strip.x + capW, x1 = strip.xMax - capW;
            float periodPx = (P1 - P0) * k, degPx = 45f / periodPx;
            ImageRect(tCompass, new Rect(strip.x, strip.y, capW, sh), new Rect(0f, 0f, CAP / TW, 1f), Color.white);
            ImageRect(tCompass, new Rect(x1, strip.y, capW, sh), new Rect(1f - CAP / TW, 0f, CAP / TW, 1f), Color.white);
            float cx = strip.center.x;
            if (!(periodPx > 0.5f) || float.IsInfinity(degPx) || float.IsNaN(heading) || x1 <= x0) return;   // пока экран не размечен (unit = 0) — иначе цикл ниже уходил в миллиарды шагов и игра висела на сером экране
            int dFirst = Mathf.FloorToInt((heading - (cx - x0) * degPx) / 45f) - 1, dLast = Mathf.CeilToInt((heading + (x1 - cx) * degPx) / 45f) + 1;
            if (dLast - dFirst > 24) dLast = dFirst + 24;
            for (int n = dFirst; n <= dLast; n++)
            {
                float xs = cx + (n * 45f - heading) / degPx, xe = xs + periodPx;
                float a = Mathf.Max(xs, x0), b = Mathf.Min(xe, x1);
                if (b <= a) continue;
                float ua = (P0 + (a - xs) / k) / TW, ub = (P0 + (b - xs) / k) / TW;
                ImageRect(tCompass, new Rect(a, strip.y, b - a, sh), new Rect(ua, 0f, ub - ua, 1f), Color.white);
                // Буква над большой риской в начале периода; у краёв — гаснет.
                if (xs >= x0 && xs <= x1)
                {
                    int d = ((n % 8) + 8) % 8; float lw = l.unit * 0.07f;
                    float fade = Mathf.Clamp01(Mathf.Min(xs - x0, x1 - xs) / (l.unit * 0.03f));
                    Color lc = d % 2 == 0 ? cream : muted; lc.a = fade;
                    Text(new Rect(xs - lw * 0.5f, cr.y, lw, cr.height - sh), Cardinals[d], l.unit * 0.032f, lc);
                }
            }
            // Неподвижная стрелка курса — латунный треугольник остриём вниз над лентой.
            if (HasKit) { float aw = sh * 0.55f, ah = aw * KArrow.height / KArrow.width; SpriteRot(new Vector2(cx, strip.y + ah * 0.35f), new Vector2(aw, ah), Mathf.PI, KArrow, Color.white); }
        }

        /// <summary>Меню паузы: затемнение, панель по центру, четыре строки (геометрия — HudLayout.PauseRow).</summary>
        void PauseMenu(TouchControls input, HudLayout l, int width, int height)
        {
            Solid(new Rect(0f, 0f, width, height), new Color(0.02f, 0.03f, 0.05f, 0.6f));
            Rect p = l.PausePanel; LoadBanners();
            if (tPause != null) ImageRect(tPause, p, Whole, Color.white); else Solid(p, new Color(0.05f, 0.06f, 0.08f, 0.88f));
            CapText(p.center.x, p.y + p.height * 199f / 1265f, "ПАУЗА", l.unit * 0.034f, cream, true);   // по центру таблички шапки
            CapText(p.center.x, p.y + p.height * 1175.5f / 1265f, input.BuildText, l.unit * 0.014f, new Color(cream.r, cream.g, cream.b, 0.6f), true);   // номер сборки — в нижней плашке
            for (int i = 0; i < HudLayout.PauseRows; i++)
            {
                Rect r = l.PauseRow(i);
                string text = i == 0 ? "ПРОДОЛЖИТЬ" : i == 1 ? "ЗВУК · " + (input.SoundOn ? "ВКЛ" : "ВЫКЛ")
                    : i == 2 ? "КАМЕРА · " + TouchControls.CameraSpeedNames[input.CameraSpeed]
                    : i == 3 ? "КНОПКИ · " + (input.BigButtons ? "КРУПНЫЕ" : "ОБЫЧНЫЕ")
                    : i == 4 ? "ОБЛАКА · " + (input.game != null && input.game.volumetricClouds ? "ОБЪЁМНЫЕ" : "РИСОВАННЫЕ")
                    : i == 5 ? "ВИБРАЦИЯ · " + (Haptics.Enabled ? "ВКЛ" : "ВЫКЛ") : "В ГЛАВНОЕ МЕНЮ";
                Panel(r, l.unit, i == 0, input.Pressed(r));
                PlateText(r, 0f, 1f, text, l.unit * 0.036f, cream);
            }
        }

        /// <summary>Рамка-уголки вокруг взятой в цель врага (касанием по кораблю) с дальностью; красная — залп ложится в корпус.
        /// За краем экрана — стрелка у края в его сторону.</summary>
        int lockHints;
        float lockHintAt = -100f;
        /// <summary>Зелёное кольцо вокруг своих кораблей на экране — отличать от врагов с одного взгляда (владелец 16.11).</summary>
        void AllyRings(TouchControls input, HudLayout l)
        {
            Camera c = Camera.main; if (c == null || !Application.isPlaying) return;
            foreach (AllyShip a in AllyShip.All)
            {
                if (a == null || !a.Alive) continue;
                Vector3 sp = c.WorldToScreenPoint(a.Center); if (sp.z <= 0f) continue;
                float dist = Vector3.Distance(c.transform.position, a.Center); if (dist > 1600f) continue;
                Vector3 sp2 = c.WorldToScreenPoint(a.Center + c.transform.right * 24f * a.transform.lossyScale.x);
                float r = Mathf.Clamp(Mathf.Abs(sp2.x - sp.x), l.unit * 0.035f, l.unit * 0.32f);
                Vector2 q = new Vector2(sp.x, Screen.height - sp.y);
                float alpha = Mathf.Clamp01((1600f - dist) / 300f) * 0.85f;
                Color g = a.UnderAttack && Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.5f ? new Color(1f, 0.5f, 0.4f, alpha) : new Color(0.45f, 1f, 0.5f, alpha);   // под огнём — мигает красным
                Ring(q, r, Mathf.Max(2f, l.unit * 0.0035f), g);
            }
        }
        /// <summary>Плашки намерения над чужими кораблями ближе 600 м (политика отношений): «УХОДИ ИЗ НАШИХ ВОД · 20 С»,
        /// «ОТВЕДИ ПУШКИ», «БЕЖИТ», «СПУСТИЛ ФЛАГ»… — 4 с после смены отношения; у враждебного и настороже — постоянно короткая.</summary>
        void MoodPlates(TouchControls input, HudLayout l)
        {
            Camera c = Camera.main; if (c == null || !Application.isPlaying) return;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                bool fresh = Time.time < t.MoodTextUntil;
                if (!fresh && t.Mood == Attitude.Calm) continue;
                float dist = Vector3.Distance(input.ship.transform.position, t.transform.position);
                if (dist > 600f) continue;
                Vector3 top = t.BoxCorner(7); top.x = t.Center.x; top.z = t.Center.z;
                Vector3 sp = c.WorldToScreenPoint(top + Vector3.up * 4f); if (sp.z <= 0f) continue;
                string txt = fresh ? t.MoodText : Relations.Name(t.Mood);
                Color col = t.Mood == Attitude.Hostile ? new Color(1f, 0.45f, 0.35f, 1f) : t.Mood == Attitude.Wary ? new Color(1f, 0.85f, 0.4f, 1f)
                          : t.Mood == Attitude.Surrender ? new Color(0.85f, 0.85f, 0.85f, 1f) : new Color(1f, 0.92f, 0.8f, 1f);
                float a = Mathf.Clamp01((600f - dist) / 120f);
                col.a *= a;
                float capH = l.unit * 0.016f;
                CapText(sp.x, Screen.height - sp.y, txt, capH, col, true);
            }
        }
        void LockMarker(TouchControls input, HudLayout l)
        {
            Broadside gb = input.Guns(); Camera c = Camera.main;
            if (gb == null || c == null || !Application.isPlaying) return;
            TargetShip t = gb.Locked;
            if (t == null)
            {
                // Подсказка (до трёх раз за запуск): враг рядом, автобой выключен, цели нет.
                if (!input.AutoBattle && lockHints < 3 && Time.unscaledTime - lockHintAt > 25f)
                    foreach (TargetShip e in TargetShip.All)
                        if (e != null && e.Alive && Vector3.Distance(e.transform.position, input.ship.transform.position) < gb.MaxRange * 1.2f)
                        { HudNotice.Show("КОСНИСЬ ВРАГА — ВЗЯТЬ В ЦЕЛЬ", 1, 3f); lockHints++; lockHintAt = Time.unscaledTime; break; }
                return;
            }
            Vector3 sp = c.WorldToScreenPoint(t.Center);
            Color col = gb.AimOnTarget && gb.AimTargetShip == t ? new Color(1f, 0.45f, 0.3f, 0.95f) : new Color(1f, 0.82f, 0.45f, 0.95f);
            float dist = Vector3.Distance(t.transform.position, input.ship.transform.position);
            {
                // Цель за краем экрана или за спиной — стрелка у края в её сторону с дальностью.
                Vector2 mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), sq = new Vector2(sp.x, Screen.height - sp.y);
                if (sp.z <= 0f) sq = mid - (sq - mid);   // за камерой проекция зеркальная
                float ex = Screen.width * 0.5f - l.unit * 0.09f, ey = Screen.height * 0.5f - l.unit * 0.09f;
                Vector2 dv0 = sq - mid;
                if (sp.z <= 0f || Mathf.Abs(dv0.x) > Screen.width * 0.5f || Mathf.Abs(dv0.y) > Screen.height * 0.5f)
                {
                    if (dv0.sqrMagnitude < 1f) dv0 = Vector2.up;
                    if (sp.z <= 0f) dv0 = dv0.normalized * (Screen.width + Screen.height);
                    float kx = Mathf.Abs(dv0.x) > 1e-3f ? ex / Mathf.Abs(dv0.x) : float.MaxValue, ky = Mathf.Abs(dv0.y) > 1e-3f ? ey / Mathf.Abs(dv0.y) : float.MaxValue;
                    Vector2 at = mid + dv0 * Mathf.Min(kx, ky), n = dv0.normalized, s = new Vector2(-n.y, n.x);
                    float a = l.unit * 0.028f, w0 = Mathf.Max(2f, l.unit * 0.005f);
                    Line(at + n * a, at - n * a * 0.6f + s * a * 0.8f, w0, col); Line(at + n * a, at - n * a * 0.6f - s * a * 0.8f, w0, col);
                    Line(at - n * a * 0.6f + s * a * 0.8f, at - n * a * 0.15f, w0, col); Line(at - n * a * 0.6f - s * a * 0.8f, at - n * a * 0.15f, w0, col);
                    Vector2 tp = at - n * l.unit * 0.06f;
                    CapText(tp.x, tp.y, Cached(12, Mathf.RoundToInt(dist / 10f), v => v * 10 + " м"), l.unit * 0.018f, col, true);
                    return;
                }
            }
            if (sp.z > 0f)
            {
                // Рамка — по проекции корпуса (8 углов его коробки), а не кругом от центра: на снимках 155 уголки
                // съезжали с корабля и висели ниже корпуса.
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool any = false;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 cp = c.WorldToScreenPoint(t.BoxCorner(i)); if (cp.z <= 0f) continue; any = true;
                    float yy = Screen.height - cp.y; x0 = Mathf.Min(x0, cp.x); x1 = Mathf.Max(x1, cp.x); y0 = Mathf.Min(y0, yy); y1 = Mathf.Max(y1, yy);
                }
                if (!any) return;
                float pad = l.unit * 0.012f, minS = l.unit * 0.06f;
                Vector2 q = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
                float hw = Mathf.Max(minS, (x1 - x0) * 0.5f + pad), hh = Mathf.Max(minS * 0.7f, (y1 - y0) * 0.5f + pad);
                hw = Mathf.Min(hw, l.unit * 0.6f); hh = Mathf.Min(hh, l.unit * 0.4f);
                float arm = Mathf.Min(hw, hh) * 0.45f, w = Mathf.Max(2f, l.unit * 0.004f);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
                    Vector2 corner = q + new Vector2(sx * hw, sy * hh);
                    Line(corner, corner - new Vector2(sx * arm, 0f), w, col);
                    Line(corner, corner - new Vector2(0f, sy * arm), w, col);
                }
                CapText(q.x, q.y + hh + l.unit * 0.024f, Cached(12, Mathf.RoundToInt(dist / 10f), v => v * 10 + " м"), l.unit * 0.018f, col, true);
                {   // прочность цели — полоса над рамкой (как в Black Flag): тёмная подложка, красная заливка
                    float bw = Mathf.Clamp(hw * 1.3f, l.unit * 0.14f, l.unit * 0.3f), bh = Mathf.Max(4f, l.unit * 0.009f), by = q.y - hh - l.unit * 0.022f - bh, e = Mathf.Max(1f, l.unit * 0.002f);
                    Solid(new Rect(q.x - bw * 0.5f - e, by - e, bw + e * 2f, bh + e * 2f), new Color(0.79f, 0.61f, 0.35f, 0.9f));
                    Solid(new Rect(q.x - bw * 0.5f, by, bw, bh), new Color(0.05f, 0.04f, 0.04f, 0.95f));
                    float hp = Mathf.Clamp01(t.Health01);
                    if (hp > 0.001f) Solid(new Rect(q.x - bw * 0.5f, by, bw * hp, bh), hp < 0.3f ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(0.92f, 0.5f, 0.2f, 1f));
                }
                // Упреждение: куда цель уйдёт за время полёта ядра. Пунктир от рамки к кольцу и подпись — чтобы было
                // понятно, что это и зачем (владелец 15.11: «что значит этот значок»).
                if (gb.HasLead)
                {
                    Vector3 lp = c.WorldToScreenPoint(gb.LeadPoint);
                    if (lp.z > 0f)
                    {
                        Vector2 lq = new Vector2(lp.x, Screen.height - lp.y);
                        Color lc = new Color(1f, 0.82f, 0.45f, 0.9f);
                        float rr = l.unit * 0.022f, lw2 = Mathf.Max(1.5f, l.unit * 0.003f);
                        for (int k = 0; k < 20; k++) { float a0 = k * Mathf.PI / 10f, a1 = (k + 1) * Mathf.PI / 10f; Line(lq + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rr, lq + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rr, lw2, lc); }
                        Line(lq - new Vector2(rr * 0.4f, 0f), lq + new Vector2(rr * 0.4f, 0f), lw2, lc); Line(lq - new Vector2(0f, rr * 0.4f), lq + new Vector2(0f, rr * 0.4f), lw2, lc);
                        Vector2 dv = lq - q; float dl = dv.magnitude;
                        if (dl > rr * 3f)
                        {
                            Vector2 dn = dv / dl; Vector2 a = q + dn * Mathf.Min(dl * 0.5f, Mathf.Max(hw, hh) * 0.6f), b = lq - dn * rr * 1.3f;
                            float seg = l.unit * 0.012f, len = (b - a).magnitude;
                            for (float t0 = 0f; t0 < len; t0 += seg * 2f) Line(a + dn * t0, a + dn * Mathf.Min(len, t0 + seg), lw2, new Color(lc.r, lc.g, lc.b, 0.7f));
                        }
                        CapText(lq.x, lq.y - rr - l.unit * 0.018f, "УПРЕЖДЕНИЕ", l.unit * 0.016f, lc, true);
                    }
                }
            }
        }

        /// <summary>Машинный телеграф: циферблат с пятью секторами, рычаг плавно доворачивается на выбранную ступень.</summary>
        float leverAngle = float.NaN, leverScale; bool leverDeferred; Vector2 leverHub;
        void Telegraph(TouchControls input, HudLayout l)
        {
            LoadBanners();
            if (tConsole == null) return;
            ImageRect(tConsole, l.console, Whole, Color.white);   // пульт: гнездо штурвала + циферблат одной латунной конструкцией
            float want = HudLayout.GearAngle(input.Gear);
            leverAngle = float.IsNaN(leverAngle) ? want : Mathf.Lerp(leverAngle, want, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            // Рычаг: ось — шарнир у основания (94, 674), ручка смотрит на 74,8° от горизонтали; длина до ручки ≈ 0,32 ширины циферблата.
            float scale = l.console.width * 0.18f / 673f;   // ручка — на середине цветных секторов
            leverDeferred = true; leverScale = scale; leverHub = l.TelegraphHub;
        }

        /// <summary>Радар: курс корабля — вверх, цель — отметка с чёрточкой курса; за пределом дальности — на ободе.</summary>
        void Radar(TouchControls input,HudLayout l)
        {
            Rect r=l.radar; if(r.width<l.unit*0.08f||input.ship==null)return;
            float range=input.RadarRange;   // касание радара меняет масштаб: 1.5 км или 600 м
            Vector2 c=r.center; float R=r.width*0.5f, lw=Mathf.Max(1f,l.unit*0.0025f);
            LoadBanners();
            if(HasRadar && tModule!=null)
            {
                // Радар со вкладками одной латунной конструкцией: под вырезанным кругом — стекло и отметки, поверх — модуль.
                moduleFrame = true; RadarArt(input,l,c,R*1.8f,range); moduleFrame = false;
                ImageRect(tModule,l.radarModule,Whole,Color.white);
                ModuleTexts(input,l);
                return;
            }
            RadarLines(input,l);
            if(HasRadar) { RadarArt(input,l,c,R,range); return; }
            if(HasKit) { RadarKit(input,l,c,R,range); return; }
            Disc(c,R,new Color(0.03f,0.04f,0.05f,0.72f));
            Ring(c,R,lw*1.4f,gold);
            Ring(c,R*0.5f,lw,new Color(gold.r,gold.g,gold.b,0.35f));
            Tint(new Color(gold.r,gold.g,gold.b,0.25f));
            Quad(new Vector2(c.x-R,c.y-lw*0.5f),new Vector2(c.x+R,c.y-lw*0.5f),new Vector2(c.x+R,c.y+lw*0.5f),new Vector2(c.x-R,c.y+lw*0.5f),Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
            Quad(new Vector2(c.x-lw*0.5f,c.y-R),new Vector2(c.x+lw*0.5f,c.y-R),new Vector2(c.x+lw*0.5f,c.y+R),new Vector2(c.x-lw*0.5f,c.y+R),Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
            // Свой корабль — стрелка носом вверх
            float s=R*0.09f; Tint(cream);
            Vertex(c+new Vector2(0,-s*1.6f),Vector2.zero);Vertex(c+new Vector2(s,s),Vector2.zero);Vertex(c+new Vector2(-s,s),Vector2.zero);
            foreach(TargetShip t in TargetShip.All)
            {
                if(t==null||!t.Alive)continue;
                Vector3 local=input.ship.transform.InverseTransformPoint(t.transform.position);
                Vector2 v=new Vector2(local.x,-local.z);
                float dist=v.magnitude;
                bool outside=dist>range;
                Vector2 p=c+(dist>1e-3f?v/dist:Vector2.zero)*Mathf.Min(dist/range,1f)*(R-lw*3f);
                float pulse=0.75f+0.25f*Mathf.Sin(Time.realtimeSinceStartup*5f);
                Color red=new Color(1f,0.32f,0.18f,outside?0.7f:pulse);
                Disc(p,R*(outside?0.055f:0.07f),red);
                if(outside)Ring(p,R*0.085f,lw,red);
            }
        }

        // ---------- баннеры, оповещения, виньетка (Resources/UI: banner_victory/defeat 2048×512, plate_notice 1024×192,
        // vignette_damage 2:1, icons 4×4). Текст всегда в пределах внутреннего поля плашки (пиксели картинки ниже). ----------
        Texture2D bVictory, bDefeat, bPlate, bVignette, bIcons, bIcons2, tInfo, tFill, tCompass, tDial, tLever, tModule, tConsole, tWheel, tSide, tSideAim, tSideReload, tSideGlow, tThin, tThinLit, tPause, tSpy, tSpyLit; bool bannersTried;
        string victoryText = "ЦЕЛЬ УНИЧТОЖЕНА";
        readonly HashSet<TargetShip> sunkSeen = new HashSet<TargetShip>();
        float victoryAt = -100f;
        float cornerTop;   // верх свободного места в правом верхнем углу — для малых подсказок
        /// <summary>Кадры проверки в редакторе: 1 — баннер победы и оповещение, 2 — поражение с виньеткой.</summary>
        public static int CaptureBanner;
        void LoadBanners()
        {
            if (bannersTried) return; bannersTried = true;
            bVictory = Resources.Load<Texture2D>("UI/banner_victory"); bDefeat = Resources.Load<Texture2D>("UI/banner_defeat");
            bPlate = Resources.Load<Texture2D>("UI/plate_notice"); bVignette = Resources.Load<Texture2D>("UI/vignette_damage");
            bIcons = Resources.Load<Texture2D>("UI/icons"); bIcons2 = Resources.Load<Texture2D>("UI/icons2");
            tInfo = Resources.Load<Texture2D>("UI/plate_info"); tFill = Resources.Load<Texture2D>("UI/ui_fill"); tCompass = Resources.Load<Texture2D>("UI/ui_compass");
            tDial = Resources.Load<Texture2D>("UI/telegraph_dial"); tLever = Resources.Load<Texture2D>("UI/telegraph_lever");
            tModule = Resources.Load<Texture2D>("UI/ui_radar_module");
            tConsole = Resources.Load<Texture2D>("UI/ui_console"); tWheel = Resources.Load<Texture2D>("UI/ui_wheel");
            tSide = Resources.Load<Texture2D>("UI/btn_side"); tSideAim = Resources.Load<Texture2D>("UI/btn_side_aim"); tSideReload = Resources.Load<Texture2D>("UI/btn_side_reload"); tSideGlow = Resources.Load<Texture2D>("UI/btn_side_glow");
            tThin = Resources.Load<Texture2D>("UI/btn_thin"); tThinLit = Resources.Load<Texture2D>("UI/btn_thin_lit"); tPause = Resources.Load<Texture2D>("UI/ui_pause");
            tSpy = Resources.Load<Texture2D>("UI/btn_spy"); tSpyLit = Resources.Load<Texture2D>("UI/btn_spy_lit");
        }
        /// <summary>Кнопка подзорной трубы — квадратная из картинок владельца: btn_spy.png 704×668 (рамка во всю картинку),
        /// btn_spy_lit.png 826×870 — раскалённая, рамка в ней с (61;101), ореол вокруг. Горит, пока труба поднята.</summary>
        void SpyButton(TouchControls input, HudLayout l)
        {
            LoadBanners(); Rect r = l.spy; bool on = input.cam.ScopeOn;
            Color tint = input.Pressed(r) ? new Color(0.8f, 0.8f, 0.8f, 1f) : Color.white;
            float k = r.width / 704f;
            if (on && tSpyLit != null) ImageRect(tSpyLit, new Rect(r.x - 61f * k, r.y - 101f * k, 826f * k, 870f * k), Whole, tint);
            else if (tSpy != null) ImageRect(tSpy, r, Whole, tint);
            else Panel(r, l.unit, on, input.Pressed(r));
            float ic = r.height * 0.6f;   // значок трубы в кожаном поле (поле — внутри рамки с орнаментом)
            Icon2(0, new Rect(r.center.x - ic * 0.5f, r.center.y - ic * 0.5f, ic, ic), Color.white);
        }
        /// <summary>Тонкая кнопка из картинок владельца (btn_thin.png 1074×181; раскалённая btn_thin_lit.png 1160×307 — та же рамка
        /// со свечением: поля 43 по бокам, 64 сверху). Режется на три части: концы с завитками не тянутся; у короткой кнопки
        /// (квадратная «пауза») концы сходятся вплотную.</summary>
        void ThinPanel(Rect r, bool lit, bool pressed, float alpha = 1f)
        {
            bool glow = lit && tThinLit != null; Texture2D tex = glow ? tThinLit : tThin;
            // Размеры — исходные, а не tex.width/height: Unity при импорте приводит картинку к степени двойки
            // (1074×181 → 1024×128), и рамка рисовалась на 71% высоты кнопки — текст и значки съезжали на нижний кант.
            float k = r.height / 181f, ox = glow ? 43f : 0f, oy = glow ? 64f : 0f, W = glow ? 1160f : 1074f, TH = glow ? 307f : 181f;
            float cap = Mathf.Min(100f, r.width / k * 0.5f), sc = ox + cap;
            Color tint = pressed ? new Color(0.8f, 0.8f, 0.8f, alpha) : new Color(1f, 1f, 1f, alpha);
            Rect d = new Rect(r.x - ox * k, r.y - oy * k, r.width + ox * k * 2f, r.height * TH / 181f);
            ImageRect(tex, new Rect(d.x, d.y, sc * k, d.height), new Rect(0f, 0f, sc / W, 1f), tint);
            if (d.width - sc * k * 2f > 0.5f) ImageRect(tex, new Rect(d.x + sc * k, d.y, d.width - sc * k * 2f, d.height), new Rect(sc / W, 0f, 1f - sc * 2f / W, 1f), tint);
            ImageRect(tex, new Rect(d.xMax - sc * k, d.y, sc * k, d.height), new Rect(1f - sc / W, 0f, sc / W, 1f), tint);
        }
        /// <summary>Картинка в прямоугольник; uv — доли картинки от верхнего левого угла.</summary>
        void ImageRect(Texture2D tex, Rect dst, Rect uv, Color col)
        {
            if (tex == null) return;
            sprites.Add(new Spr { a = new Vector2(dst.x, dst.y), b = new Vector2(dst.xMax, dst.y), c = new Vector2(dst.xMax, dst.yMax), d = new Vector2(dst.x, dst.yMax),
                ua = new Vector2(uv.x, 1f - uv.y), ub = new Vector2(uv.xMax, 1f - uv.y), uc = new Vector2(uv.xMax, 1f - uv.yMax), ud = new Vector2(uv.x, 1f - uv.yMax),
                col = col, tex = tex });
        }
        static readonly Rect Whole = new Rect(0f, 0f, 1f, 1f);
        /// <summary>Сплошной прямоугольник в слое спрайтов (поверх плашек; Box рисуется под ними).</summary>
        void Solid(Rect dst, Color col) { ImageRect(Texture2D.whiteTexture, dst, Whole, col); }
        /// <summary>Отрезок толщиной w в слое спрайтов.</summary>
        void Line(Vector2 a, Vector2 b, float w, Color col)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 1e-3f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (w * 0.5f);
            sprites.Add(new Spr { a = a - n, b = b - n, c = b + n, d = a + n, ua = Vector2.zero, ub = Vector2.right, uc = Vector2.one, ud = Vector2.up, col = col, tex = Texture2D.whiteTexture });
        }
        /// <summary>Иконка из листа 4×4: 0 корпус, 1 вражеский огонь, 2 враг, 3 цель уничтожена, 4 перезарядка, 5 ветер, 6 ход, 7 компас,
        /// 8 настройки, 9 ангар, 10 пауза, 11 назад, 12 звук, 13 без звука, 14 вибрация, 15 помощь.</summary>
        void Icon(int index, Rect dst, Color col)
        {
            const float inset = 0.006f;
            float u = (index % 4) * 0.25f, v = (index / 4) * 0.25f;
            ImageRect(bIcons, dst, new Rect(u + inset, v + inset, 0.25f - inset * 2f, 0.25f - inset * 2f), col);
        }
        /// <summary>Второй лист значков (icons2.png, 4×4): 0 труба, 1 книппели, 2 ядро, 3 волной, 4 союзник, 5 торговец.</summary>
        void Icon2(int index, Rect dst, Color col)
        {
            LoadBanners(); if (bIcons2 == null) return;
            const float inset = 0.006f;
            float u = (index % 4) * 0.25f, v = (index / 4) * 0.25f;
            ImageRect(bIcons2, dst, new Rect(u + inset, v + inset, 0.25f - inset * 2f, 0.25f - inset * 2f), col);
        }
        /// <summary>Информационная плашка (plate_info.png 1536×91): тонкая — тремя частями с заклёпками по краям;
        /// высокая (несколько строк) — девятью, края берутся из полос без заклёпок, чтобы те не вытягивались.</summary>
        void InfoPlate(Rect r, float rowH, Color col)
        {
            LoadBanners();
            if (tInfo == null) { Solid(r, new Color(0.02f, 0.025f, 0.035f, 0.82f * col.a)); return; }
            const float W = 1536f, H = 91f, capPx = 64f;
            float k = rowH / H, cw = capPx * k, cu = capPx / W;
            if (r.height <= rowH * 1.08f)
            {
                ImageRect(tInfo, new Rect(r.x, r.y, cw, r.height), new Rect(0f, 0f, cu, 1f), col);
                ImageRect(tInfo, new Rect(r.x + cw, r.y, r.width - cw * 2f, r.height), new Rect(cu, 0f, 1f - cu * 2f, 1f), col);
                ImageRect(tInfo, new Rect(r.xMax - cw, r.y, cw, r.height), new Rect(1f - cu, 0f, cu, 1f), col);
                return;
            }
            float[] vy = { 0f, 0.3f, 0.36f, 1f }, uy = { 0f, 0.3f, 0.3f, 0.36f };   // верх 0–0,3; середина — полоса 0,3–0,36 без заклёпки; низ 0,64–1
            float th = 0.3f * H * k;
            float[] dy = { r.y, r.y + th, r.yMax - th, r.yMax };
            float[] sy0 = { 0f, 0.3f, 0.7f }, sy1 = { 0.3f, 0.36f, 1f };
            float[] dx = { r.x, r.x + cw, r.xMax - cw, r.xMax };
            float[] sx0 = { 0f, cu, 1f - cu }, sx1 = { cu, 1f - cu, 1f };
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
                ImageRect(tInfo, new Rect(dx[i], dy[j], dx[i + 1] - dx[i], dy[j + 1] - dy[j]), new Rect(sx0[i], sy0[j], sx1[i] - sx0[i], sy1[j] - sy0[j]), col);
        }
        /// <summary>Картинка, повёрнутая вокруг своей точки pivot (пиксели картинки от верхнего левого угла).
        /// angle — против часовой (в градусах) относительно исходного положения; scale — экранных пикселей на пиксель картинки.</summary>
        void ImagePivot(Texture2D tex, Vector2 at, Vector2 pivot, float scale, float angleDeg, Color col)
        {
            if (tex == null) return;
            float a = angleDeg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            System.Func<float, float, Vector2> T = (x, y) => { float px = (x - pivot.x) * scale, py = (y - pivot.y) * scale; return at + new Vector2(px * cs + py * sn, -px * sn + py * cs); };
            float w = tex.width, h = tex.height;
            sprites.Add(new Spr { a = T(0, 0), b = T(w, 0), c = T(w, h), d = T(0, h),
                ua = new Vector2(0, 1), ub = new Vector2(1, 1), uc = new Vector2(1, 0), ud = new Vector2(0, 0), col = col, tex = tex });
        }
        /// <summary>Поле внутри картинки (пиксели src от верхнего левого угла) — в экранный прямоугольник dst.</summary>
        static Rect Inner(Rect dst, float srcW, float srcH, float x0, float y0, float x1, float y1)
        {
            return Rect.MinMaxRect(dst.x + dst.width * x0 / srcW, dst.y + dst.height * y0 / srcH, dst.x + dst.width * x1 / srcW, dst.y + dst.height * y1 / srcH);
        }
        static float Envelope(float age, float life, float fadeIn, float fadeOut)
        {
            if (age < 0f || age > life) return 0f;
            return Mathf.Clamp01(age / fadeIn) * Mathf.Clamp01((life - age) / fadeOut);
        }

        /// <summary>Красная виньетка по краям экрана при попадании в свой корпус; пульсирует при малой прочности.</summary>
        void DamageVignette(int width, int height)
        {
            LoadBanners();
            PlayerHull hull = PlayerHull.Instance;
            if (bVignette != null && !Application.isPlaying && CaptureBanner == 2) { ImageRect(bVignette, new Rect(0f, 0f, width, height), Whole, new Color(1f, 1f, 1f, 0.55f)); return; }
            if (bVignette == null || hull == null) return;
            float hit = Mathf.Clamp01(1f - (Time.time - hull.LastHitTime) / 0.9f);
            float low = hull.Alive && hull.Health01 < 0.3f ? (0.18f + 0.1f * Mathf.Sin(Time.time * 4f)) * (1f - hull.Health01 / 0.3f) : 0f;
            float a = Mathf.Clamp01(Mathf.Max(hit * hit * 0.9f, low));
            if (!hull.Alive) a = 0.55f;
            if (a < 0.01f) return;
            ImageRect(bVignette, new Rect(0f, 0f, width, height), Whole, new Color(1f, 1f, 1f, a));
            // Видно, откуда прилетело: половина кромки со стороны попадания ярче.
            Camera view = Camera.main;
            if (hull.Alive && hit > 0.02f && view != null && hull.LastHitDir != Vector3.zero)
            {
                float sideways = Vector3.Dot(hull.LastHitDir, view.transform.right), extra = Mathf.Clamp01(hit * 0.9f);
                if (sideways < -0.25f) ImageRect(bVignette, new Rect(0f, 0f, width * 0.5f, height), new Rect(0f, 0f, 0.5f, 1f), new Color(1f, 1f, 1f, extra));
                else if (sideways > 0.25f) ImageRect(bVignette, new Rect(width * 0.5f, 0f, width * 0.5f, height), new Rect(0.5f, 0f, 0.5f, 1f), new Color(1f, 1f, 1f, extra));
            }
        }

        /// <summary>Баннер победы при гибели цели, баннер поражения при гибели своего корабля, плашка «враг даёт залп».</summary>
        void Banners(HudLayout l)
        {
            LoadBanners();
            bool capV = !Application.isPlaying && CaptureBanner == 1, capD = !Application.isPlaying && CaptureBanner == 2;
            if (Application.isPlaying) foreach (TargetShip t in TargetShip.All)
            {
                if (t == null) continue;
                if (!t.Alive && sunkSeen.Add(t)) { victoryAt = Time.time; victoryText = t.Captured ? "ПРИЗ ВЗЯТ" : "ЦЕЛЬ УНИЧТОЖЕНА"; }
                else if (t.Alive) sunkSeen.Remove(t);
            }
            PlayerHull hull = PlayerHull.Instance;
            float bw = Mathf.Min(l.safe.width * 0.5f, l.safe.height * 2.1f), bh = bw * 0.25f;
            float vic = capV ? 1f : Envelope(Time.time - victoryAt, 2.2f, 0.2f, 0.5f);   // было 3,6 с по центру — закрывало корабль
            bool dead = capD || (hull != null && !hull.Alive);
            float deadFor = capD ? 1f : hull != null ? hull.DeadFor : 0f, delay = hull != null ? hull.respawnDelay : 4f;
            if (dead)
            {
                float a = Mathf.Clamp01(deadFor / 0.35f);
                float pop = 1f + 0.06f * (1f - a);
                Rect r = new Rect(l.safe.center.x - bw * pop * 0.5f, l.safe.y + l.safe.height * 0.5f - bh * pop * 0.5f, bw * pop, bh * pop);
                if (bDefeat != null)
                {
                    ImageRect(bDefeat, r, Whole, new Color(1f, 1f, 1f, a));
                    Text(Inner(r, 2048f, 512f, 530f, 185f, 1518f, 325f), "КОРАБЛЬ ПОДБИТ", bh * 0.42f, new Color(1f, 0.62f, 0.48f, a));
                }
                else Text(new Rect(l.safe.x, l.safe.center.y - l.unit * 0.06f, l.safe.width, l.unit * 0.12f), "КОРАБЛЬ ПОДБИТ", l.unit * 0.06f, new Color(1f, 0.5f, 0.35f, a));
            }
            else if (vic > 0f)
            {
                float pop = capV ? 1f : 1f + 0.06f * (1f - Mathf.Clamp01((Time.time - victoryAt) / 0.25f));
                float vw = bw * 0.62f, vh = bh * 0.62f;   // меньше и выше корабля: под полосой корпуса, а не в центре экрана
                Rect r = new Rect(l.safe.center.x - vw * pop * 0.5f, l.safe.y + l.safe.height * 0.235f, vw * pop, vh * pop);
                bh = vh;
                if (bVictory != null)
                {
                    ImageRect(bVictory, r, Whole, new Color(1f, 1f, 1f, vic));
                    // Надпись строго по центру поля, с запасом от угловых орнаментов (они заходят внутрь до ~x 500 из 2048).
                    Text(Inner(r, 2048f, 512f, 530f, 180f, 1518f, 310f), victoryText, bh * 0.42f, new Color(1f, 0.86f, 0.55f, vic));
                }
                else Text(new Rect(l.safe.x, l.safe.center.y - l.unit * 0.06f, l.safe.width, l.unit * 0.12f), victoryText, l.unit * 0.06f, new Color(1f, 0.86f, 0.55f, vic));
            }
            // Плашка оповещения сверху по центру, ниже полосы корпуса: «враг даёт залп», а при гибели — отсчёт возврата.
            float note = dead ? Mathf.Clamp01(deadFor / 0.35f) : capV ? 1f : Envelope(Time.time - EnemyGunner.LastVolleyTime, 2.4f, 0.15f, 0.5f);
            string noteText = "ВРАГ ДАЁТ ЗАЛП"; int noteIcon = 1;
            // Малой плашкой в правом верхнем углу — «враг даёт залп» и постоянные подсказки (до причала, ремонт);
            // по центру — только тревога крепости, вход в зону и отсчёт возврата.
            bool corner = !dead;
            if (dead) { noteText = "ВОЗВРАТ ЧЕРЕЗ " + Mathf.CeilToInt(Mathf.Max(0f, delay - deadFor)) + " с"; noteIcon = 11; }
            else
            {
                // Край района боя: предупреждение, пока корабль в 250 м от границы (там же руль мягко отводит его обратно).
                ShipController own = hull != null ? hull.GetComponent<ShipController>() : null;
                // События боя (руль, баллон, порт, сторожевой) и итог залпа — малой плашкой, если она свободна.
                if (note < 0.05f && HudNotice.Alpha > 0f) { noteText = HudNotice.Text; noteIcon = HudNotice.Icon; note = HudNotice.Alpha; }
                else if (note < 0.05f && Application.isPlaying && TargetShip.Tally >= 2 && Time.time - TargetShip.TallyAt > 0.5f && Time.time - TargetShip.TallyAt < 3f)
                { noteText = "ПОПАДАНИЙ · " + TargetShip.Tally; noteIcon = 0; note = Mathf.Clamp01(3f - (Time.time - TargetShip.TallyAt)); }
                // Корпус ниже 35% и не у крепости — напоминание, где чинятся.
                if (hull != null && hull.Health01 < 0.35f && note < 0.05f && Application.isPlaying) { noteText = "КОРПУС " + Mathf.RoundToInt(hull.Health01 * 100f) + "% · К ПРИЧАЛУ"; noteIcon = 9; note = 0.9f; }
                if (own != null && own.EdgeWarning > 0.05f && note < 0.05f) { noteText = "КРАЙ РАЙОНА БОЯ"; noteIcon = 1; note = Mathf.Clamp01(own.EdgeWarning * 3f); }
                // Крепость: тревога важнее всего, затем ремонт у причала, затем вход в безопасную зону.
                SkyFortress fort = SkyFortress.Instance;
                if (fort != null && Application.isPlaying)
                {
                    float alarm = Envelope(Time.time - SkyFortress.FortressAlarmAt, 3.5f, 0.15f, 0.5f);
                    float zone = Envelope(Time.time - fort.EnteredAt, 3f, 0.2f, 0.6f);
                    float foeAlarm = Envelope(Time.time - SkyFortress.EnemyAlarmAt, 3.5f, 0.15f, 0.5f);
                    if (alarm > 0f) { noteText = "КРЕПОСТЬ ОТКРЫЛА ОГОНЬ"; noteIcon = 1; note = alarm; corner = false; }
                    else if (foeAlarm > 0f) { noteText = "КРЕПОСТЬ БЬЁТ ПО ВРАГУ"; noteIcon = 0; note = foeAlarm; corner = true; }   // в угол: по центру висела десятки секунд поверх корабля
                    else if (fort.Hostile) { noteText = "ТРЕВОГА · " + Mathf.CeilToInt(fort.HostileLeft) + " с"; noteIcon = 1; note = 1f; corner = true; }
                    else if (fort.PlayerDocked && hull != null) { noteText = "РЕМОНТ · " + Mathf.RoundToInt(hull.Health01 * 100f) + "%"; noteIcon = 0; note = 1f; corner = true; }
                    else if (zone > 0f && fort.PlayerInside) { noteText = "БЕЗОПАСНАЯ ЗОНА"; noteIcon = 0; note = zone; corner = false; }
                    else if (fort.PlayerInside && !fort.Hostile && note <= 0f && hull != null)
                    {
                        // В зоне — постоянная подсказка: сколько до причала, а у причала на ходу — сбавить ход.
                        // Положение корабля — по PlayerHull (он висит на корабле игрока): в Banners нет ссылки на ввод.
                        Vector3 toBerth = fort.BerthWorld - hull.transform.position; toBerth.y = 0f;
                        float bd = toBerth.magnitude;
                        noteText = bd < fort.berthRadius ? "СБАВЬ ХОД" : "ПРИЧАЛ · " + Mathf.RoundToInt(bd / 10f) * 10 + " м";
                        noteIcon = 9; note = 0.85f; corner = true;
                    }
                }
            }
            if (vic > 0f && !dead) corner = true;   // баннер победы теперь в слоте центральной плашки
            if (note > 0f && bPlate != null)
            {
                // Плашка plate_notice.png 1536×264: слева гнездо значка (кольцо, центр x 205), справа — завиток; середина тянется.
                const float PW = 1536f, PH = 264f, LCAP = 330f, RCAP = 160f, RING = 205f;
                float pw = Mathf.Min(l.safe.width * 0.32f, l.safe.height * 1.3f), ph = pw * PH / PW;
                float fsz = ph * 0.3f;
                float need = noteText.Length * fsz * 0.64f + ph * (LCAP + RCAP) / PH + ph * 0.25f;   // текст + гнездо + завиток + поля
                if (corner)
                {
                    ph = Mathf.Min(l.safe.width * 0.2f, l.safe.height * 0.8f) * 1.15f * PH / PW; fsz = ph * 0.3f;
                    need = noteText.Length * fsz * 0.64f + ph * (LCAP + RCAP) / PH + ph * 0.25f;
                    pw = Mathf.Min(Mathf.Max(ph * PW / PH * 0.8f, need), l.pause.x - l.course.xMax - l.unit * 0.04f);   // не заезжает на компас
                }
                else pw = Mathf.Min(Mathf.Max(pw, need), l.safe.width * 0.6f);
                Rect r = corner ? new Rect(l.pause.x - pw - l.unit * 0.015f, cornerTop, pw, ph)   // левее кнопки паузы
                                : new Rect(l.safe.center.x - pw * 0.5f, l.safe.y + l.safe.height * 0.235f, pw, ph);   // ниже подписи «КОРПУС»
                Color pc = new Color(1f, 1f, 1f, note);
                float lw = ph * LCAP / PH, rw = ph * RCAP / PH;
                ImageRect(bPlate, new Rect(r.x, r.y, lw, ph), new Rect(0f, 0f, LCAP / PW, 1f), pc);
                ImageRect(bPlate, new Rect(r.x + lw, r.y, r.width - lw - rw, ph), new Rect(LCAP / PW, 0f, 1f - (LCAP + RCAP) / PW, 1f), pc);
                ImageRect(bPlate, new Rect(r.xMax - rw, r.y, rw, ph), new Rect(1f - RCAP / PW, 0f, RCAP / PW, 1f), pc);
                float ic = ph * 0.6f, icx = r.x + ph * RING / PH;
                Icon(noteIcon, new Rect(icx - ic * 0.5f, r.center.y - ic * 0.5f, ic, ic), pc);
                Rect tr = new Rect(r.x + lw, r.y + ph * 0.2f, r.width - lw - rw, ph * 0.6f);
                { float capH = Mathf.Min(l.unit * 0.03f * 0.7f, tr.height * 0.6f, tr.width * 0.92f / Mathf.Max(0.01f, CapRatio(noteText)));
                  CapText(tr.center.x, tr.center.y, noteText, corner ? capH : Mathf.Min(capH * 1.25f, tr.width * 0.92f / Mathf.Max(0.01f, CapRatio(noteText))), dead ? new Color(cream.r, cream.g, cream.b, note) : new Color(1f, 0.72f, 0.5f, note), true); }
            }
        }

        /// <summary>Прочность корпуса — широкая полоса под плашкой курса. При попадании вздрагивает красным, светлый
        /// отрезок показывает только что снесённое и через 0.6 с сползает; ниже 30% полоса пульсирует.</summary>
        float hullGhost = 1f, hullGhostHold, hullSeen = 1f;
        void Hull(HudLayout l)
        {
            PlayerHull hull = PlayerHull.Instance;
            if (hull == null) return;
            float f = hull.Health01, now = Time.unscaledTime;
            if (f < hullSeen - 1e-4f) hullGhostHold = now + 0.6f;
            hullSeen = f;
            if (f >= hullGhost) hullGhost = f;
            else if (now > hullGhostHold) hullGhost = Mathf.MoveTowards(hullGhost, f, Time.unscaledDeltaTime * 0.6f);
            float flash = Mathf.Clamp01(1f - (Time.time - hull.LastHitTime) / 0.5f);
            float bw = l.unit * 0.45f;   // полоса корпуса прежней ширины (компас стал шире)
            Rect bar = new Rect(l.course.center.x - bw * 0.5f, l.course.yMax + l.unit * 0.012f, bw, l.unit * 0.04f);
            Color fill = Color.Lerp(new Color(1f, 0.35f, 0.2f), Color.white, Mathf.Clamp01((f - 0.25f) / 0.5f));
            fill = Color.Lerp(fill, new Color(1f, 0.15f, 0.08f), flash);
            if (f < 0.3f && hull.Alive) fill.a = 0.7f + 0.3f * Mathf.Sin(now * 7f);
            if (HasKit)
            {
                Sprite(bar, KGroove, Color.white);
                LoadBanners();
                if (f > 0.001f)
                {
                    // Заливка — внутри канавки (без белого торца, что торчал при малом корпусе)
                    Rect gr = new Rect(bar.x + bar.height * 0.55f, bar.y + bar.height * 0.3f, (bar.width - bar.height * 1.1f) * f, bar.height * 0.4f);
                    if (tFill != null) ImageRect(tFill, gr, new Rect(0f, 0f, Mathf.Max(f, 0.02f), 1f), new Color(Mathf.Lerp(1f, fill.r, 0.5f), Mathf.Lerp(1f, fill.g, 0.5f), Mathf.Lerp(1f, fill.b, 0.5f), fill.a));
                    else Sprite(new Rect(bar.x, bar.y, bar.width * f, bar.height), new Rect(KFill.x, KFill.y, KFill.width * f, KFill.height), fill);
                }
            }
            else
            {
                Box(bar, new Color(0.015f, 0.02f, 0.026f, 0.9f));
                Box(new Rect(bar.x, bar.y, bar.width * f, bar.height), fill * gold);
            }
            if (hullGhost > f + 0.002f)
            {
                float pad = bar.height * 0.22f;
                Solid(new Rect(bar.x + bar.width * f, bar.y + pad, bar.width * (hullGhost - f), bar.height - pad * 2f), new Color(1f, 0.95f, 0.85f, 0.9f));
            }
            // Подпись — на тёмной подложке (на светлых облаках терялась); ниже 30% подложка пульсирует красным.
            Rect cap = new Rect(bar.center.x - l.unit * 0.13f, bar.yMax + l.unit * 0.008f, l.unit * 0.26f, l.unit * 0.04f);   // с зазором: плашка наезжала на полосу корпуса   // шире: по краям заклёпки плашки
            float alarm = f < 0.3f && f > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            InfoPlate(cap, cap.height, Color.white);
            if (alarm > 0f) Solid(new Rect(cap.x + cap.height * 0.6f, cap.y + cap.height * 0.2f, cap.width - cap.height * 1.2f, cap.height * 0.6f), new Color(0.55f, 0.06f, 0.04f, 0.7f * alarm));   // ниже 30% — пульсирует красным
            Text(new Rect(cap.x + cap.height * 0.6f, cap.y, cap.width - cap.height * 1.2f, cap.height), Cached(1, Mathf.RoundToInt(f * 100f), v => "КОРПУС " + v + "%"), l.unit * 0.03f, f < 0.3f ? new Color(1f, 0.7f, 0.6f) : cream);
        }

        void RadarKit(TouchControls input,HudLayout l,Vector2 c,float R,float range)
        {
            Sprite(new Rect(c.x-R,c.y-R*KRadar.height/KRadar.width,R*2f,R*2f*KRadar.height/KRadar.width),KRadar,Color.white);
            float face=R*0.6f;
            float s=R*0.2f; Sprite(new Rect(c.x-s*0.45f,c.y-s*0.5f,s*0.9f,s),KArrow,Color.white);
            foreach(TargetShip t in TargetShip.All)
            {
                if(t==null||!t.Alive)continue;
                Vector3 local=input.ship.transform.InverseTransformPoint(t.transform.position);
                Vector2 v=new Vector2(local.x,-local.z); float dist=v.magnitude; bool outside=dist>range;
                Vector2 p=c+(dist>1e-3f?v/dist:Vector2.zero)*Mathf.Min(dist/range,1f)*face;
                float pulse=0.8f+0.2f*Mathf.Sin(Time.realtimeSinceStartup*5f);
                float g=R*(outside?0.13f:0.17f)*pulse;
                Sprite(new Rect(p.x-g*0.45f,p.y-g*0.5f,g*0.9f,g),KGem,new Color(1f,1f,1f,outside?0.65f:1f));
            }
        }

        public void Draw(TouchControls input,RenderTexture target,int width,int height,Shader shader,Font typeface)
        {
            if(shader==null||!shader.isSupported||typeface==null) throw new System.InvalidOperationException("HUD shader/font is missing");
            if(material==null) {material=new Material(shader);material.hideFlags=HideFlags.HideAndDontSave;}
            if(font!=typeface)ratioCache.Clear();   // сменили шрифт — ширины строк другие
            font=typeface;
            HudLayout l=input.Layout(width,height);
            labels.Clear(); MissingGlyphs=GlyphsDrawn=MissingCharacter=TextIssues=0; FirstTextIssue=null;
            minGlyph=l.unit*0.012f; screenRect=new Rect(0f,0f,width,height);
            RenderTexture previous=RenderTexture.active;
            bool srgb=GL.sRGBWrite;
            RenderTexture.active=target;
            GL.sRGBWrite=target!=null?target.sRGB:QualitySettings.activeColorSpace==ColorSpace.Linear;
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0f,width,height,0f);
                if(!material.SetPass(0))throw new System.InvalidOperationException("HUD panel pass failed");
                GL.Begin(GL.TRIANGLES);
                try
                {
                    LoadBanners();
                    if(input.Paused){PauseMenu(input,l,width,height);}   // номер сборки — внизу панели паузы   // «СБОРКА N» — только в меню паузы (владелец 16.11)
                    else
                    {
                    if(Application.isPlaying||CaptureBanner!=0)DamageVignette(width,height);   // под всеми плашками
                    LoadBanners(); Panel(l.pause,l.unit,false,input.Pressed(l.pause));
                    {float pi=l.pause.height*0.58f; Icon(10,new Rect(l.pause.center.x-pi*0.5f,l.pause.center.y-pi*0.5f,pi,pi),Color.white);}
                    // Плашки «облака» справа сверху нет — переключатель в панели FPS.
                    if(input.game.ships!=null&&input.game.ships.Length>1) {Panel(l.ship,l.unit);PlateText(l.ship,0f,1f,input.game.CurrentShipName,l.unit*0.03f,cream);}
                    cornerTop=l.safe.y+l.unit*0.02f;
                    Compass(input,l);
                    if(tModule==null){Panel(l.speed,l.unit);PlateText(l.speed,0f,1f,Cached(3,input.Gear*1000+Mathf.RoundToInt(Mathf.Abs(input.ship.Speed)),v=>TouchControls.GearNames[v/1000]+" · "+(v%1000)+" м/с"),l.unit*0.034f,cream);}   // ступень рукояти и скорость (с модулем радара — во вкладке хода)
                    Telegraph(input,l);
                    LockMarker(input,l);MoodPlates(input,l);
                    Radar(input,l);
                    Hull(l);
                    if(Application.isPlaying&&input.cam!=null)SpyButton(input,l);   // подзорная труба — квадратная кнопка под паузой
                    Panel(l.fps,l.unit);PlateText(l.fps,0f,1f,Application.isPlaying?Cached(2,Mathf.RoundToInt(input.Fps),v=>v+" FPS"):"FPS —",l.unit*0.03f,Application.isPlaying&&input.Fps<24f?new Color(1f,0.55f,0.45f,1f):muted);   // ниже 24 — красным: просадку видно сразу
                    Broadside guns=input.Guns();
                    if(guns!=null)
                    {
                        Volley(input,l,l.port,-1,guns.Readiness(-1),guns.cooldown,guns.AimOnTarget&&guns.AimSide<0);
                        Volley(input,l,l.starboard,1,guns.Readiness(1),guns.cooldown,guns.AimOnTarget&&guns.AimSide>0);
                        if(Application.isPlaying){string autoText=input.AutoBattle?(string.IsNullOrEmpty(input.AutoState)?"АВТОБОЙ · ВКЛ":input.AutoState):"АВТОБОЙ";Panel(l.auto,l.unit,input.AutoBattle,input.Pressed(l.auto));PlateText(l.auto,0f,1f,autoText,l.unit*0.03f,input.AutoBattle?new Color(1f,0.88f,0.55f):cream);}   // раньше соседей: свечение включённой кнопки ложится под них
                        Panel(l.mode,l.unit,false,input.Pressed(l.mode));
                        IconText(l.mode,3,guns.ripple?"ВОЛНОЙ":"ВЕСЬ БОРТ",l.unit*0.03f,cream);
                        Panel(l.ammo,l.unit,guns.chainShot,input.Pressed(l.ammo));
                        IconText(l.ammo,guns.chainShot?1:2,guns.chainShot?"КНИППЕЛИ":"ЯДРА",l.unit*0.03f,guns.chainShot?new Color(1f,0.88f,0.55f):cream);
                        // Абордаж: кнопка, пока можно; во время схватки — полоса хода абордажа.
                        PlayerBoarding pb=input.Boarding();
                        if(Application.isPlaying&&pb!=null&&(pb.Candidate!=null||pb.Active))
                        {
                            float pulse=pb.Active?1f:0.75f+0.25f*Mathf.Sin(Time.unscaledTime*7f);
                            Panel(l.board,l.unit,true,input.Pressed(l.board));
                            if(pb.Active)Solid(new Rect(l.board.x+l.board.height*0.12f,l.board.yMax-l.board.height*0.22f,(l.board.width-l.board.height*0.24f)*pb.Progress01,l.board.height*0.1f),gold);
                            PlateText(l.board,0f,1f,pb.Active?Cached(4,Mathf.RoundToInt(pb.Progress01*100f),v=>"АБОРДАЖ · "+v+"%"):"НА АБОРДАЖ",l.unit*0.03f,new Color(1f,0.88f,0.55f,pulse));
                        }
                    }
                    Vector2 home=tConsole!=null?l.wheel:(input.StickActive?l.ClampWheel(input.StickOrigin):l.wheel);   // с пультом штурвал стоит в своём гнезде
                    if(leverDeferred){leverDeferred=false;ImagePivot(tLever,leverHub,new Vector2(94f,674f),leverScale,leverAngle-74.8f,Color.white);}
                    float r=l.radius, a=input.StickActive?0.85f:0.45f;
                    if(HasKit)
                    {
                        // Штурвал поворачивается вместе с рулём, ручка — палец.
                        Vector2 off=input.StickOffset(l.radius);
                        float ang=Mathf.Clamp(off.x/Mathf.Max(1f,l.radius),-1f,1f)*1.2f;
                        float d=tConsole!=null?l.console.width*560f/1536f:r*2.35f;   // штурвал — по гнезду пульта
                        if(tWheel!=null) ImagePivot(tWheel,home,new Vector2(tWheel.width*0.5f,tWheel.height*0.5f),d/tWheel.width,-ang*Mathf.Rad2Deg,Color.white);
                        else SpriteRot(home,new Vector2(d,d*KWheel.height/KWheel.width),ang,KWheel,new Color(1f,1f,1f,input.StickActive?1f:0.7f));
                        float kd=r*0.66f; Sprite(new Rect(home.x+off.x-kd*0.5f,home.y+off.y-kd*0.5f,kd,kd*KKnob.height/KKnob.width),KKnob,new Color(1f,1f,1f,input.StickActive?1f:0.8f));
                    }
                    else
                    {
                    Disc(home,r*1.12f,new Color(0.04f,0.045f,0.055f,a*0.65f));
                    Ring(home,r*1.12f,Mathf.Max(1f,l.unit*0.003f),new Color(gold.r,gold.g,gold.b,a));
                    Ring(home,r*0.94f,Mathf.Max(1f,l.unit*0.002f),new Color(gold.r,gold.g,gold.b,a*0.55f));
                    for(int i=0;i<8;i++)
                    {
                        float t=i*Mathf.PI/4f;Vector2 v=new Vector2(Mathf.Cos(t),Mathf.Sin(t));
                        Vector2 side=new Vector2(-v.y,v.x)*Mathf.Max(1f,l.unit*0.002f);
                        Tint(new Color(gold.r,gold.g,gold.b,a*0.7f));
                        Quad(home+v*r*0.5f-side,home+v*r*0.92f-side,home+v*r*0.92f+side,home+v*r*0.5f+side,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
                    }
                    Vector2 knob=home+input.StickOffset(l.radius);
                    Disc(knob,r*0.31f,new Color(0.2f,0.17f,0.12f,a));Ring(knob,r*0.31f,Mathf.Max(1f,l.unit*0.004f),new Color(gold.r,gold.g,gold.b,a));
                    }
                    if(Application.isPlaying||CaptureBanner!=0)Banners(l);
                    {   // Возврат после гибели — через затемнение, а не мгновенным переносом.
                        PlayerHull fadeHull=PlayerHull.Instance; float blackout=fadeHull!=null&&Application.isPlaying?fadeHull.Blackout:0f;
                        if(blackout>0.004f)Solid(new Rect(0f,0f,width,height),new Color(0f,0f,0f,blackout));
                    }
                    if(input.HintAlpha>0f&&input.HintStage<4)
                    {
                        // Подсказки первого запуска — по шагам, на плашке (на светлых облаках голый текст не читается).
                        float ha=input.HintAlpha, pw=Mathf.Min(l.hint.width,l.unit*0.5f), ph=l.unit*0.095f;
                        Rect plate=new Rect(l.hint.center.x-pw*0.5f,l.hint.yMax-ph,pw,ph);
                        if(tThin!=null) ThinPanel(plate,false,false,ha);
                        else if(HasKit) {float hs=ph/PanelN[0].height; Slice9(plate,PanelN[0],30f,30f*hs,new Color(1f,1f,1f,ha));}
                        else Box(plate,new Color(0.055f,0.062f,0.073f,0.86f*ha));
                        Color hint=new Color(cream.r,cream.g,cream.b,ha), act=new Color(1f,0.84f,0.5f,ha);
                        string h1,h2;
                        if(input.HintStage==0){h1="ДЖОЙСТИК ВВЕРХ";h2="ДАТЬ ХОД";}
                        else if(input.HintStage==1){h1="ДЕРЖИ КНОПКУ БОРТА";h2="ОТПУСТИ — ЗАЛП";}
                        else if(input.HintStage==2){h1="ДВА КАСАНИЯ СПРАВА";h2="КАМЕРА ЗА КОРМУ";}
                        else{h1="СПРАВА — ОБЗОР";h2="2 ПАЛЬЦА — МАСШТАБ";}
                        PlateText(plate,0f,0.5f,h1,l.unit*0.026f,hint);
                        PlateText(plate,0.5f,0.5f,h2,l.unit*0.026f,input.HintStage<3?act:hint);
                    }
                    }
                }
                finally {GL.End();}
                FlushSprites();
                // Все размеры запрашиваются до чтения UV: перестройка атласа не портит уже собранные буквы.
                for(int i=0;i<labels.Count;i++) {font.RequestCharactersInTexture(labels[i].text,labels[i].size,FontStyle.Normal);font.RequestCharactersInTexture("Н",labels[i].size,FontStyle.Normal);}
                material.SetTexture("_MainTex",font.material.mainTexture);
                if(!material.SetPass(1))throw new System.InvalidOperationException("HUD text pass failed");
                GL.Begin(GL.TRIANGLES);
                try {for(int i=0;i<labels.Count;i++) DrawLabel(labels[i]);}
                finally {GL.End();}
                DrawCount++;
            }
            finally {GL.PopMatrix();GL.sRGBWrite=srgb;RenderTexture.active=previous;}
        }

        void DrawLabel(Label label)
        {
            float width=0f;int minY=int.MaxValue,maxY=int.MinValue;
            for(int i=0;i<label.text.Length;i++)
            {
                CharacterInfo c;
                if(!font.GetCharacterInfo(label.text[i],out c,label.size,FontStyle.Normal)) {MissingGlyphs++;MissingCharacter=label.text[i];continue;}
                width+=c.advance; if(label.text[i]!=' ') {minY=Mathf.Min(minY,c.minY);maxY=Mathf.Max(maxY,c.maxY);}
            }
            if(width<=0f||maxY<minY)return;
            // По вертикали — по высоте заглавной «Н», а не по рамке букв строки: иначе хвосты «Д», «Щ», «Ц» поднимали
            // всю надпись («ЯДРА» стояла выше «ВОЛНОЙ» на соседней кнопке).
            CharacterInfo cap;
            if(font.GetCharacterInfo('Н',out cap,label.size,FontStyle.Normal)&&cap.maxY>cap.minY){minY=cap.minY;maxY=cap.maxY;}
            // Текст никогда не выходит за свою плашку: поля по бокам под угловой орнамент, по высоте — 80% плашки.
            float side=Mathf.Max(6f,label.rect.height*0.3f);
            float scale=Mathf.Clamp01((label.rect.width-side*2f)/width);
            scale=Mathf.Min(scale,label.rect.height*0.8f/Mathf.Max(1f,maxY-minY));
            float glyphH=(maxY-minY)*scale, textW=width*scale;
            bool small=glyphH<minGlyph&&label.text.Length>1;
            bool off=label.rect.x<screenRect.x-1f||label.rect.y<screenRect.y-1f||label.rect.xMax>screenRect.xMax+1f||label.rect.yMax>screenRect.yMax+1f;
            if((small||off)&&label.color.a>0.05f)
            {
                TextIssues++;
                if(FirstTextIssue==null)FirstTextIssue="«"+label.text+"» "+(small?"мелко: высота букв "+glyphH.ToString("F1")+" px < "+minGlyph.ToString("F1"):"за краем экрана")+", поле "+label.rect.width.ToString("F0")+"x"+label.rect.height.ToString("F0")+", текст "+textW.ToString("F0");
            }
            float x=label.rect.center.x-width*scale*0.5f;
            float baseline=label.rect.center.y+(minY+maxY)*0.5f*scale;
            for(int i=0;i<label.text.Length;i++)
            {
                CharacterInfo c;if(!font.GetCharacterInfo(label.text[i],out c,label.size,FontStyle.Normal))continue;
                if(label.text[i]!=' ')
                {
                    Rect r=Rect.MinMaxRect(x+c.minX*scale,baseline-c.maxY*scale,x+c.maxX*scale,baseline-c.minY*scale);
                    Tint(new Color(0f,0f,0f,label.color.a*0.75f)); Glyph(r,c,1f);
                    Tint(label.color);Glyph(r,c,0f);GlyphsDrawn++;
                }
                x+=c.advance*scale;
            }
        }
        static void Glyph(Rect r,CharacterInfo c,float offset)
        {
            Quad(new Vector2(r.x+offset,r.y+offset),new Vector2(r.xMax+offset,r.y+offset),new Vector2(r.xMax+offset,r.yMax+offset),new Vector2(r.x+offset,r.yMax+offset),c.uvTopLeft,c.uvTopRight,c.uvBottomRight,c.uvBottomLeft);
        }
        public void Dispose() {if(material!=null)SkyTideBootstrap.SafeDestroy(material);material=null;}
    }
}
