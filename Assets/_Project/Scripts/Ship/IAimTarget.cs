using UnityEngine;

namespace SkyTide
{
    /// <summary>Цель прицела флагмана: любой корабль — враг (TargetShip) или свой (AllyShip). Владелец 16.11: «чтобы я мог
    /// любой корабль при желании брать в таргет».</summary>
    public interface IAimTarget
    {
        Transform transform { get; }
        GameObject gameObject { get; }
        bool Alive { get; }
        Vector3 Center { get; }
        Vector3 Velocity { get; }
        float Health01 { get; }
        bool TryHit(Vector3 a, Vector3 b, out Vector3 point);
        Vector3 BoxCorner(int i);
    }
}
