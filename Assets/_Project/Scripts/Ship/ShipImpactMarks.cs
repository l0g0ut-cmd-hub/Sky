using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Painted battle scars on the hull: for every hit a soot stain and a torn-through hole
    /// (Resources/FX/hull_decals: top row 4 holes, bottom row 4 soot stains), each a small grid
    /// projected onto the real hull surface so it bends with the planks. One mesh, one draw call.
    /// </summary>
    public sealed class ShipImpactMarks
    {
        struct Mark { public Vector3 point,normal; public float radius; public int seed; }
        const int Grid=6;                       // 7x7 vertices per layer
        readonly List<Mark> marks=new List<Mark>();
        readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();
        readonly List<Color32> colors=new List<Color32>();
        readonly List<int> soot=new List<int>(),holes=new List<int>();
        readonly Transform parent;
        readonly ShipHitSurface surface;
        readonly Mesh mesh;
        readonly Material material;
        public int Count {get {return marks.Count;}}
        public int VertexCount {get {return mesh.vertexCount;}}
        public ShipImpactMarks(Transform parent,ShipHitSurface surface)
        {
            this.parent=parent;this.surface=surface;
            GameObject go=new GameObject("Surface impact scars");go.transform.SetParent(parent,false);
            mesh=new Mesh{name="Projected battle scars"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
            Shader shader=Shader.Find("Hidden/SkyTide/HullDecal");
            Texture2D atlas=Resources.Load<Texture2D>("FX/hull_decals");
            if(shader!=null&&shader.isSupported&&atlas!=null){material=new Material(shader){name="Hull battle decals"};material.mainTexture=atlas;}
            else material=MaterialFactory.Opaque("Charred impact cavities",new Color(.018f,.011f,.008f),.025f);
            MeshRenderer r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static float Noise(int seed,int k){return Mathf.Repeat(Mathf.Sin(seed*3.17f+k*12.91f)*43758.54f,1f);}
        public void Add(Vector3 world,Vector3 worldNormal,float severity,int seed)
        {
            Vector3 point=parent.InverseTransformPoint(world),normal=parent.InverseTransformDirection(worldNormal).normalized;
            foreach(Mark m in marks)if((m.point-point).sqrMagnitude<.25f)return;
            if(marks.Count==16)marks.RemoveAt(0);
            marks.Add(new Mark{point=point,normal=normal,radius=Mathf.Lerp(1.3f,2f,Mathf.Clamp01(severity)),seed=seed});Rebuild();
        }
        /// <summary>One projected grid; cell = atlas cell (0..3 holes, 4..7 soot).</summary>
        void Layer(List<int> tris,Mark m,float half,float angle,int cell,float lift,byte tone)
        {
            Vector3 n=m.normal;
            Vector3 side=Vector3.Cross(n,Mathf.Abs(n.y)>.85f?Vector3.right:Vector3.up).normalized,up=Vector3.Cross(n,side);
            float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
            Vector3 ax=(side*c+up*s)*half,ay=(-side*s+up*c)*half;
            Vector2 cellMin=new Vector2((cell%4)*.25f,cell<4?.5f:0f);   // texture v=0 is the bottom row (soot)
            int start=vertices.Count;
            for(int y=0;y<=Grid;y++)for(int x=0;x<=Grid;x++)
            {
                float u=(float)x/Grid,v=(float)y/Grid;
                Vector3 p=m.point+ax*(u*2-1)+ay*(v*2-1),pn=n;ShipHitSurface.Contact hit;byte a=255;
                if(surface.Trace(p+n*2.5f,p-n*4f,out hit)&&(hit.point-p).sqrMagnitude<4f){p=hit.point;}
                else a=0;                                                   // off the hull: fade this corner out
                vertices.Add(p+pn*lift);normals.Add(pn);colors.Add(new Color32(tone,tone,tone,a));
                uv.Add(cellMin+new Vector2(.004f+u*.242f,.004f+v*.492f));
            }
            int row=Grid+1;
            for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++)
            {
                int i=start+y*row+x;
                Face(tris,i,i+row,i+1);Face(tris,i+1,i+row,i+row+1);
            }
        }
        void Face(List<int> list,int a,int b,int c)
        {
            if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a])<0){int swap=b;b=c;c=swap;}
            list.Add(a);list.Add(b);list.Add(c);
        }
        void Rebuild()
        {
            vertices.Clear();normals.Clear();uv.Clear();colors.Clear();soot.Clear();holes.Clear();
            foreach(Mark m in marks)
            {
                ShipHitSurface.Contact probe;
                if(!surface.Trace(m.point+m.normal*2.5f,m.point-m.normal*4f,out probe))continue;
                float lift=.02f+(m.seed%16)*.001f;
                int hole=m.radius>1.65f?(m.seed&1):2+(m.seed&1);
                Layer(soot,m,m.radius*2.4f,Noise(m.seed,3)*Mathf.PI*2,4+(m.seed>>1&3),lift,255);
                Layer(holes,m,m.radius,(Noise(m.seed,7)-.5f)*.9f,hole,lift+.006f,190);   // щепа чуть темнее — не светлая кайма на тёмном борту
            }
            List<int> all=new List<int>(soot.Count+holes.Count);all.AddRange(soot);all.AddRange(holes);   // soot under every hole
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(colors);
            mesh.SetTriangles(all,0);mesh.RecalculateBounds();
        }
        public void Clear(){marks.Clear();mesh.Clear();}
        public void Dispose(){SkyTideBootstrap.SafeDestroy(mesh);SkyTideBootstrap.SafeDestroy(material);}
    }
}
