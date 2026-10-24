// Vista terrain erosion (vista-plan §4.3, §10 M1.7): virtual-pipe water flow with sediment transport and deposition, plus
// thermal erosion to a talus angle, over two layers (bedrock and loose material). One step is five passes:
//   CSFlux           rain and the outflow to the four neighbours                    (reads Terrain, Water; Flux in place)
//   CSWater          water, discharge, erosion or deposition, evaporation, flow sums (Terrain, Water, Sediment, Flux → the B side)
//   CSTransport      suspended sediment moves with the water                         (Water of the step, Flux, Sediment B → A)
//   CSThermalOut     how much loose material and rock leaves each cell               (Terrain B → Thermal)
//   CSThermalApply   each cell loses what it sent, gains its share of its neighbours' (Terrain B, Thermal → Terrain A)
// Since M1.11d the rock has strata (Vista.World.RockHardness): CSWater's rock erodibility and CSThermalOut's rock talus follow
// the hardness of the rock at its current height, looked up in the bed table as the reference does (since M1.12c, faded toward
// massive rock's where the cell's rock is massive). Plus CSClear, which
// zeroes a buffer. Each thread computes one cell and writes only it (gather-only, no atomics), so a run
// is bit-identical every time on one driver. Every buffer is a UAV: passes are separated by UAV barriers, never state
// transitions. The C# reference is Vista.World.ErosionReference, mirrored line for line; amounts that move between layers are
// made exactly representable first, and `precise` keeps the compiler from folding that away.

cbuffer ErosionConstants : register(b0)
{
    uint  Size;             // samples per side
    uint  Accumulate;       // 1 when this step adds to the flow sums
    uint  ClearCount;       // 16-byte words CSClear zeroes
    uint  _pad1;
    float Cell;
    float InvCell;
    float CellArea;
    float TimeStep;
    float FluxPerHead;      // Δt·A·g / l
    float FluxKept;         // 1 − damping
    float DepthPerFlux;     // Δt / l²
    float Rain;
    float Retain;           // 1 − evaporation
    float Capacity;
    float MinSlope;
    float DischargeExponent;
    float Dissolve;
    float RockErodibility;
    float Deposition;
    float HalfThermalRate;
    float LooseDropEdge;
    float LooseDropCorner;
    float RockDropEdge;
    float RockDropCorner;
    float RockErodibilityHard;
    float RockDropEdgeHard;
    float RockDropCornerHard;
    float TableScale;       // bed-table entries per metre of rock height
    float TableLast;        // the largest table coordinate a lookup reads
    uint  _pad2;
    uint  _pad3;
    uint  _pad4;
};

RWStructuredBuffer<float2> TerrainIn   : register(u0); // (bedrock, loose)
RWStructuredBuffer<float>  WaterIn     : register(u1);
RWStructuredBuffer<float>  SedimentIn  : register(u2);
RWStructuredBuffer<float4> Flux        : register(u3); // outflow to −x, +x, −z, +z in m³/s
RWStructuredBuffer<float2> TerrainOut  : register(u4);
RWStructuredBuffer<float>  WaterOut    : register(u5);
RWStructuredBuffer<float>  SedimentOut : register(u6);
RWStructuredBuffer<float2> Flow        : register(u7); // sums of (outflow, water depth)
RWStructuredBuffer<float4> Thermal     : register(u8); // (loose moved, rock moved, sum of the loose excess, 0)
RWByteAddressBuffer        ClearTarget : register(u9);
RWStructuredBuffer<float3> Strata      : register(u10); // per cell (bed-table coordinate at height 0, mottle, massive)
RWStructuredBuffer<float2> Bands       : register(u11); // the bed tables by stratigraphic height: (a bed's hardness, massive rock's)

float Ground(uint c)
{
    float2 t = TerrainIn[c];
    return t.x + t.y;
}

