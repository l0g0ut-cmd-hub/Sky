using UnityEngine;

namespace SkyTide
{
    public static class ShipDeckDressing
    {
        static Mesh barrel,rope;
        public static readonly Vector3[] Barrels={new Vector3(-3.75f,ShipCrew.MainDeck,-3.5f),new Vector3(-4.65f,ShipCrew.MainDeck,-3.5f),
            new Vector3(4.4f,ShipCrew.MainDeck,-3.6f),new Vector3(5.25f,ShipCrew.MainDeck,-3.6f)};
        public static void Build(Transform visual,ShipVariant variant)
        {
            if(variant==null||variant.crew==null||variant.materials.Length<7)return;
            if(barrel==null)barrel=CrewParts.StavedBarrel();if(rope==null)rope=CrewParts.SpareRope();
            Transform group=new GameObject("Deck supplies").transform;group.SetParent(visual,false);
            foreach(Vector3 p in Barrels)CrewParts.Object("Iron-bound supply barrel",group,barrel,variant.materials[5],variant.materials[1],p);
            foreach(Vector3 p in new[]{new Vector3(-3.5f,ShipCrew.MainDeck+.008f,-5.2f),new Vector3(5.25f,ShipCrew.PlatformTop+.008f,-2.25f)})
                CrewParts.Object("Spare rope coil",group,rope,variant.materials[2],variant.materials[2],p);
        }
    }
}
