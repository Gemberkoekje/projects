# The M1.11c output finish (vista-plan section 10, M1.11c) on the local GPU, against a main build (-Before, M1.11b's Vista.App.dll):
#  1. Clipping: six views rendered by main (M1.11b's punchy look, clamped) and by this build with --dither off (the saturation held
#     back), from the same EXR. Per view: the share of pixels with a channel at 0, the share that changed (every one must have had a
#     channel at 0 under main: only clipped colours move), and over those the Oklab hue's mean distance from AgX's base look.
#  2. The old looks are unchanged: --look base --dither off on the four measurement views, PNG and EXR byte-identical to main's.
#  3. Banding: the sky view at -SkyWidth (3840x2160), denoised, the dither off and on from one render: the longest run of equal
#     8-bit values down five sky columns, and a loupe of the sky, plain and contrast-stretched.
#  4. 16 bits: the default view with --png16, read back by GDI+ (an independent decoder), within a step of the 8-bit PNG.
# It writes the sheets to commit, small (cells of -CellWidth): before-after.png (main against the new defaults) and
# sky-loupe.png. Build first: dotnet build Vista.App -c Release (from RayTracer/), and main's the same way.
param(
    [Parameter(Mandatory = $true)][string]$Before,
    [string]$Out = (Join-Path $env:TEMP 'vista-finish'),
    [string]$App = '',
    [int]$Width = 480,
    [int]$CellWidth = 320,
    [int]$Spp = 64,
    [int]$SkyWidth = 3840,
    [int]$SkySpp = 1024,
    [string]$DenoiseDevice = 'cuda',
    [int]$LoupeWidth = 400
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when a mandatory parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Finish
{
    // A PNG as R, G, B bytes, top row first, through GDI+ (which also reads 16-bit PNGs, to 8 bits).
    public static byte[] Rgb(string path, out int width, out int height)
    {
        using (var bitmap = new Bitmap(path))
        {
            width = bitmap.Width;
            height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var raw = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            bitmap.UnlockBits(data);
            var rgb = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 3; c++)
                        rgb[(y * width + x) * 3 + c] = raw[y * data.Stride + x * 3 + 2 - c];
            return rgb;
        }
    }

    public static void Save(string path, byte[] rgb, int width, int height)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var raw = new byte[data.Stride * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 3; c++)
                        raw[y * data.Stride + x * 3 + 2 - c] = rgb[(y * width + x) * 3 + c];
            Marshal.Copy(raw, 0, data.Scan0, raw.Length);
            bitmap.UnlockBits(data);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    static bool Zero(byte[] rgb, int p) { return rgb[p] == 0 || rgb[p + 1] == 0 || rgb[p + 2] == 0; }

    public static double ZeroShare(byte[] rgb)
    {
        int n = 0;
        for (int p = 0; p < rgb.Length; p += 3) if (Zero(rgb, p)) n++;
        return 3.0 * n / rgb.Length;
    }

    // Pixels that differ, and of those how many had no channel at 0 in before (must be none: only clipped colours move).
    public static int[] Changed(byte[] before, byte[] after)
    {
        int changed = 0, unclipped = 0;
        for (int p = 0; p < before.Length; p += 3)
        {
            if (before[p] == after[p] && before[p + 1] == after[p + 1] && before[p + 2] == after[p + 2]) continue;
            changed++;
            if (!Zero(before, p)) unclipped++;
        }
        return new[] { changed, unclipped };
    }

    static double Linear(byte b)
    {
        double v = b / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    // Oklab (Ottosson 2020): lightness, chroma and hue in degrees.
    static double[] Oklab(byte[] rgb, int p)
    {
        double r = Linear(rgb[p]), g = Linear(rgb[p + 1]), b = Linear(rgb[p + 2]);
        double l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1.0 / 3.0);
        double m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1.0 / 3.0);
        double s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1.0 / 3.0);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return new[] { L, Math.Sqrt(A * A + B * B), Math.Atan2(B, A) * 180.0 / Math.PI };
    }

    // The mean distance of image's Oklab hue from reference's, over the pixels with a channel at 0 in mask (the colours main
    // clipped) that are coloured enough for a hue (chroma over 0.03, lightness over 0.1 in both). Returns { mean, pixels }.
    public static double[] HueShift(byte[] reference, byte[] image, byte[] mask)
    {
        double sum = 0.0;
        int n = 0;
        for (int p = 0; p < mask.Length; p += 3)
        {
            if (!Zero(mask, p)) continue;
            double[] a = Oklab(reference, p), b = Oklab(image, p);
            if (a[1] < 0.03 || b[1] < 0.03 || a[0] < 0.1 || b[0] < 0.1) continue;
            double d = Math.Abs(b[2] - a[2]);
            if (d > 180.0) d = 360.0 - d;
            sum += d;
            n++;
        }
        return new[] { n > 0 ? sum / n : 0.0, n };
    }

    // The frame's mean of each pixel's largest channel, and its mean saturation (max - min) / max.
    public static double[] Tones(byte[] rgb)
    {
        double sum = 0.0, saturation = 0.0;
        for (int p = 0; p < rgb.Length; p += 3)
        {
            int max = Math.Max(rgb[p], Math.Max(rgb[p + 1], rgb[p + 2])), min = Math.Min(rgb[p], Math.Min(rgb[p + 1], rgb[p + 2]));
            sum += max;
            if (max > 0) saturation += (max - min) / (double)max;
        }
        int pixels = rgb.Length / 3;
        return new[] { sum / pixels, saturation / pixels };
    }

    // The longest run of equal values down column x in channel c, and how many distinct values the column holds.
    public static int[] Run(byte[] rgb, int width, int height, int x, int c)
    {
        int longest = 1, run = 1;
        var seen = new bool[256];
        int distinct = 0;
        for (int y = 0; y < height; y++)
        {
            byte v = rgb[(y * width + x) * 3 + c];
            if (!seen[v]) { seen[v] = true; distinct++; }
            if (y == 0) continue;
            run = v == rgb[((y - 1) * width + x) * 3 + c] ? run + 1 : 1;
            if (run > longest) longest = run;
        }
        return new[] { longest, distinct };
    }

    public static byte[] Crop(byte[] rgb, int width, int x0, int y0, int w, int h)
    {
        var crop = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            Array.Copy(rgb, ((y0 + y) * width + x0) * 3, crop, y * w * 3, w * 3);
        return crop;
    }

    // Each channel stretched by the same gain about the reference crop's mean, so a step of one level becomes gain levels.
    public static byte[] Stretch(byte[] rgb, byte[] reference, double gain)
    {
        var mean = new double[3];
        for (int p = 0; p < reference.Length; p++) mean[p % 3] += reference[p];
        for (int c = 0; c < 3; c++) mean[c] /= reference.Length / 3;
        var stretched = new byte[rgb.Length];
        for (int p = 0; p < rgb.Length; p++)
            stretched[p] = (byte)Math.Max(0, Math.Min(255, Math.Round(128 + gain * (rgb[p] - mean[p % 3]))));
        return stretched;
    }

    public static int Max(byte[] a, byte[] b)
    {
        int worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return worst;
    }
}
'@

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$october = Join-Path $views 'atmosphere\zermatt-october.recipe.json'
$ridges = Join-Path $views 'aerial\layered-ridges.camera.json'
$list = @(
    @{ Name = 'sky'; View = @('--recipe', $day, '--camera', (Join-Path $views 'sky.camera.json')) },
    @{ Name = 'aerial'; View = @('--recipe', $day, '--camera', (Join-Path $views 'aerial.camera.json')) },
    @{ Name = 'default'; View = @('--recipe', $day, '--camera', (Join-Path $views 'default.camera.json')) },
    @{ Name = 'down-slope'; View = @('--recipe', $golden, '--camera', (Join-Path $views 'down-slope.camera.json')) },
    @{ Name = 'default-golden'; View = @('--recipe', $golden, '--camera', (Join-Path $views 'default.camera.json')) },
    @{ Name = 'ridges-october'; View = @('--recipe', $october, '--camera', $ridges, '--sun-elevation', '2') }
)

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Hash([string]$path) { (Get-FileHash -Algorithm SHA256 $path).Hash }

