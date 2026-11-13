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
        static readonly Rect KWheel = new Rect(24,503,422,442), KRadar = new Rect(447,498,407,437), KArrow = new Rect(872,606,72,81),
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
        Texture2D rFrame, rFace, rSweep, rShip, rEnemy, rFar; bool radarTried;
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
                }
                return rFrame != null && rFace != null;
            }
        }
        const float SweepPeriod = 3f;
        void RadarArt(TouchControls input, HudLayout l, Vector2 c, float R, float range)
        {
            float size = R * 2f * 1024f / 900f;           // рамка (с выступами) — 900 из 1024 пикселей по ширине
            float face = size * (250f / 1024f);            // внутренний край кольца рамки
            float t = Time.realtimeSinceStartup;
            float sweep = Mathf.Repeat(t / SweepPeriod, 1f) * Mathf.PI * 2f;
            const float edge0 = -60f * Mathf.Deg2Rad;     // передний край луча на картинке (вверх-вправо)
            Image(rFace, c, size, 0f, Color.white);
            if (rSweep != null) Image(rSweep, c, size, sweep, new Color(1f, 1f, 1f, 0.9f));
            foreach (TargetShip ts in TargetShip.All)
            {
                if (ts == null || !ts.Alive) continue;
                Vector3 local = input.ship.transform.InverseTransformPoint(ts.transform.position);
                Vector2 v = new Vector2(local.x, -local.z); float dist = v.magnitude; bool outside = dist > range;
                Vector2 dir = dist > 1e-3f ? v / dist : Vector2.up;
                Vector2 p = c + dir * Mathf.Min(dist / range, 1f) * face * (outside ? 0.93f : 0.88f);
                // Отметка вспыхивает, когда по ней проходит луч, и гаснет до следующего оборота.
                float beta = Mathf.Atan2(dir.y, dir.x);
                float since = Mathf.Repeat(edge0 + sweep - beta, Mathf.PI * 2f) / (Mathf.PI * 2f) * SweepPeriod;
                float glow = 0.55f + 0.45f * Mathf.Exp(-since * 1.6f);
                float g = R * (outside ? 0.26f : 0.24f) * (0.9f + 0.15f * Mathf.Exp(-since * 3f));
                if (outside && rFar != null) Image(rFar, p, g, beta + 45f * Mathf.Deg2Rad, new Color(1f, 1f, 1f, glow));
                else if (rEnemy != null) Image(rEnemy, p, g, 0f, new Color(1f, 1f, 1f, glow));
                string txt = dist >= 1000f ? (dist / 1000f).ToString("F1") + " км" : Mathf.RoundToInt(dist / 10f) * 10 + " м";
                float fs = l.unit * 0.022f, tw = l.unit * 0.16f; bool left = p.x > c.x + R * 0.2f;
                Text(new Rect(left ? p.x - tw - g * 0.45f : p.x + g * 0.45f, p.y - fs * 0.75f, tw, fs * 1.5f), txt, fs, new Color(1f, 0.8f, 0.62f, 1f));
            }
            Image(rFrame, c, size, 0f, Color.white);
            if (rShip != null) Image(rShip, c, R * 0.3f, 0f, Color.white);
        }

        void Panel(Rect r, float u, bool lit = false, bool pressed = false)
        {
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
        void Volley(TouchControls input,HudLayout l,Rect r,int side,float readiness,float cooldown)
        {
            bool ready=readiness>=0.9999f;
            Panel(r,l.unit,ready,input.Pressed(r));
            float icon=HasKit?r.height*0.62f:0f;
            if(HasKit)
            {
                Rect src=side<0?KCannonL:KCannonR; float iw=icon*src.width/src.height;
                Rect ir=side<0?new Rect(r.x+l.unit*0.014f,r.y+(r.height-icon)*0.45f,iw,icon):new Rect(r.xMax-l.unit*0.014f-iw,r.y+(r.height-icon)*0.45f,iw,icon);
                Sprite(ir,src,new Color(1f,1f,1f,ready?1f:0.6f));
                icon=iw+l.unit*0.01f;
                r=side<0?new Rect(r.x+icon,r.y,r.width-icon-l.unit*0.02f,r.height):new Rect(r.x+l.unit*0.02f,r.y,r.width-icon-l.unit*0.02f,r.height);
            }
            Rect title=new Rect(r.x,r.y+l.unit*0.012f,r.width,r.height*0.44f);
            Text(title,side<0?"ЛЕВЫЙ БОРТ":"ПРАВЫЙ БОРТ",l.unit*0.028f,cream);
            Rect state=new Rect(r.x,r.y+r.height*0.5f,r.width,r.height*0.24f);
            Text(state,ready?"ГОТОВ":"ПЕРЕЗАРЯДКА "+((1f-readiness)*cooldown).ToString("F1")+" с",l.unit*0.022f,ready?gold:muted);
            float inset=l.unit*0.022f, h=Mathf.Max(2f,l.unit*0.006f);
            Rect track=new Rect(r.x+inset,r.yMax-l.unit*0.023f,r.width-inset*2f,h);
            if(HasKit)
            {
                float bh=Mathf.Max(h*3f,l.unit*0.016f); Rect bar=new Rect(track.x,track.center.y-bh*0.5f,track.width,bh);
                Sprite(bar,KGroove,Color.white);
                float f=Mathf.Clamp01(readiness);
                if(f>0.001f) Sprite(new Rect(bar.x,bar.y,bar.width*f,bar.height),new Rect(KFill.x,KFill.y,KFill.width*f,KFill.height),ready?Color.white:new Color(0.75f,0.8f,0.85f,0.8f));
            }
            else
            {
                Box(track,new Color(0.015f,0.02f,0.026f,1f));
                Box(new Rect(track.x,track.y,track.width*Mathf.Clamp01(readiness),track.height),ready?gold:new Color(0.51f,0.56f,0.6f));
            }
        }

        /// <summary>Радар: курс корабля — вверх, цель — точка с расстоянием рядом; за пределом дальности — на ободе.</summary>
        void Radar(TouchControls input,HudLayout l)
        {
            Rect r=l.radar; if(r.width<l.unit*0.08f||input.ship==null)return;
            const float range=1500f;
            Vector2 c=r.center; float R=r.width*0.5f, lw=Mathf.Max(1f,l.unit*0.0025f);
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
                string txt=dist>=1000f?(dist/1000f).ToString("F1")+" км":Mathf.RoundToInt(dist/10f)*10+" м";
                float fs=l.unit*0.022f, tw=l.unit*0.16f;
                bool left=p.x>c.x+R*0.2f;
                Rect lr=new Rect(left?p.x-tw-R*0.1f:p.x+R*0.1f,p.y-fs*0.75f,tw,fs*1.5f);
                Text(lr,txt,fs,new Color(1f,0.72f,0.55f,1f));
            }
        }

        // ---------- баннеры, оповещения, виньетка (Resources/UI: banner_victory/defeat 2048×512, plate_notice 1024×192,
        // vignette_damage 2:1, icons 4×4). Текст всегда в пределах внутреннего поля плашки (пиксели картинки ниже). ----------
        Texture2D bVictory, bDefeat, bPlate, bVignette, bIcons; bool bannersTried;
        readonly HashSet<TargetShip> sunkSeen = new HashSet<TargetShip>();
        float victoryAt = -100f;
        /// <summary>Кадры проверки в редакторе: 1 — баннер победы и оповещение, 2 — поражение с виньеткой.</summary>
        public static int CaptureBanner;
        void LoadBanners()
        {
            if (bannersTried) return; bannersTried = true;
            bVictory = Resources.Load<Texture2D>("UI/banner_victory"); bDefeat = Resources.Load<Texture2D>("UI/banner_defeat");
            bPlate = Resources.Load<Texture2D>("UI/plate_notice"); bVignette = Resources.Load<Texture2D>("UI/vignette_damage");
            bIcons = Resources.Load<Texture2D>("UI/icons");
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
        /// <summary>Иконка из листа 4×4: 0 корпус, 1 вражеский огонь, 2 враг, 3 цель уничтожена, 4 перезарядка, 5 ветер, 6 ход, 7 компас,
        /// 8 настройки, 9 ангар, 10 пауза, 11 назад, 12 звук, 13 без звука, 14 вибрация, 15 помощь.</summary>
        void Icon(int index, Rect dst, Color col)
        {
            const float inset = 0.006f;
            float u = (index % 4) * 0.25f, v = (index / 4) * 0.25f;
            ImageRect(bIcons, dst, new Rect(u + inset, v + inset, 0.25f - inset * 2f, 0.25f - inset * 2f), col);
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
        }

        /// <summary>Баннер победы при гибели цели, баннер поражения при гибели своего корабля, плашка «враг даёт залп».</summary>
        void Banners(HudLayout l)
        {
            LoadBanners();
            bool capV = !Application.isPlaying && CaptureBanner == 1, capD = !Application.isPlaying && CaptureBanner == 2;
            if (Application.isPlaying) foreach (TargetShip t in TargetShip.All)
            {
                if (t == null) continue;
                if (!t.Alive && sunkSeen.Add(t)) victoryAt = Time.time;
                else if (t.Alive) sunkSeen.Remove(t);
            }
            PlayerHull hull = PlayerHull.Instance;
            float bw = Mathf.Min(l.safe.width * 0.62f, l.safe.height * 2.6f), bh = bw * 0.25f;
            float vic = capV ? 1f : Envelope(Time.time - victoryAt, 3.6f, 0.25f, 0.7f);
            bool dead = capD || (hull != null && !hull.Alive);
            float deadFor = capD ? 1f : hull != null ? hull.DeadFor : 0f, delay = hull != null ? hull.respawnDelay : 4f;
            if (dead)
            {
                float a = Mathf.Clamp01(deadFor / 0.35f);
                float pop = 1f + 0.06f * (1f - a);
                Rect r = new Rect(l.safe.center.x - bw * pop * 0.5f, l.safe.y + l.safe.height * 0.3f - bh * pop * 0.5f, bw * pop, bh * pop);
                if (bDefeat != null)
                {
                    ImageRect(bDefeat, r, Whole, new Color(1f, 1f, 1f, a));
                    Text(Inner(r, 2048f, 512f, 430f, 180f, 1618f, 330f), "КОРАБЛЬ ПОДБИТ", bh * 0.42f, new Color(1f, 0.62f, 0.48f, a));
                }
                else Text(new Rect(l.safe.x, l.safe.center.y - l.unit * 0.06f, l.safe.width, l.unit * 0.12f), "КОРАБЛЬ ПОДБИТ", l.unit * 0.06f, new Color(1f, 0.5f, 0.35f, a));
                int left = Mathf.CeilToInt(Mathf.Max(0f, delay - deadFor));
                Text(new Rect(r.x, r.yMax, r.width, bh * 0.3f), "ВОЗВРАТ ЧЕРЕЗ " + left + " с", bh * 0.2f, new Color(cream.r, cream.g, cream.b, a * 0.9f));
            }
            else if (vic > 0f)
            {
                float pop = capV ? 1f : 1f + 0.06f * (1f - Mathf.Clamp01((Time.time - victoryAt) / 0.25f));
                Rect r = new Rect(l.safe.center.x - bw * pop * 0.5f, l.safe.y + l.safe.height * 0.3f - bh * pop * 0.5f, bw * pop, bh * pop);
                if (bVictory != null)
                {
                    ImageRect(bVictory, r, Whole, new Color(1f, 1f, 1f, vic));
                    Rect inner = Inner(r, 2048f, 512f, 430f, 175f, 1618f, 312f);
                    float ic = inner.height * 0.95f;
                    Icon(3, new Rect(inner.x, inner.center.y - ic * 0.5f, ic, ic), new Color(1f, 1f, 1f, vic));
                    Text(new Rect(inner.x + ic * 1.1f, inner.y, inner.width - ic * 2.2f, inner.height), "ЦЕЛЬ УНИЧТОЖЕНА", bh * 0.42f, new Color(1f, 0.86f, 0.55f, vic));
                }
                else Text(new Rect(l.safe.x, l.safe.center.y - l.unit * 0.06f, l.safe.width, l.unit * 0.12f), "ЦЕЛЬ УНИЧТОЖЕНА", l.unit * 0.06f, new Color(1f, 0.86f, 0.55f, vic));
            }
            // Оповещение о вражеском залпе — плашка сверху по центру (не в бою на экране гибели).
            float note = dead ? 0f : capV ? 1f : Envelope(Time.time - EnemyGunner.LastVolleyTime, 2.4f, 0.15f, 0.5f);
            if (note > 0f && bPlate != null)
            {
                float pw = Mathf.Min(l.safe.width * 0.36f, l.safe.height * 1.5f), ph = pw * 192f / 1024f;
                Rect r = new Rect(l.safe.center.x - pw * 0.5f, l.safe.y + l.safe.height * 0.13f, pw, ph);
                ImageRect(bPlate, r, Whole, new Color(1f, 1f, 1f, note));
                Rect inner = Inner(r, 1024f, 192f, 150f, 48f, 874f, 144f);
                float ic = inner.height * 1.05f;
                Icon(1, new Rect(inner.x, inner.center.y - ic * 0.5f, ic, ic), new Color(1f, 1f, 1f, note));
                Text(new Rect(inner.x + ic * 1.1f, inner.y, inner.width - ic * 1.1f, inner.height), "ВРАГ ДАЁТ ЗАЛП", ph * 0.4f, new Color(1f, 0.72f, 0.5f, note));
            }
        }

        /// <summary>Прочность корпуса — под плашкой курса; при попадании плашка вздрагивает красным.</summary>
        void Hull(HudLayout l)
        {
            PlayerHull hull = PlayerHull.Instance;
            if (hull == null) return;
            float f = hull.Health01;
            float flash = Mathf.Clamp01(1f - (Time.time - hull.LastHitTime) / 0.5f);
            Rect bar = new Rect(l.course.x + l.unit * 0.02f, l.course.yMax + l.unit * 0.012f, l.course.width - l.unit * 0.04f, l.unit * 0.028f);
            Color fill = Color.Lerp(new Color(1f, 0.35f, 0.2f), Color.white, Mathf.Clamp01((f - 0.25f) / 0.5f));
            fill = Color.Lerp(fill, new Color(1f, 0.15f, 0.08f), flash);
            if (HasKit)
            {
                Sprite(bar, KGroove, Color.white);
                if (f > 0.001f) Sprite(new Rect(bar.x, bar.y, bar.width * f, bar.height), new Rect(KFill.x, KFill.y, KFill.width * f, KFill.height), fill);
            }
            else
            {
                Box(bar, new Color(0.015f, 0.02f, 0.026f, 0.9f));
                Box(new Rect(bar.x, bar.y, bar.width * f, bar.height), fill * gold);
            }
            Text(new Rect(bar.x, bar.yMax, bar.width, l.unit * 0.032f), "КОРПУС " + Mathf.RoundToInt(f * 100f) + "%", l.unit * 0.02f, f < 0.3f ? new Color(1f, 0.55f, 0.45f) : muted);
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
                string txt=dist>=1000f?(dist/1000f).ToString("F1")+" км":Mathf.RoundToInt(dist/10f)*10+" м";
                float fs=l.unit*0.022f, tw=l.unit*0.16f; bool left=p.x>c.x+R*0.2f;
                Text(new Rect(left?p.x-tw-g*0.5f:p.x+g*0.5f,p.y-fs*0.75f,tw,fs*1.5f),txt,fs,new Color(1f,0.78f,0.6f,1f));
            }
        }

        public void Draw(TouchControls input,RenderTexture target,int width,int height,Shader shader,Font typeface)
        {
            if(shader==null||!shader.isSupported||typeface==null) throw new System.InvalidOperationException("HUD shader/font is missing");
            if(material==null) {material=new Material(shader);material.hideFlags=HideFlags.HideAndDontSave;}
            font=typeface;
            HudLayout l=input.Layout(width,height);
            labels.Clear(); MissingGlyphs=GlyphsDrawn=MissingCharacter=0;
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
                    if(Application.isPlaying||CaptureBanner!=0)DamageVignette(width,height);   // под всеми плашками
                    Panel(l.clouds,l.unit,false,input.Pressed(l.clouds));
                    Text(new Rect(l.clouds.x,l.clouds.y,l.clouds.width,l.clouds.height*0.42f),"ОБЛАКА",l.unit*0.021f,muted);
                    Text(new Rect(l.clouds.x,l.clouds.y+l.clouds.height*0.36f,l.clouds.width,l.clouds.height*0.55f),input.game.volumetricClouds?"ОБЪЁМ":"РИСОВАННЫЕ",l.unit*0.031f,cream);
                    if(input.game.ships!=null&&input.game.ships.Length>1) {Panel(l.ship,l.unit);Text(l.ship,input.game.CurrentShipName,l.unit*0.025f,cream);}
                    Panel(l.course,l.unit); Text(l.course,"КУРС "+(Mathf.RoundToInt(input.ship.transform.eulerAngles.y)%360).ToString("000")+"°",l.unit*0.028f,cream);
                    Panel(l.speed,l.unit);Text(l.speed,"ХОД "+input.ship.Speed.ToString("F1")+" м/с",l.unit*0.027f,cream);
                    Radar(input,l);
                    Hull(l);
                    Panel(l.fps,l.unit);Text(l.fps,Application.isPlaying?Mathf.RoundToInt(input.Fps)+" FPS":"FPS —",l.unit*0.024f,muted);
                    Text(l.version,input.BuildText,l.unit*0.023f,cream);
                    Broadside guns=input.Guns();
                    if(guns!=null)
                    {
                        Volley(input,l,l.port,-1,guns.Readiness(-1),guns.cooldown);
                        Volley(input,l,l.starboard,1,guns.Readiness(1),guns.cooldown);
                        Panel(l.mode,l.unit,false,input.Pressed(l.mode));
                        Text(l.mode,guns.ripple?"ЗАЛП ВОЛНОЙ":"ВЕСЬ БОРТ",l.unit*0.026f,cream);
                    }
                    Vector2 home=input.StickActive?l.ClampWheel(input.StickOrigin):l.wheel;
                    float r=l.radius, a=input.StickActive?0.85f:0.45f;
                    if(HasKit)
                    {
                        // Штурвал поворачивается вместе с рулём, ручка — палец.
                        Vector2 off=input.StickOffset(l.radius);
                        float ang=Mathf.Clamp(off.x/Mathf.Max(1f,l.radius),-1f,1f)*1.2f;
                        float d=r*2.35f;
                        SpriteRot(home,new Vector2(d,d*KWheel.height/KWheel.width),ang,KWheel,new Color(1f,1f,1f,input.StickActive?1f:0.7f));
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
                    if(input.HintAlpha>0f)
                    {
                        Color hint=new Color(cream.r,cream.g,cream.b,input.HintAlpha*0.85f);
                        Text(new Rect(l.hint.x,l.hint.y,l.hint.width,l.hint.height*0.5f),"СПРАВА — ОБЗОР",l.unit*0.021f,hint);
                        Text(new Rect(l.hint.x,l.hint.center.y,l.hint.width,l.hint.height*0.5f),"2 ПАЛЬЦА — МАСШТАБ",l.unit*0.021f,hint);
                    }
                }
                finally {GL.End();}
                FlushSprites();
                // Все размеры запрашиваются до чтения UV: перестройка атласа не портит уже собранные буквы.
                for(int i=0;i<labels.Count;i++) font.RequestCharactersInTexture(labels[i].text,labels[i].size,FontStyle.Normal);
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
            // Текст никогда не выходит за свою плашку: поля по бокам под угловой орнамент, по высоте — 80% плашки.
            float side=Mathf.Max(6f,label.rect.height*0.3f);
            float scale=Mathf.Clamp01((label.rect.width-side*2f)/width);
            scale=Mathf.Min(scale,label.rect.height*0.8f/Mathf.Max(1f,maxY-minY));
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
