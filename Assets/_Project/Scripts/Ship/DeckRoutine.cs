using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Distance-driven deck work: constant walking speed, visible stops and shared conversation timing.</summary>
    public sealed class DeckRoutine
    {
        public struct Frame
        {
            public Vector3 position,forward;
            public float distance,speed,lift,crouch,talk,grip;
            public bool cargo,stored;
        }
        sealed class Path
        {
            readonly List<Vector3> points=new List<Vector3>();
            readonly List<float> distances=new List<float>();
            public float Length {get {return distances[distances.Count-1];}}
            public Path(Vector3[] route)
            {
                for(int i=0;i<route.Length-1;i++)for(int j=0;j<16;j++)
                {
                    float t=j/16f;Vector3 a=route[Mathf.Max(0,i-1)],b=route[i],c=route[i+1],d=route[Mathf.Min(route.Length-1,i+2)];
                    Vector3 p=.5f*((2f*b)+(-a+c)*t+(2f*a-5f*b+4f*c-d)*t*t+(-a+3f*b-3f*c+d)*t*t*t);
                    Add(p);
                }
                Add(route[route.Length-1]);
            }
            void Add(Vector3 p){distances.Add(points.Count==0?0f:Length+Vector3.Distance(points[points.Count-1],p));points.Add(p);}
            public void Sample(float distance,out Vector3 position,out Vector3 direction)
            {
                distance=Mathf.Clamp(distance,0f,Length);int lo=0,hi=points.Count-1;
                while(hi-lo>1){int mid=(lo+hi)/2;if(distances[mid]<=distance)lo=mid;else hi=mid;}
                float t=Mathf.InverseLerp(distances[lo],distances[hi],distance);
                position=Vector3.Lerp(points[lo],points[hi],t);
                Vector3 a=points[hi]-points[Mathf.Max(0,lo-1)],b=points[Mathf.Min(points.Count-1,hi+1)]-points[lo];
                direction=Vector3.Lerp(a.normalized,b.normalized,t).normalized;
            }
        }
        readonly Path path;
        readonly bool cargo;
        public const float PatrolSpeed=.68f,LoadedSpeed=.50f,EmptySpeed=.85f,PickupTime=2.3f,DropTime=2.4f;
        public const float ConversationTime=5.2f,TurnTime=1.2f,RecoveryTime=.9f;
        public static readonly Vector3 CargoSource=new Vector3(2.8f,ShipCrew.MainDeck,-2.65f);
        public static readonly Vector3 CargoDestination=new Vector3(-1.8f,ShipCrew.MainDeck,-7f);
        public static readonly Vector3 LookoutStation=new Vector3(2.5f,ShipCrew.MainDeck,7.3f);
        float TravelTime(float speed){return path.Length/speed+.4f;}
        float Travel(float time,float speed,out float velocity)
        {
            float duration=TravelTime(speed);time=Mathf.Clamp(time,0,duration);
            if(time<.4f){velocity=speed*time/.4f;return .5f*speed*time*time/.4f;}
            if(time>duration-.4f){float left=duration-time;velocity=speed*left/.4f;return path.Length-.5f*speed*left*left/.4f;}
            velocity=speed;return speed*(time-.2f);
        }
        public float Arrival {get {return TravelTime(cargo?LoadedSpeed:PatrolSpeed)+(cargo?PickupTime:0f);}}
        public float Duration {get {return cargo?PickupTime+TravelTime(LoadedSpeed)+DropTime+RecoveryTime+TravelTime(EmptySpeed)+1.4f:2f*TravelTime(PatrolSpeed)+ConversationTime+TurnTime+1.6f;}}
        public DeckRoutine(bool cargo)
        {
            this.cargo=cargo;
            path=new Path(cargo?new[]{CargoSource,new Vector3(2.8f,ShipCrew.MainDeck,-4.2f),new Vector3(2.2f,ShipCrew.MainDeck,-4.8f),
                new Vector3(2.2f,ShipCrew.MainDeck,-6.8f),new Vector3(-1.4f,ShipCrew.MainDeck,-6.8f),new Vector3(-1.8f,ShipCrew.MainDeck,-6.8f),CargoDestination}
                :new[]{new Vector3(-2.4f,ShipCrew.MainDeck,-5.6f),new Vector3(-2.4f,ShipCrew.MainDeck,-3.8f),new Vector3(-1.65f,ShipCrew.MainDeck,-2.8f),
                new Vector3(-1.65f,ShipCrew.MainDeck,1.4f),new Vector3(-1.5f,ShipCrew.MainDeck,4f),new Vector3(-1.5f,ShipCrew.MainDeck,6.25f),new Vector3(.8f,ShipCrew.MainDeck,6.25f),new Vector3(1.1f,ShipCrew.MainDeck,6.5f)});
        }
        static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3f-2f*t);}
        static Vector3 Face(Vector3 a,Vector3 b,float t)
        {
            float from=Mathf.Atan2(a.x,a.z)*Mathf.Rad2Deg,to=Mathf.Atan2(b.x,b.z)*Mathf.Rad2Deg;
            return Quaternion.Euler(0,Mathf.LerpAngle(from,to,Ease(t)),0)*Vector3.forward;
        }
        public Frame Sample(float time)
        {
            float t=Mathf.Repeat(time,Duration),travel=TravelTime(cargo?LoadedSpeed:PatrolSpeed);
            Frame f=new Frame();Vector3 start,first,last,end;path.Sample(0f,out start,out first);path.Sample(path.Length,out end,out last);
            if(cargo)
            {
                float dropStart=PickupTime+travel,dropEnd=dropStart+DropTime,backStart=dropEnd+RecoveryTime,backEnd=backStart+TravelTime(EmptySpeed);
                if(t<PickupTime){f.position=start;f.forward=Vector3.back;f.lift=Ease(t/PickupTime);f.cargo=true;f.grip=1f;}
                else if(t<dropStart){f.distance=Travel(t-PickupTime,LoadedSpeed,out f.speed);path.Sample(f.distance,out f.position,out f.forward);f.lift=1f;f.cargo=true;f.grip=1f;}
                else if(t<dropEnd){f.position=end;f.forward=Face(last,Vector3.back,(t-dropStart)/.45f);f.lift=1f-Ease((t-dropStart)/DropTime);f.cargo=true;f.grip=1f;}
                else if(t<backStart){float q=(t-dropEnd)/RecoveryTime;f.position=end;f.forward=Face(Vector3.back,-last,q);f.grip=1f-Ease(q);f.stored=true;}
                else if(t<backEnd){float d=Travel(t-backStart,EmptySpeed,out f.speed);path.Sample(path.Length-d,out f.position,out f.forward);f.forward=-f.forward;f.distance=path.Length+d;f.stored=true;}
                else{f.position=start;f.forward=Face(-first,Vector3.back,(t-backEnd)/1.4f);f.stored=true;}
                f.crouch=f.cargo?1f-f.lift:f.grip;
            }
            else
            {
                Vector3 conversation=(LookoutStation-end).normalized;
                if(t<travel){f.distance=Travel(t,PatrolSpeed,out f.speed);path.Sample(f.distance,out f.position,out f.forward);}
                else if(t<travel+ConversationTime){float a=t-travel;f.position=end;f.forward=Face(last,conversation,a/.65f);f.talk=Ease(a/.65f)*Ease((ConversationTime-a)/.65f);}
                else if(t<travel+ConversationTime+TurnTime){f.position=end;f.forward=Face(conversation,-last,(t-travel-ConversationTime)/TurnTime);}
                else if(t<2f*travel+ConversationTime+TurnTime){float d=Travel(t-travel-ConversationTime-TurnTime,PatrolSpeed,out f.speed);path.Sample(path.Length-d,out f.position,out f.forward);f.forward=-f.forward;f.distance=path.Length+d;}
                else{f.position=start;f.forward=Face(-first,first,(t-2f*travel-ConversationTime-TurnTime)/1.6f);}
            }
            return f;
        }
        public float LockerOpen(float time)
        {
            float t=Mathf.Repeat(time,Duration),arrival=Arrival;
            return Ease((t-arrival+2f)/1.2f)*(1f-Ease((t-arrival-DropTime-.3f)/.8f));
        }
        public static Vector3 CrateCenter(float lift){return new Vector3(0,Mathf.Lerp(.29f,.94f,lift),Mathf.Lerp(.50f,.35f,lift));}
    }
}
