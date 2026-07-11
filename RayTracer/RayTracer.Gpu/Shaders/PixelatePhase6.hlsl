// Retro pixelation post-pass (plan §1.5 / §2.3). Runs in place on the resolved Output image
// after the resolve pass: each pixel is replaced by the pixel at the centre of its
// BlockSize×BlockSize cell, so the beauty reads as a chunky low-resolution nearest-neighbour
// upscale — the 90s "3D Maze" pixel look — with sub-block antialiasing collapsed away.
//
// The in-place read/write is race-free: every pixel in a block reads the *same* block-centre
// pixel T (the clamp at the image edge collapses a partial block to one shared, in-bounds T),
// and T's own thread reads and writes T unchanged. So T is invariant for the whole dispatch
// and no thread ever writes a pixel another thread reads. The host only dispatches this when
// BlockSize > 1; otherwise Output is left bit-identical to the resolve result.
RWTexture2D<float4> Output : register(u3);

cbuffer PixelateConstants : register(b0)
{
    uint Width;
    uint Height;
    uint BlockSize;
};

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Width || tid.y >= Height)
        return;

    uint cx = min((tid.x / BlockSize) * BlockSize + BlockSize / 2u, Width - 1u);
    uint cy = min((tid.y / BlockSize) * BlockSize + BlockSize / 2u, Height - 1u);
    Output[tid.xy] = Output[uint2(cx, cy)];
}