function Read([string]$png) {
    $w = 0; $h = 0
    $rgb = [Finish]::Rgb($png, [ref]$w, [ref]$h)
    return @{ Rgb = $rgb; Width = $w; Height = $h }
}

function Preview([string]$dll, [string]$png, [string[]]$view, [string[]]$extra, [int]$width) {
    Run $dll (@('--preview', '--seed', '42', '--width', $width, '--spp', $Spp, '--out', $png) + $view + $extra)
}

'1. Clipping: main (M1.11b) against this build with --dither off, one EXR per view'
'   view             zero channel main / now   changed (unclipped)   hue from base main / now (pixels)   mean, saturation main / now'
foreach ($v in $list) {
    $main = Join-Path $Out "clip-$($v.Name)-main.png"
    $now = Join-Path $Out "clip-$($v.Name)-now.png"
    $base = Join-Path $Out "clip-$($v.Name)-base.png"
    Preview $Before $main $v.View @() $Width
    Preview $App $now $v.View @('--dither', 'off') $Width
    Preview $App $base $v.View @('--dither', 'off', '--look', 'base') $Width
    $exrs = @($main, $now, $base) | ForEach-Object { Hash ([IO.Path]::ChangeExtension($_, '.exr')) } | Select-Object -Unique
    if (@($exrs).Count -ne 1) { throw "$($v.Name): the renders differ." }
    $m = Read $main; $n = Read $now; $b = Read $base
    $changed = [Finish]::Changed($m.Rgb, $n.Rgb)
    if ($changed[1] -ne 0) { throw "$($v.Name): $($changed[1]) pixels changed that main did not clip." }
    $hueMain = [Finish]::HueShift($b.Rgb, $m.Rgb, $m.Rgb)
    $hueNow = [Finish]::HueShift($b.Rgb, $n.Rgb, $m.Rgb)
    $tm = [Finish]::Tones($m.Rgb); $tn = [Finish]::Tones($n.Rgb)
    $pixels = $m.Width * $m.Height
    '   {0,-16} {1,6:P1} / {2,6:P1}      {3,6:P1} ({4})          {5,5:0.0} / {6,5:0.0} deg ({7})        {8,5:0.0}, {9:0.00} / {10,5:0.0}, {11:0.00}' -f
        $v.Name, [Finish]::ZeroShare($m.Rgb), [Finish]::ZeroShare($n.Rgb), ($changed[0] / $pixels), $changed[1], $hueMain[0], $hueNow[0], $hueMain[1],
        $tm[0], $tm[1], $tn[0], $tn[1]
}

