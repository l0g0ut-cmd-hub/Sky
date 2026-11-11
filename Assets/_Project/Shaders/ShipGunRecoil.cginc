#ifndef SKYTIDE_GUN_RECOIL_INCLUDED
#define SKYTIDE_GUN_RECOIL_INCLUDED
// Per-renderer local translations; rigid barrels retain their original normal/tangent basis.
float4 _ShipGunOffsets[16];
void SkyTideGunVertex(inout float4 vertex, float4 color)
{
    int id = (int)round(color.a * 255.0) - 1;
    if (id >= 0 && id < 16) vertex.xyz += _ShipGunOffsets[id].xyz;
}
#endif