// The hardness of the rock at height `rock` in cell c: the bed table interpolated at the rock's table coordinate, moved toward
// massive rock's by how massive the cell's rock is (M1.12c; exactly the bed where it is not), plus the mottle.
float Hardness(uint c, float rock)
{
    float3 s = Strata[c];
    precise float scaled = rock * TableScale;
    precise float t = clamp(scaled + s.x, 0.0, TableLast);
    precise float b = floor(t);
    uint k = uint(b);
    precise float f = t - b;
    float2 lo = Bands[k], hi = Bands[k + 1u];
    precise float bed = lo.x + (hi.x - lo.x) * f;
    precise float massive = lo.y + (hi.y - lo.y) * f;
    precise float mixed = bed + (massive - bed) * s.z;
    return clamp(mixed + s.y, 0.0, 1.0);
}

// The eight neighbours, in the order both sides sum them.
static const int NeighbourI[8] = { -1, 0, 1, -1, 1, -1, 0, 1 };
static const int NeighbourJ[8] = { -1, -1, -1, 0, 0, 1, 1, 1 };

[numthreads(8, 8, 1)]
void CSFlux(uint3 tid : SV_DispatchThreadID)
{
    uint i = tid.x, j = tid.y, n = Size;
    if (i >= n || j >= n)
        return;
    uint c = j * n + i;
    uint west = i > 0u ? c - 1u : c + 1u, east = i < n - 1u ? c + 1u : c - 1u;
    uint south = j > 0u ? c - n : c + n, north = j < n - 1u ? c + n : c - n;
    float ground = Ground(c);
    float surface = ground + (WaterIn[c] + Rain);
    // Past the edge the ground continues at the edge's slope and is dry. (Both sides of ?: are evaluated, so every index
    // read is in the grid: past an edge, the neighbour index names the cell opposite.)
    float gw = Ground(west), ge = Ground(east), gs = Ground(south), gn = Ground(north);
    float xm = i > 0u ? gw + (WaterIn[west] + Rain) : ground + (ground - ge);
    float xp = i < n - 1u ? ge + (WaterIn[east] + Rain) : ground + (ground - gw);
    float zm = j > 0u ? gs + (WaterIn[south] + Rain) : ground + (ground - gn);
    float zp = j < n - 1u ? gn + (WaterIn[north] + Rain) : ground + (ground - gs);
    float4 f = Flux[c];
    float fxm = max(0.0, f.x * FluxKept + FluxPerHead * (surface - xm));
    float fxp = max(0.0, f.y * FluxKept + FluxPerHead * (surface - xp));
    float fzm = max(0.0, f.z * FluxKept + FluxPerHead * (surface - zm));
    float fzp = max(0.0, f.w * FluxKept + FluxPerHead * (surface - zp));
    float leaving = ((fxm + fxp) + (fzm + fzp)) * TimeStep;
    float volume = (WaterIn[c] + Rain) * CellArea;
    float scale = leaving > volume ? volume / leaving : 1.0;
    Flux[c] = float4(fxm * scale, fxp * scale, fzm * scale, fzp * scale);
}

// q^e, with the exponents the profiles use done exactly, as the reference does them.
float DischargeTerm(float q)
{
    return DischargeExponent == 1.0 ? q : DischargeExponent == 0.5 ? sqrt(q) : pow(q, DischargeExponent);
}

