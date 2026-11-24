namespace SandcastleRpgMaker.AI;

/// <summary>How a drawn picture becomes a game sprite.</summary>
/// <param name="Height">The sprite's height in pixels (its width follows the picture's shape).</param>
/// <param name="Colours">How many colours at most (0: as many as it has).</param>
/// <param name="Palette">A fixed palette (RGB triples) - every pixel takes the nearest; null: chosen from the picture (Colours).</param>
/// <param name="BackgroundTolerance">How far from the border's colour still counts as background (0-255; 0: keep the background).</param>
/// <param name="Outline">A one-pixel dark outline round the sprite, as old games drew them.</param>
/// <param name="KeepBiggest">Keep only the biggest figure (the AI sometimes draws two or three side by side).</param>
public sealed record SpriteOptions(int Height = 48, int Colours = 32, byte[]? Palette = null, int BackgroundTolerance = 40, bool Outline = false, bool KeepBiggest = true);

/// <summary>A small RGBA picture.</summary>
public sealed record PixelImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// A picture from the AI artist made into a game sprite: the background (the colour round the border) taken away,
/// cropped to what's left, shrunk to the sprite's size (each pixel the average of what it covers), its colours cut
/// down to a few or fitted to a palette, an outline if wanted. All done in plain code - nothing invented.
/// </summary>
public static class SpriteMaker
{
    /// <summary>Ultima 6's sixteen colours (the PC's EGA palette).</summary>
    public static readonly byte[] Ega16 =
    {
        0,0,0, 0,0,170, 0,170,0, 0,170,170, 170,0,0, 170,0,170, 170,85,0, 170,170,170,
        85,85,85, 85,85,255, 85,255,85, 85,255,255, 255,85,85, 255,85,255, 255,255,85, 255,255,255
    };

    public static PixelImage Make(GeneratedImage picture, SpriteOptions options)
    {
        int w = picture.Width, h = picture.Height;
        var rgba = (byte[])picture.Rgba.Clone();
        if (options.BackgroundTolerance > 0)
        {
            RemoveBackground(rgba, w, h, options.BackgroundTolerance);
            if (options.KeepBiggest) KeepBiggestPiece(rgba, w, h);
        }
        var (x0, y0, x1, y1) = Bounds(rgba, w, h);
        if (x1 < x0) return new PixelImage(1, 1, new byte[4]);
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        int th = Math.Clamp(options.Height, 4, 512), tw = Math.Max(1, (int)Math.Round((double)cw * th / ch));
        var small = Shrink(rgba, w, x0, y0, cw, ch, tw, th);
        if (options.Palette is { Length: >= 3 } palette) { StretchLevels(small); FitPalette(small, palette); }
        else if (options.Colours > 0) FitPalette(small, MedianCut(small, options.Colours));
        var image = new PixelImage(tw, th, small);
        return options.Outline ? AddOutline(image) : image;
    }

