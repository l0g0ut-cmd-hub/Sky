using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Shared deck fittings, built once from proper shaded geometry.</summary>
    public sealed class CrewParts
    {
        public const float WheelHeight=1.42f,GripRadius=.375f,GripWoodRadius=.025f;
        readonly List<Vector3> v=new List<Vector3>(),n=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();
        readonly List<int>[] tris={new List<int>(),new List<int>()};
        int Add(Vector3 p,Vector3 normal)
        {
            int i=v.Count;v.Add(p);n.Add(normal);
            uv.Add(Mathf.Abs(normal.y)>.5f?new Vector2(p.x,p.z):Mathf.Abs(normal.x)>.5f?new Vector2(p.z,p.y):new Vector2(p.x,p.y));return i;
        }
        void Triangle(int a,int b,int c,int slot)
        {
            if(Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]),n[a]+n[b]+n[c])<0){int q=b;b=c;c=q;}
            tris[slot].Add(a);tris[slot].Add(b);tris[slot].Add(c);
        }
        public void Box(Vector3 center,Vector3 size,int slot=0)
        {
            Vector3[] axes={Vector3.right,Vector3.up,Vector3.forward};
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
            {
                Vector3 normal=axes[axis]*sign,u=axes[(axis+1)%3]*size[(axis+1)%3]*.5f,w=axes[(axis+2)%3]*size[(axis+2)%3]*.5f;
                Vector3 p=center+normal*size[axis]*.5f;
                int a=Add(p-u-w,normal),b=Add(p+u-w,normal),c=Add(p+u+w,normal),d=Add(p-u+w,normal);
                Triangle(a,b,c,slot);Triangle(a,c,d,slot);
            }
        }
        public void Cylinder(Vector3 from,Vector3 to,float radius,int slot=0,int sides=16)
        {
            Vector3 axis=(to-from).normalized;
            Vector3 a=Vector3.Cross(axis,Mathf.Abs(axis.y)>.8f?Vector3.right:Vector3.up).normalized,b=Vector3.Cross(axis,a);
            int start=v.Count;
            for(int j=0;j<2;j++)for(int i=0;i<sides;i++)
            {
                float t=i*Mathf.PI*2/sides;Vector3 normal=a*Mathf.Cos(t)+b*Mathf.Sin(t);
                Add((j==0?from:to)+normal*radius,normal);
            }
            for(int i=0;i<sides;i++){int k=(i+1)%sides;Triangle(start+i,start+k,start+sides+k,slot);Triangle(start+i,start+sides+k,start+sides+i,slot);}
            for(int j=0;j<2;j++)
            {
                Vector3 c=j==0?from:to,normal=axis*(j==0?-1:1);int middle=Add(c,normal),ring=v.Count;
                for(int i=0;i<sides;i++){float t=i*Mathf.PI*2/sides;Add(c+(a*Mathf.Cos(t)+b*Mathf.Sin(t))*radius,normal);}
                for(int i=0;i<sides;i++)Triangle(middle,ring+i,ring+(i+1)%sides,slot);
            }
        }
        public void Torus(float radius,float tube,int slot=0,int segments=48)
        {
            int start=v.Count;const int rings=8;
            for(int i=0;i<segments;i++)for(int j=0;j<rings;j++)
            {
                float a=i*Mathf.PI*2/segments,b=j*Mathf.PI*2/rings;
                Vector3 normal=new Vector3(Mathf.Cos(a)*Mathf.Cos(b),Mathf.Sin(a)*Mathf.Cos(b),Mathf.Sin(b));
                Add(new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0)+normal*tube,normal);
            }
            for(int i=0;i<segments;i++)for(int j=0;j<rings;j++)
            {
                int a=start+i*rings+j,b=start+((i+1)%segments)*rings+j,c=start+((i+1)%segments)*rings+(j+1)%rings,d=start+i*rings+(j+1)%rings;
                Triangle(a,b,c,slot);Triangle(a,c,d,slot);
            }
        }
        public Mesh Finish(string name)
        {
            Mesh mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.subMeshCount=2;
            mesh.SetTriangles(tris[0],0);mesh.SetTriangles(tris[1],1);mesh.RecalculateBounds();mesh.RecalculateTangents();mesh.UploadMeshData(true);return mesh;
        }
        public static Transform Object(string name,Transform parent,Mesh mesh,Material wood,Material metal,Vector3 position)
        {
            GameObject go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=mesh.subMeshCount==1?new[]{wood}:new[]{wood,metal};return go.transform;
        }
        public static Mesh Wheel()
        {
            CrewParts g=new CrewParts();g.Torus(.265f,.025f);
            g.Cylinder(new Vector3(0,0,-.035f),new Vector3(0,0,.035f),.06f,1);
            for(int i=0;i<8;i++)
            {
                float t=i*Mathf.PI/4;Vector3 d=new Vector3(Mathf.Cos(t),Mathf.Sin(t),0);
                g.Cylinder(d*.04f,d*.35f,.016f);g.Cylinder(d*.25f,d*.285f,.023f,1);
                g.Cylinder(d*.285f,d*.45f,GripWoodRadius);
            }
            return g.Finish("Bronze-bound wooden helm");
        }
        public static Mesh Stand()
        {
            CrewParts g=new CrewParts();g.Box(new Vector3(0,.08f,.07f),new Vector3(.36f,.16f,.46f));
            float top=WheelHeight-.045f,bottom=.125f;
            g.Box(new Vector3(0,(top+bottom)*.5f,.08f),new Vector3(.14f,top-bottom,.16f));
            g.Cylinder(new Vector3(0,WheelHeight,-.06f),new Vector3(0,WheelHeight,.16f),.045f,1);
            return g.Finish("Helm pedestal");
        }
        public static Mesh GunPlatform()
        {
            CrewParts g=new CrewParts();
            for(int i=0;i<9;i++)g.Box(new Vector3(0,.19f,-1.37f+i*.34f),new Vector3(1.85f,.38f,.325f));
            g.Box(new Vector3(-.64f,.10f,-1.68f),new Vector3(.55f,.20f,.30f));
            g.Box(new Vector3(.64f,.10f,-1.68f),new Vector3(.55f,.20f,.30f));
            return g.Finish("Upper gun planked platform");
        }
        public static Mesh GunStand()
        {
            CrewParts g=new CrewParts();g.Box(new Vector3(0,.08f,0),new Vector3(.48f,.16f,.50f));
            g.Cylinder(new Vector3(0,.16f,0),new Vector3(0,1.47f,0),.10f);
            g.Cylinder(new Vector3(0,.20f,0),new Vector3(0,.30f,0),.12f,1);
            g.Cylinder(new Vector3(0,1.40f,-.24f),new Vector3(0,1.40f,.24f),.055f,1);
            g.Box(new Vector3(0,1.51f,-.22f),new Vector3(.12f,.30f,.07f),1);
            g.Box(new Vector3(0,1.51f,.22f),new Vector3(.12f,.30f,.07f),1);
            return g.Finish("Upper swivel gun mounting");
        }
        public static Mesh Hat()
        {
            CrewParts g=new CrewParts();const int count=48;
            int center=g.Add(new Vector3(0,.12f,0),Vector3.up),start=g.v.Count;
            for(int i=0;i<count;i++)
            {
                float t=i*Mathf.PI*2/count;float radius=.22f+.05f*Mathf.Cos(t*3f);
                g.Add(new Vector3(Mathf.Sin(t)*radius,.12f+.035f*(1-Mathf.Cos(t*3f)),Mathf.Cos(t)*radius),Vector3.up);
            }
            for(int i=0;i<count;i++)g.Triangle(center,start+i,start+(i+1)%count,0);
            g.Cylinder(new Vector3(0,.10f,0),new Vector3(0,.24f,0),.13f,0,24);
            g.Cylinder(new Vector3(0,.13f,0),new Vector3(0,.15f,0),.135f,1,24);
            return g.Finish("Captain tricorn");
        }
        public static Mesh Ramrod()
        {
            CrewParts g=new CrewParts();g.Cylinder(new Vector3(0,0,-.15f),new Vector3(0,0,.95f),GripWoodRadius);
            g.Cylinder(new Vector3(0,0,.88f),new Vector3(0,0,1.02f),.04f,1);return g.Finish("Gun rammer");
        }
        public static Mesh BreechHandles()
        {
            CrewParts g=new CrewParts();
            for(int side=-1;side<=1;side+=2)
            {
                g.Cylinder(new Vector3(-.75f,-.28f,side*.24f),new Vector3(-.57f,-.28f,side*.24f),GripWoodRadius);
                for(int end=0;end<2;end++)
                {
                    float x=end==0?-.75f:-.57f;
                    g.Cylinder(new Vector3(x,-.14f,side*.13f),new Vector3(x,-.28f,side*.24f),.012f,1);
                }
            }
            return g.Finish("Wooden breech control handles");
        }
        public static Mesh CargoCrate()
        {
            CrewParts g=new CrewParts();g.Box(Vector3.zero,new Vector3(.5f,.42f,.42f));
            for(int side=-1;side<=1;side+=2)
            {
                g.Box(new Vector3(side*.22f,0,0),new Vector3(.025f,.445f,.445f),1);
                g.Cylinder(new Vector3(side*.32f,.09f,-.09f),new Vector3(side*.32f,.09f,.09f),GripWoodRadius);
                for(int end=-1;end<=1;end+=2)g.Cylinder(new Vector3(side*.25f,.09f,end*.085f),new Vector3(side*.32f,.09f,end*.085f),.012f,1);
            }
            return g.Finish("Bound cargo crate with side grips");
        }
        public static Mesh Locker()
        {
            CrewParts g=new CrewParts();g.Box(new Vector3(0,.04f,0),new Vector3(1f,.08f,.54f));
            g.Box(new Vector3(0,1.36f,0),new Vector3(1f,.08f,.54f));
            g.Box(new Vector3(0,.7f,-.24f),new Vector3(1f,1.4f,.06f));
            for(int side=-1;side<=1;side+=2)g.Box(new Vector3(side*.47f,.7f,0),new Vector3(.06f,1.4f,.54f));
            return g.Finish("Quarter-deck stores locker");
        }
        public static Mesh CargoPallet()
        {
            CrewParts g=new CrewParts();g.Box(new Vector3(0,.04f,0),new Vector3(.9f,.08f,.8f));return g.Finish("Cargo pickup pallet");
        }
        public static Mesh LockerDoor(int side)
        {
            CrewParts g=new CrewParts();g.Box(new Vector3(-side*.22f,.7f,0),new Vector3(.44f,1.28f,.045f));
            g.Box(new Vector3(-side*.39f,.72f,.04f),new Vector3(.035f,.1f,.04f),1);
            return g.Finish("Hinged stores door");
        }
    }
}