'2. The old looks: --look base --dither off on the four measurement views, against main''s --look base'
foreach ($v in $list | Select-Object -First 4) {
    $main = Join-Path $Out "base-$($v.Name)-main.png"
    $now = Join-Path $Out "base-$($v.Name)-now.png"
    Preview $Before $main $v.View @('--look', 'base') $Width
    Preview $App $now $v.View @('--look', 'base', '--dither', 'off') $Width
    foreach ($ext in '.png', '.exr') {
        if ((Hash ([IO.Path]::ChangeExtension($main, $ext))) -ne (Hash ([IO.Path]::ChangeExtension($now, $ext)))) { throw "$($v.Name): $ext differs from main's." }
    }
    '   {0,-14} PNG and EXR byte-identical' -f $v.Name
}

"3. Banding: the sky view at $SkyWidth px, $SkySpp spp, denoised ($DenoiseDevice), the dither off and on from one render"
$sky = Join-Path $Out 'sky4k.png'
Run $App (@('--sheet', '--seed', '42', '--width', $SkyWidth, '--spp', $SkySpp, '--denoise', '--denoise-device', $DenoiseDevice,
            '--vary', 'dither=off,on', '--columns', '2', '--out', $sky) + $list[0].View)
$cells = Join-Path $Out 'sky4k.cells'
if ((Hash (Join-Path $cells 'cell01.denoised.exr')) -ne (Hash (Join-Path $cells 'cell02.denoised.exr'))) { throw 'The two cells were denoised differently.' }
$off = Read (Join-Path $cells 'cell01.denoised.png')
$on = Read (Join-Path $cells 'cell02.denoised.png')
$s1 = Get-Content (Join-Path $cells 'cell01.json') -Raw | ConvertFrom-Json
$s2 = Get-Content (Join-Path $cells 'cell02.json') -Raw | ConvertFrom-Json
'   write (PNG + EXR, beauty and denoised): {0:0.00} s off, {1:0.00} s on; render {2:0.0} s, denoise {3:0.0} s' -f
    [double]$s1.timingsSeconds.write, [double]$s2.timingsSeconds.write, [double]$s1.timingsSeconds.render, [double]$s1.timingsSeconds.denoise
