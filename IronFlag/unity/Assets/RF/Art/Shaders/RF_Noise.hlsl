#ifndef RF_NOISE_INCLUDED
#define RF_NOISE_INCLUDED

// The one source of unevenness in the game, and it has nothing behind it.
//
// No texture, and not because a texture would be cheating - the models come out of Blender
// and the sounds out of SuperCollider, so a generated tiling normal map would have been
// perfectly in keeping. It is that there is nothing for one to tile against. A map is 240 m
// across and the detail on it wants to be measured in tens of centimetres, so a texture
// would repeat some tens of times across the frame and the repeat is the thing everyone
// sees. Two octaves of value noise on the world position cost less than the sample would
// and cannot be laid at the wrong scale, because the coordinate is metres.
//
// Split out from RF_Surface.hlsl so that RF_Mark.shader - which is unlit, and wants only
// the noise - does not have to pull URP's whole lighting library in to eat the edge off a
// scorch mark.

// One number per lattice point, repeatable.
//
// The fract-of-a-big-sine hash, which bands on hardware that computes sin badly and is fine
// here for the same reason the noise is untextured: nothing on this map is seen from closer
// than about eight metres, and every use of it perturbs a normal or nibbles an edge rather
// than producing a colour anybody reads a value off.
float RfHash(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453123);
}

// Value noise in 0..1, smooth across cell boundaries.
float RfNoise(float2 at)
{
    float2 cell = floor(at);
    float2 into = frac(at);
    float2 fade = into * into * (3.0 - (2.0 * into));

    float southWest = RfHash(cell);
    float southEast = RfHash(cell + float2(1.0, 0.0));
    float northWest = RfHash(cell + float2(0.0, 1.0));
    float northEast = RfHash(cell + float2(1.0, 1.0));

    return lerp(
        lerp(southWest, southEast, fade.x),
        lerp(northWest, northEast, fade.x),
        fade.y);
}

// Two octaves of it: the coarse one is what the eye reads as unevenness, the fine one is
// what stops the coarse one reading as a grid of blobs.
float RfGrain(float2 at)
{
    return (RfNoise(at) * 0.65) + (RfNoise((at * 2.17) + 19.3) * 0.35);
}

#endif