    /// <summary>Clears everything joined to the border that is near the border's own colour (flood fill from the edges).</summary>
    public static void RemoveBackground(byte[] rgba, int w, int h, int tolerance)
    {
        long r = 0, g = 0, b = 0;
        int n = 0;
        void Sample(int x, int y) { int i = (y * w + x) * 4; r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; n++; }
        for (int x = 0; x < w; x++) { Sample(x, 0); Sample(x, h - 1); }
        for (int y = 0; y < h; y++) { Sample(0, y); Sample(w - 1, y); }
        int br = (int)(r / n), bg = (int)(g / n), bb = (int)(b / n);
        bool Near(int i) => Math.Abs(rgba[i] - br) + Math.Abs(rgba[i + 1] - bg) + Math.Abs(rgba[i + 2] - bb) <= tolerance * 3;
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int p = stack.Pop();
            if (seen[p] || !Near(p * 4)) continue;
            seen[p] = true;
            rgba[p * 4 + 3] = 0;
            int px = p % w, py = p / w;
            if (px > 0) stack.Push(p - 1);
            if (px < w - 1) stack.Push(p + 1);
            if (py > 0) stack.Push(p - w);
            if (py < h - 1) stack.Push(p + w);
        }
    }

    /// <summary>Only the biggest joined piece stays solid (pieces touching side or corner are one).</summary>
    public static void KeepBiggestPiece(byte[] rgba, int w, int h)
    {
        var label = new int[w * h];
        int next = 0, best = 0, bestSize = 0;
        var stack = new Stack<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (label[start] != 0 || rgba[start * 4 + 3] == 0) continue;
            int id = ++next, size = 0;
            label[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                size++;
                int px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = px + dx, y = py + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        int q = y * w + x;
                        if (label[q] != 0 || rgba[q * 4 + 3] == 0) continue;
                        label[q] = id;
                        stack.Push(q);
                    }
            }
            if (size > bestSize) { bestSize = size; best = id; }
        }
        for (int p = 0; p < w * h; p++) if (label[p] != best) rgba[p * 4 + 3] = 0;
    }

    /// <summary>Spreads the sprite's brightness over the whole range (the darkest 2% black, the brightest 2% white) - so a
    /// small fixed palette (Ultima 6's sixteen) gets its colours, not only its greys.</summary>
    public static void StretchLevels(byte[] rgba)
    {
        var levels = new List<int>();
        for (int i = 0; i < rgba.Length; i += 4) if (rgba[i + 3] > 0) levels.Add((rgba[i] * 3 + rgba[i + 1] * 4 + rgba[i + 2] * 2) / 9);
        if (levels.Count < 4) return;
        levels.Sort();
        int low = levels[levels.Count * 2 / 100], high = levels[levels.Count * 98 / 100];
        if (high - low < 8) return;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            for (int c = 0; c < 3; c++) rgba[i + c] = (byte)Math.Clamp((rgba[i + c] - low) * 255 / (high - low), 0, 255);
        }
    }

    private static (int, int, int, int) Bounds(byte[] rgba, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rgba[(y * w + x) * 4 + 3] > 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        return (x0, y0, x1, y1);
    }

    /// <summary>Each small pixel: the average of the solid pixels it covers; see-through when fewer than half are solid.</summary>
    private static byte[] Shrink(byte[] rgba, int w, int x0, int y0, int cw, int ch, int tw, int th)
    {
        var result = new byte[tw * th * 4];
        for (int ty = 0; ty < th; ty++)
            for (int tx = 0; tx < tw; tx++)
            {
                int sx0 = x0 + tx * cw / tw, sx1 = Math.Max(sx0 + 1, x0 + (tx + 1) * cw / tw);
                int sy0 = y0 + ty * ch / th, sy1 = Math.Max(sy0 + 1, y0 + (ty + 1) * ch / th);
                long r = 0, g = 0, b = 0;
                int solid = 0, all = 0;
                for (int y = sy0; y < sy1; y++)
                    for (int x = sx0; x < sx1; x++)
                    {
                        all++;
                        int i = (y * w + x) * 4;
                        if (rgba[i + 3] == 0) continue;
                        r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; solid++;
                    }
                int o = (ty * tw + tx) * 4;
                if (solid * 2 < all) continue;
                result[o] = (byte)(r / solid); result[o + 1] = (byte)(g / solid); result[o + 2] = (byte)(b / solid); result[o + 3] = 255;
            }
        return result;
    }

    /// <summary>A palette of up to count colours that suits the picture (median cut).</summary>
    public static byte[] MedianCut(byte[] rgba, int count)
    {
        var colours = new List<(byte R, byte G, byte B)>();
        for (int i = 0; i < rgba.Length; i += 4) if (rgba[i + 3] > 0) colours.Add((rgba[i], rgba[i + 1], rgba[i + 2]));
        if (colours.Count == 0) return new byte[] { 0, 0, 0 };
        var boxes = new List<List<(byte R, byte G, byte B)>> { colours };
        while (boxes.Count < count)
        {
            var box = boxes.Where(x => x.Count > 1).OrderByDescending(Spread).FirstOrDefault();
            if (box is null || Spread(box) == 0) break;
            int channel = Channel(box);
            box.Sort((a, b) => Pick(a, channel).CompareTo(Pick(b, channel)));
            boxes.Remove(box);
            boxes.Add(box.GetRange(0, box.Count / 2));
            boxes.Add(box.GetRange(box.Count / 2, box.Count - box.Count / 2));
        }
        var palette = new List<byte>();
        foreach (var box in boxes)
        {
            palette.Add((byte)box.Average(c => c.R));
            palette.Add((byte)box.Average(c => c.G));
            palette.Add((byte)box.Average(c => c.B));
        }
        return palette.ToArray();

        static int Pick((byte R, byte G, byte B) c, int ch) => ch == 0 ? c.R : ch == 1 ? c.G : c.B;
        static int Range(List<(byte R, byte G, byte B)> box, int ch) => box.Max(c => Pick(c, ch)) - box.Min(c => Pick(c, ch));
        static int Spread(List<(byte R, byte G, byte B)> box) => Math.Max(Range(box, 0), Math.Max(Range(box, 1), Range(box, 2)));
        static int Channel(List<(byte R, byte G, byte B)> box)
        {
            int r = Range(box, 0), g = Range(box, 1), b = Range(box, 2);
            return r >= g && r >= b ? 0 : g >= b ? 1 : 2;
        }
    }

    /// <summary>Every solid pixel takes the nearest colour of the palette.</summary>
    public static void FitPalette(byte[] rgba, byte[] palette)
    {
        int count = palette.Length / 3;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            int best = 0, bestDistance = int.MaxValue;
            for (int k = 0; k < count; k++)
            {
                int dr = rgba[i] - palette[k * 3], dg = rgba[i + 1] - palette[k * 3 + 1], db = rgba[i + 2] - palette[k * 3 + 2];
                int d = 3 * dr * dr + 4 * dg * dg + 2 * db * db; // the eye sees green best, blue least
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            rgba[i] = palette[best * 3]; rgba[i + 1] = palette[best * 3 + 1]; rgba[i + 2] = palette[best * 3 + 2];
        }
    }

    /// <summary>A one-pixel dark line round the sprite (the picture grows by one pixel on each side).</summary>
    public static PixelImage AddOutline(PixelImage image)
    {
        int w = image.Width + 2, h = image.Height + 2;
        var result = new byte[w * h * 4];
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height && image.Rgba[(y * image.Width + x) * 4 + 3] > 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4, sx = x - 1, sy = y - 1;
                if (Solid(sx, sy)) { Array.Copy(image.Rgba, (sy * image.Width + sx) * 4, result, o, 4); continue; }
                if (Solid(sx - 1, sy) || Solid(sx + 1, sy) || Solid(sx, sy - 1) || Solid(sx, sy + 1))
                { result[o] = 16; result[o + 1] = 12; result[o + 2] = 8; result[o + 3] = 255; }
            }
        return new PixelImage(w, h, result);
    }
}
