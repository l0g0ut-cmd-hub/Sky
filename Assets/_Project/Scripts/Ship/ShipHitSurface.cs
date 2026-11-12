using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Static source triangles in model space; a small BVH avoids testing the whole mesh per ball.</summary>
    public sealed class ShipHitSurface
    {
        public struct Contact { public Vector3 point,normal; public int slot; public float fraction; }
        struct Face { public int a,b,c,slot; public Vector3 center; }
        struct Node { public Bounds bounds; public int left,right,start,count; }
        sealed class SortFaces : IComparer<int>
        {
            public Face[] faces; public int axis;
            public int Compare(int a,int b){return faces[a].center[axis].CompareTo(faces[b].center[axis]);}
        }
        readonly Vector3[] vertices;
        readonly Face[] faces;
        readonly int[] order,stack=new int[64];
        readonly List<Node> nodes=new List<Node>();
        readonly SortFaces sorter;
        public int TriangleCount {get {return faces.Length;}}
        public Bounds Bounds {get;private set;}

        public ShipHitSurface(TextAsset source)
        {
            float started=Time.realtimeSinceStartup;
            byte[] b=source.bytes;string magic=System.Text.Encoding.ASCII.GetString(b,0,4);
            if(magic!="STM1"&&magic!="STM2")throw new InvalidOperationException("Invalid hit-surface mesh");
            int count=BitConverter.ToInt32(b,4),second=BitConverter.ToInt32(b,8),offset=12;
            vertices=new Vector3[count];
            for(int i=0;i<count;i++){vertices[i]=new Vector3(BitConverter.ToSingle(b,offset),BitConverter.ToSingle(b,offset+4),BitConverter.ToSingle(b,offset+8));offset+=12;}
            offset=12+count*32;var all=new List<Face>();
            int groups=magic=="STM1"?1:second;
            for(int group=0;group<groups;group++)
            {
                int slot=0,indices=second*3;
                if(magic=="STM2"){slot=BitConverter.ToInt32(b,offset);indices=BitConverter.ToInt32(b,offset+4);offset+=8;}
                for(int i=0;i<indices;i+=3)
                {
                    int a=BitConverter.ToInt32(b,offset),c=BitConverter.ToInt32(b,offset+8),d=BitConverter.ToInt32(b,offset+4);offset+=12;
                    if(Vector3.Cross(vertices[d]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-14f)continue;
                    all.Add(new Face{a=a,b=d,c=c,slot=slot,center=(vertices[a]+vertices[d]+vertices[c])/3f});
                }
            }
            faces=all.ToArray();if(faces.Length==0)throw new InvalidOperationException("Empty hit surface");
            order=new int[faces.Length];for(int i=0;i<order.Length;i++)order[i]=i;
            sorter=new SortFaces{faces=faces};Build(0,order.Length);Bounds=nodes[0].bounds;
            Debug.Log("SkyTide: exact hit surface — triangles="+faces.Length+", nodes="+nodes.Count+", built="+(Time.realtimeSinceStartup-started).ToString("F3")+" s");
        }
        int Build(int start,int count)
        {
            Face first=faces[order[start]];Bounds bounds=new Bounds(vertices[first.a],Vector3.zero),centers=new Bounds(first.center,Vector3.zero);
            for(int i=start;i<start+count;i++){Face f=faces[order[i]];bounds.Encapsulate(vertices[f.a]);bounds.Encapsulate(vertices[f.b]);bounds.Encapsulate(vertices[f.c]);centers.Encapsulate(f.center);}
            bounds.Expand(.0002f);int at=nodes.Count;nodes.Add(new Node{bounds=bounds,start=start,count=count,left=-1,right=-1});
            if(count>12)
            {
                Vector3 size=centers.size;sorter.axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
                Array.Sort(order,start,count,sorter);int half=count/2,left=Build(start,half),right=Build(start+half,count-half);
                nodes[at]=new Node{bounds=bounds,left=left,right=right,count=0};
            }
            return at;
        }
        static bool Slab(Bounds bounds,Vector3 start,Vector3 delta,float limit)
        {
            float near=0,far=limit;Vector3 min=bounds.min,max=bounds.max;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(delta[axis])<1e-9f){if(start[axis]<min[axis]||start[axis]>max[axis])return false;continue;}
                float a=(min[axis]-start[axis])/delta[axis],b=(max[axis]-start[axis])/delta[axis];if(a>b){float swap=a;a=b;b=swap;}
                near=Mathf.Max(near,a);far=Mathf.Min(far,b);if(near>far)return false;
            }
            return true;
        }
        public bool Trace(Vector3 start,Vector3 end,out Contact contact)
        {
            contact=new Contact{fraction=1.000001f};Vector3 delta=end-start;int pending=1;stack[0]=0;bool found=false;
            while(pending>0)
            {
                Node node=nodes[stack[--pending]];if(!Slab(node.bounds,start,delta,contact.fraction))continue;
                if(node.count==0){stack[pending++]=node.left;stack[pending++]=node.right;continue;}
                for(int i=node.start;i<node.start+node.count;i++)
                {
                    Face f=faces[order[i]];Vector3 e1=vertices[f.b]-vertices[f.a],e2=vertices[f.c]-vertices[f.a],p=Vector3.Cross(delta,e2);
                    float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-8f)continue;
                    float inv=1f/det;Vector3 s=start-vertices[f.a];float u=Vector3.Dot(s,p)*inv;if(u<-.000001f||u>1.000001f)continue;
                    Vector3 q=Vector3.Cross(s,e1);float v=Vector3.Dot(delta,q)*inv;if(v<-.000001f||u+v>1.000001f)continue;
                    float t=Vector3.Dot(e2,q)*inv;if(t<0||t>1||t>=contact.fraction)continue;
                    Vector3 normal=Vector3.Cross(e1,e2).normalized;if(Vector3.Dot(normal,delta)>0)normal=-normal;
                    contact=new Contact{point=start+delta*t,normal=normal,slot=f.slot,fraction=t};found=true;
                }
            }
            return found;
        }
    }
}
