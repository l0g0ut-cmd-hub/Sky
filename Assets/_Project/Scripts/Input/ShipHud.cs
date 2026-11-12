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
        void Panel(Rect r, float u, bool lit = false, bool pressed = false)
        {
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
            Rect title=new Rect(r.x,r.y+l.unit*0.012f,r.width,r.height*0.44f);
            Text(title,side<0?"ЛЕВЫЙ БОРТ":"ПРАВЫЙ БОРТ",l.unit*0.028f,cream);
            Rect state=new Rect(r.x,r.y+r.height*0.5f,r.width,r.height*0.24f);
            Text(state,ready?"ГОТОВ":"ПЕРЕЗАРЯДКА "+((1f-readiness)*cooldown).ToString("F1")+" с",l.unit*0.022f,ready?gold:muted);
            float inset=l.unit*0.022f, h=Mathf.Max(2f,l.unit*0.006f);
            Rect track=new Rect(r.x+inset,r.yMax-l.unit*0.023f,r.width-inset*2f,h);
            Box(track,new Color(0.015f,0.02f,0.026f,1f));
            Box(new Rect(track.x,track.y,track.width*Mathf.Clamp01(readiness),track.height),ready?gold:new Color(0.51f,0.56f,0.6f));
        }

        /// <summary>Радар: курс корабля — вверх, цель — точка с расстоянием рядом; за пределом дальности — на ободе.</summary>
        void Radar(TouchControls input,HudLayout l)
        {
            Rect r=l.radar; if(r.width<l.unit*0.08f||input.ship==null)return;
            const float range=1500f;
            Vector2 c=r.center; float R=r.width*0.5f, lw=Mathf.Max(1f,l.unit*0.0025f);
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
                    Panel(l.clouds,l.unit,false,input.Pressed(l.clouds));
                    Text(new Rect(l.clouds.x,l.clouds.y,l.clouds.width,l.clouds.height*0.42f),"ОБЛАКА",l.unit*0.021f,muted);
                    Text(new Rect(l.clouds.x,l.clouds.y+l.clouds.height*0.36f,l.clouds.width,l.clouds.height*0.55f),input.game.volumetricClouds?"ОБЪЁМ":"РИСОВАННЫЕ",l.unit*0.031f,cream);
                    if(input.game.ships!=null&&input.game.ships.Length>1) {Panel(l.ship,l.unit);Text(l.ship,input.game.CurrentShipName,l.unit*0.025f,cream);}
                    Panel(l.course,l.unit); Text(l.course,"КУРС "+(Mathf.RoundToInt(input.ship.transform.eulerAngles.y)%360).ToString("000")+"°",l.unit*0.028f,cream);
                    Panel(l.speed,l.unit);Text(l.speed,"ХОД "+input.ship.Speed.ToString("F1")+" м/с",l.unit*0.027f,cream);
                    Radar(input,l);
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
                    if(input.HintAlpha>0f)
                    {
                        Color hint=new Color(cream.r,cream.g,cream.b,input.HintAlpha*0.85f);
                        Text(new Rect(l.hint.x,l.hint.y,l.hint.width,l.hint.height*0.5f),"СПРАВА — ОБЗОР",l.unit*0.021f,hint);
                        Text(new Rect(l.hint.x,l.hint.center.y,l.hint.width,l.hint.height*0.5f),"2 ПАЛЬЦА — МАСШТАБ",l.unit*0.021f,hint);
                    }
                }
                finally {GL.End();}
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
            float scale=Mathf.Clamp01((label.rect.width-6f)/width);
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