[numthreads(8, 8, 1)]
void CSWater(uint3 tid : SV_DispatchThreadID)
{
    uint i = tid.x, j = tid.y, n = Size;
    if (i >= n || j >= n)
        return;
    uint c = j * n + i;
    // Neighbour indices clamped into the grid (both sides of ?: are evaluated).
    uint west = i > 0u ? c - 1u : c, east = i < n - 1u ? c + 1u : c;
    uint south = j > 0u ? c - n : c, north = j < n - 1u ? c + n : c;
    float2 t = TerrainIn[c];
    precise float rock = t.x;
    precise float loose = t.y;
    float ground = rock + loose;
    float before = WaterIn[c] + Rain;

    float4 f = Flux[c];
    float fromXm = i > 0u ? Flux[west].y : 0.0;         // the west neighbour's flow east
    float fromXp = i < n - 1u ? Flux[east].x : 0.0;     // the east neighbour's flow west
    float fromZm = j > 0u ? Flux[south].w : 0.0;
    float fromZp = j < n - 1u ? Flux[north].z : 0.0;
    float inflow = (fromXm + fromXp) + (fromZm + fromZp);
    float outflow = (f.x + f.y) + (f.z + f.w);
    float after = max(0.0, before + DepthPerFlux * (inflow - outflow));

    // The net flow through the cell, as discharge per metre of width (m²/s).
    float wx = 0.5 * ((fromXm - f.x) + (f.y - fromXp));
    float wz = 0.5 * ((fromZm - f.z) + (f.w - fromZp));
    float discharge = sqrt(wx * wx + wz * wz) * InvCell;

    // The ground's slope: central differences inside, one-sided at the edges.
    float gxm = Ground(west);
    float gxp = Ground(east);
    float gzm = Ground(south);
    float gzp = Ground(north);
    float gx = (gxp - gxm) * (i > 0u && i < n - 1u ? 0.5 * InvCell : InvCell);
    float gz = (gzp - gzm) * (j > 0u && j < n - 1u ? 0.5 * InvCell : InvCell);
    float g2 = gx * gx + gz * gz;
    float sine = sqrt(g2 / (1.0 + g2));
    float capacity = Capacity * max(sine, MinSlope) * DischargeTerm(discharge);

    // A cell never cuts below its lowest edge neighbour nor fills above its highest in one step.
    float lowest = min(min(gxm, gxp), min(gzm, gzp));
    float highest = max(max(gxm, gxp), max(gzm, gzp));
    precise float carried = SedimentIn[c];
    if (capacity > carried)
    {
        float demand = min(Dissolve * (capacity - carried), max(0.0, ground - lowest));
        precise float looseLeft = loose - min(demand, loose);
        precise float fromLoose = loose - looseLeft;
        float hardness = Hardness(c, rock);
        precise float erodibility = RockErodibility + (RockErodibilityHard - RockErodibility) * hardness;
        precise float rockLeft = rock - (demand - fromLoose) * erodibility;
        precise float fromRock = rock - rockLeft;
        loose = looseLeft;
        rock = rockLeft;
        carried += fromLoose + fromRock;
    }
    else
    {
        precise float looseAfter = loose + min(Deposition * (carried - capacity), max(0.0, highest - ground));
        precise float dropped = looseAfter - loose;
        loose = looseAfter;
        carried = max(0.0, carried - dropped);
    }

    TerrainOut[c] = float2(rock, loose);
    SedimentOut[c] = carried;
    WaterOut[c] = after * Retain;
    if (Accumulate != 0u)
        Flow[c] = Flow[c] + float2(outflow, after);
}

// The fraction of cell c's water this step sent toward one neighbour, per m³/s of that outflow.
float Share(uint c)
{
    float before = WaterIn[c] + Rain;
    return before > 0.0 ? DepthPerFlux / before : 0.0;
}

[numthreads(8, 8, 1)]
void CSTransport(uint3 tid : SV_DispatchThreadID)
{
    uint i = tid.x, j = tid.y, n = Size;
    if (i >= n || j >= n)
        return;
    uint c = j * n + i;
    uint west = i > 0u ? c - 1u : c, east = i < n - 1u ? c + 1u : c;
    uint south = j > 0u ? c - n : c, north = j < n - 1u ? c + n : c;
    float4 f = Flux[c];
    float outflow = (f.x + f.y) + (f.z + f.w);
    float leaving = SedimentIn[c] * (outflow * Share(c));
    float fromXm = i > 0u ? SedimentIn[west] * (Flux[west].y * Share(west)) : 0.0;
    float fromXp = i < n - 1u ? SedimentIn[east] * (Flux[east].x * Share(east)) : 0.0;
    float fromZm = j > 0u ? SedimentIn[south] * (Flux[south].w * Share(south)) : 0.0;
    float fromZp = j < n - 1u ? SedimentIn[north] * (Flux[north].z * Share(north)) : 0.0;
    SedimentOut[c] = max(0.0, (SedimentIn[c] - leaving) + ((fromXm + fromXp) + (fromZm + fromZp)));
}

