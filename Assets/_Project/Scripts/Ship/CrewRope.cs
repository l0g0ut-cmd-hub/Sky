using UnityEngine;

namespace SkyTide
{
    /// <summary>A round, shaded running rope. Its straight hand span has a real 25 mm radius.</summary>
    public sealed class CrewRope
    {
        const int Rings=6,Sides=12,Stride=Sides+1;
        public const float Radius=CrewParts.GripWoodRadius;
        readonly Transform transform;
        readonly Mesh mesh;
        readonly Vector3[] points=new Vector3[Rings];
        readonly Vector3[] vertices=new Vector3[Rings*Stride+2*(Sides+2)];
        readonly Vector3[] normals=new Vector3[Rings*Stride+2*(Sides+2)];
        readonly Vector4[] tangents=new Vector4[Rings*Stride+2*(Sides+2)];
        readonly Vector2[] uv=new Vector2[Rings*Stride+2*(Sides+2)];
        readonly Vector3[] ringAxes=new Vector3[Rings],ringSides=new Vector3[Rings];
#if UNITY_EDITOR
        public Mesh MeshCapture {get {return mesh;}}
        public Transform TransformCapture {get {return transform;}}
#endif

        public CrewRope(Transform parent,Material material)
        {
            GameObject go=new GameObject("Round running rigging");transform=go.transform;transform.SetParent(parent,false);
            mesh=new Mesh{name="Working rope tube"};mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
            int[] triangles=new int[(Rings-1)*Sides*6+Sides*6];int at=0;
            for(int ring=0;ring<Rings-1;ring++)for(int side=0;side<Sides;side++)
            {
                int a=ring*Stride+side,b=a+1,c=b+Stride,d=a+Stride;
                triangles[at++]=a;triangles[at++]=b;triangles[at++]=c;
                triangles[at++]=a;triangles[at++]=c;triangles[at++]=d;
            }
            for(int end=0;end<2;end++)for(int side=0;side<Sides;side++)
            {
                int middle=Rings*Stride+end*(Sides+2),a=middle+1+side,b=a+1;
                triangles[at++]=middle;triangles[at++]=end==0?b:a;triangles[at++]=end==0?a:b;
            }
            mesh.vertices=vertices;mesh.triangles=triangles;
        }

        public void SetPositions(Vector3[] world)
        {
            for(int i=0;i<Rings;i++)points[i]=transform.InverseTransformPoint(world[i]);
            float length=0;Vector3 previousSide=Vector3.right;
            for(int ring=0;ring<Rings;ring++)
            {
                Vector3 axis=(points[Mathf.Min(ring+1,Rings-1)]-points[Mathf.Max(0,ring-1)]).normalized;
                Vector3 side=Vector3.ProjectOnPlane(previousSide,axis).normalized;
                if(side.sqrMagnitude<.1f)side=Vector3.Cross(axis,Vector3.forward).normalized;
                Vector3 other=Vector3.Cross(axis,side);ringAxes[ring]=axis;ringSides[ring]=side;previousSide=side;
                if(ring>0)length+=Vector3.Distance(points[ring],points[ring-1]);
                for(int s=0;s<=Sides;s++)
                {
                    float angle=s*Mathf.PI*2/Sides;Vector3 normal=side*Mathf.Cos(angle)+other*Mathf.Sin(angle);
                    Vector3 tangent=-side*Mathf.Sin(angle)+other*Mathf.Cos(angle);int i=ring*Stride+s;
                    vertices[i]=points[ring]+normal*Radius;normals[i]=normal;tangents[i]=new Vector4(tangent.x,tangent.y,tangent.z,1);
                    uv[i]=new Vector2((float)s/Sides,length/.30f);
                }
            }
            for(int end=0;end<2;end++)
            {
                int ring=end==0?0:Rings-1,middle=Rings*Stride+end*(Sides+2);Vector3 normal=ringAxes[ring]*(end==0?-1:1),side=ringSides[ring];
                vertices[middle]=points[ring];normals[middle]=normal;tangents[middle]=new Vector4(side.x,side.y,side.z,1);uv[middle]=new Vector2(.5f,.5f);
                for(int s=0;s<=Sides;s++)
                {
                    int i=middle+1+s;vertices[i]=vertices[ring*Stride+s];normals[i]=normal;tangents[i]=tangents[middle];
                    float angle=s*Mathf.PI*2/Sides;uv[i]=new Vector2(.5f+.5f*Mathf.Cos(angle),.5f+.5f*Mathf.Sin(angle));
                }
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.tangents=tangents;mesh.uv=uv;mesh.RecalculateBounds();
        }
    }
}