'   largest change the dither made: {0} step(s)' -f [Finish]::Max($off.Rgb, $on.Rgb)
'   column   longest run of equal values (R, G, B), off -> on      distinct values (G), off -> on'
foreach ($x in @(0.125, 0.375, 0.5, 0.625, 0.875)) {
    $col = [int]($x * ($off.Width - 1))
    $runs = foreach ($c in 0, 1, 2) { @([Finish]::Run($off.Rgb, $off.Width, $off.Height, $col, $c)[0], [Finish]::Run($on.Rgb, $on.Width, $on.Height, $col, $c)[0]) }
    $g0 = [Finish]::Run($off.Rgb, $off.Width, $off.Height, $col, 1); $g1 = [Finish]::Run($on.Rgb, $on.Width, $on.Height, $col, 1)
    '   {0,6}   {1,4}, {2,4}, {3,4} -> {4,3}, {5,3}, {6,3}                        {7} -> {8}' -f $col, $runs[0], $runs[2], $runs[4], $runs[1], $runs[3], $runs[5], $g0[1], $g1[1]
}

# The loupe: the middle of the frame's top half, LoupeWidth at 16:9, plain and stretched 16 times about the undithered crop's mean.
$lw = $LoupeWidth; $lh = [int]($LoupeWidth * 9 / 16)
$x0 = [int](($off.Width - $lw) / 2); $y0 = [int]($off.Height / 4 - $lh / 2)
$cropOff = [Finish]::Crop($off.Rgb, $off.Width, $x0, $y0, $lw, $lh)
$cropOn = [Finish]::Crop($on.Rgb, $on.Width, $x0, $y0, $lw, $lh)
# (--labels splits at commas, so the labels have none.)
$loupes = @(
    @{ File = 'loupe-off.png'; Rgb = $cropOff; Label = 'dither off' },
    @{ File = 'loupe-on.png'; Rgb = $cropOn; Label = 'dither on' },
    @{ File = 'loupe-off-x16.png'; Rgb = [Finish]::Stretch($cropOff, $cropOff, 16.0); Label = 'dither off: contrast x16' },
    @{ File = 'loupe-on-x16.png'; Rgb = [Finish]::Stretch($cropOn, $cropOff, 16.0); Label = 'dither on: contrast x16' }
)
foreach ($l in $loupes) { [Finish]::Save((Join-Path $Out $l.File), $l.Rgb, $lw, $lh) }
Run $App (@('--sheet-from') + ($loupes | ForEach-Object { Join-Path $Out $_.File }) + @('--labels', (($loupes | ForEach-Object { $_.Label }) -join ','),
           '--columns', '2', '--title', "the denoised sky at ${SkyWidth}x$([int]($SkyWidth * 9 / 16)), loupe ${lw}x$lh at $x0,$y0", '--out', (Join-Path $Out 'sky-loupe.png')))

'4. 16 bits: the default view with --png16 against --dither off, read by GDI+'
$wide = Join-Path $Out 'png16-default.png'
$narrow = Join-Path $Out 'png16-default-8.png'
Preview $App $wide $list[2].View @('--png16') $Width
Preview $App $narrow $list[2].View @('--dither', 'off') $Width
$w16 = Read $wide; $w8 = Read $narrow
$side = Get-Content ([IO.Path]::ChangeExtension($wide, '.json')) -Raw | ConvertFrom-Json
'   GDI+ reads it ({0}x{1}); largest difference from the 8-bit PNG {2} step(s); sidecar bitDepth {3}, dither {4}; {5:0} KB against {6:0} KB' -f
    $w16.Width, $w16.Height, [Finish]::Max($w16.Rgb, $w8.Rgb), $side.image.bitDepth, $side.image.dither, ((Get-Item $wide).Length / 1KB), ((Get-Item $narrow).Length / 1KB)

'Before/after at the cell width, for the committed sheet: main (M1.11b) against the new defaults:'
$pairs = @(); $labels = @()
foreach ($v in $list) {
    $old = Join-Path $Out "small-$($v.Name)-before.png"
    $new = Join-Path $Out "small-$($v.Name)-after.png"
    Preview $Before $old $v.View @() $CellWidth
    Preview $App $new $v.View @() $CellWidth
    if ((Hash ([IO.Path]::ChangeExtension($old, '.exr'))) -ne (Hash ([IO.Path]::ChangeExtension($new, '.exr')))) { throw "$($v.Name): the before and after EXRs differ." }
    $pairs += $old, $new
    $labels += "M1.11b: $($v.Name)", "M1.11c: $($v.Name)"
}
Run $App (@('--sheet-from') + $pairs + @('--labels', ($labels -join ','), '--columns', '2',
           '--title', 'left: M1.11b (punchy clamped, no dither) | right: M1.11c (saturation held back, dithered)', '--out', (Join-Path $Out 'before-after.png')))
"Sheets in $Out"
