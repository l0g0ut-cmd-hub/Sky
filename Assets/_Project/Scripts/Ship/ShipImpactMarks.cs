using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Bounded, surface-projected char and jagged raised wood, attached to the damaged model.</summary>
    public sealed class ShipImpactMarks
    {
        struct Mark { public Vector3 point,normal; public float radius; public int seed; }
        readonly List<Mark> marks=new List<Mark>();
        readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();
        readonly List<int> black=new List<int>(),wood=new List<int>();
        readonly Transform parent;
        readonly ShipHitSurface surface;
        readonly Mesh mesh;
        readonly Material charred,raw;
        public int Count {get {return marks.Count;}}
        public int VertexCount {get {return mesh.vertexCount;}}
        public ShipImpactMarks(Transform parent,ShipHitSurface surface)
        {
            this.parent=parent;this.surface=surface;
            GameObject go=new GameObject("Surface impact scars");go.transform.SetParent(parent,false);
            mesh=new Mesh{name="Projected battle scars"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
            charred=MaterialFactory.Opaque("Charred impact cavities",new Color(.018f,.011f,.008f),.025f);
            raw=MaterialFactory.Opaque("Torn burnt plank edges",new Color(.13f,.055f,.018f),.06f);
            go.AddComponent<MeshRenderer>().sharedMaterials=new[]{charred,raw};
        }
        static float Noise(int seed,int point){return Mathf.Repeat(Mathf.Sin(seed*3.17f+point*12.91f)*43758.54f,1f);}
        public void Add(Vector3 world,Vector3 worldNormal,float severity,int seed)
        {
            Vector3 point=parent.InverseTransformPoint(world),normal=parent.InverseTransformDirection(worldNormal).normalized;
            foreach(Mark m in marks)if((m.point-point).sqrMagnitude<.25f)return;
            if(marks.Count==16)marks.RemoveAt(0);
            marks.Add(new Mark{point=point,normal=normal,radius=Mathf.Lerp(.75f,1.35f,severity),seed=seed});Rebuild();
        }
        int Vertex(Vector3 p,Vector3 n){int i=vertices.Count;vertices.Add(p);normals.Add(n);uv.Add(new Vector2(p.x,p.y));return i;}
        void Face(List<int> list,int a,int b,int c)
        {
            if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a])<0){int swap=b;b=c;c=swap;}
            list.Add(a);list.Add(b);list.Add(c);
        }
        void Rebuild()
        {
            vertices.Clear();normals.Clear();uv.Clear();black.Clear();wood.Clear();
            foreach(Mark m in marks)
            {
                Vector3 side=Vector3.Cross(m.normal,Mathf.Abs(m.normal.y)>.85f?Vector3.right:Vector3.up).normalized,up=Vector3.Cross(m.normal,side);
                const int sides=14;Vector3[] ring=new Vector3[sides];bool valid=true;
                for(int i=0;i<sides;i++)
                {
                    float angle=i*Mathf.PI*2/sides,radius=m.radius*Mathf.Lerp(.75f,1.15f,Noise(m.seed,i));
                    Vector3 p=m.point+(side*Mathf.Cos(angle)+up*Mathf.Sin(angle)*.6f)*radius;ShipHitSurface.Contact hit;
                    if(!surface.Trace(p+m.normal*2.5f,p-m.normal*4f,out hit)||(hit.point-p).sqrMagnitude>2.25f){valid=false;break;}
                    ring[i]=hit.point;
                }
                if(!valid)continue;
                float lift=.025f+(m.seed%16)*.0015f;
                int center=Vertex(m.point+m.normal*lift,m.normal),inner=vertices.Count;
                for(int i=0;i<sides;i++)
                {
                    Vector3 p=Vector3.Lerp(m.point,ring[i],.62f);ShipHitSurface.Contact projected;
                    if(surface.Trace(p+m.normal*2.5f,p-m.normal*4f,out projected))p=projected.point;
                    Vertex(p+m.normal*(lift+.01f),m.normal);
                }
                int outer=vertices.Count;
                for(int i=0;i<sides;i++)Vertex(ring[i]+m.normal*(.015f+Noise(m.seed,i+20)*.11f),m.normal);
                for(int i=0;i<sides;i++)
                {
                    int next=(i+1)%sides;Face(black,center,inner+i,inner+next);
                    Face(i%3==0?wood:black,inner+i,outer+i,outer+next);Face(i%3==0?wood:black,inner+i,outer+next,inner+next);
                }
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.subMeshCount=2;
            mesh.SetTriangles(black,0);mesh.SetTriangles(wood,1);mesh.RecalculateBounds();
        }
        public void Clear(){marks.Clear();mesh.Clear();}
        public void Dispose(){SkyTideBootstrap.SafeDestroy(mesh);SkyTideBootstrap.SafeDestroy(charred);SkyTideBootstrap.SafeDestroy(raw);}
    }
}