[numthreads(8, 8, 1)]
void CSThermalOut(uint3 tid : SV_DispatchThreadID)
{
    uint i = tid.x, j = tid.y, n = Size;
    if (i >= n || j >= n)
        return;
    uint c = j * n + i;
    float2 t = TerrainIn[c];
    precise float rock = t.x;
    precise float loose = t.y;
    float ground = rock + loose;
    // Harder rock stands steeper: the drops it holds follow its hardness.
    float hardness = Hardness(c, rock);
    precise float rockEdge = RockDropEdge + (RockDropEdgeHard - RockDropEdge) * hardness;
    precise float rockCorner = RockDropCorner + (RockDropCornerHard - RockDropCorner) * hardness;
    float maxLoose = 0.0, maxRock = 0.0, sum = 0.0;
    [unroll]
    for (uint q = 0u; q < 8u; q++)
    {
        int ni = int(i) + NeighbourI[q], nj = int(j) + NeighbourJ[q];
        // The index stays in the grid even for a neighbour past the edge, in case the compiler reads before it branches.
        uint m = uint(clamp(nj, 0, int(n) - 1)) * n + uint(clamp(ni, 0, int(n) - 1));
        if (ni < 0 || ni >= int(n) || nj < 0 || nj >= int(n))
            continue;
        bool corner = NeighbourI[q] != 0 && NeighbourJ[q] != 0;
        precise float drop = ground - Ground(m);
        precise float overLoose = max(0.0, drop - (corner ? LooseDropCorner : LooseDropEdge));
        float overRock = max(0.0, drop - (corner ? rockCorner : rockEdge));
        maxLoose = max(maxLoose, overLoose);
        maxRock = max(maxRock, overRock);
        sum += overLoose;
    }
    precise float slide = min(loose, HalfThermalRate * maxLoose);
    precise float collapse = max(0.0, HalfThermalRate * maxRock - slide);
    precise float slid = loose - (loose - slide);
    precise float collapsed = rock - (rock - collapse);
    Thermal[c] = float4(slid, collapsed, sum, 0.0);
}

[numthreads(8, 8, 1)]
void CSThermalApply(uint3 tid : SV_DispatchThreadID)
{
    uint i = tid.x, j = tid.y, n = Size;
    if (i >= n || j >= n)
        return;
    uint c = j * n + i;
    float2 t = TerrainIn[c];
    float ground = t.x + t.y;
    float gained = 0.0;
    [unroll]
    for (uint q = 0u; q < 8u; q++)
    {
        int ni = int(i) + NeighbourI[q], nj = int(j) + NeighbourJ[q];
        uint m = uint(clamp(nj, 0, int(n) - 1)) * n + uint(clamp(ni, 0, int(n) - 1));
        if (ni < 0 || ni >= int(n) || nj < 0 || nj >= int(n))
            continue;
        float4 sender = Thermal[m];
        float sent = sender.x + sender.y;
        if (!(sent > 0.0 && sender.z > 0.0))
            continue;
        bool corner = NeighbourI[q] != 0 && NeighbourJ[q] != 0;
        // The neighbour's excess toward this cell, computed exactly as the neighbour computed it.
        precise float drop = Ground(m) - ground;
        precise float over = max(0.0, drop - (corner ? LooseDropCorner : LooseDropEdge));
        gained += sent * (over / sender.z);
    }
    float4 own = Thermal[c];
    TerrainOut[c] = float2(t.x - own.y, (t.y - own.x) + gained);
}

[numthreads(64, 1, 1)]
void CSClear(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x < ClearCount)
        ClearTarget.Store4(tid.x * 16u, uint4(0u, 0u, 0u, 0u));
}
