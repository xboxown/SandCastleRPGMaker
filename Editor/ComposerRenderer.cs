using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>A bone where it ended up for one frame: figure units, West's coordinates for a mirrored East.</summary>
public sealed record PlacedBone(ComposerBone Bone, Point Start, Point End, double Angle, double RestAngle, double Length, double Width, bool Shown);

/// <summary>
/// The Sprite Composer's drawing: places the skeleton for a pose frame and direction (each bone hangs from its parent,
/// turned by the pose, sized by the recipe), then draws every bone in layer order - its part picture if the library has
/// one for it, else its shape in the recipe's colour, the far side darker, a dark edge round it.
/// </summary>
public static class ComposerRenderer
{
    /// <summary>The picture every frame is drawn into (so a sprite's frames line up): units wide / high, the feet at FeetX, FeetY.</summary>
    public const double CanvasWidth = 84, CanvasHeight = 76, FeetX = 42, FeetY = 73;

    private static Point Dir(double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(Math.Sin(a), Math.Cos(a));
    }

    private static Vector Turn(Vector v, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new Vector(v.X * c + v.Y * s, v.Y * c - v.X * s);
    }

    public static bool Mirrored(ComposerLibrary lib, string direction) => direction == "East" && !lib.Skeleton.ContainsKey("East");

    /// <summary>Every bone placed (feet at 0, 0; up is minus).</summary>
    public static List<PlacedBone> Place(ComposerLibrary lib, string direction, ComposerPoseFrame? frame, ComposerRecipe recipe)
    {
        bool mirror = Mirrored(lib, direction);
        string boneDirection = mirror ? "West" : direction;
        string poseDirection = mirror ? "West" : direction;
        var bones = lib.BonesFor(boneDirection);
        double bodyAngle = frame?.BodyAngle ?? 0;
        var root = new Point(0, -lib.RootHeight * recipe.Height * recipe.LegLength + (frame?.Lower ?? 0) * recipe.Height);
        var placed = new Dictionary<string, PlacedBone>();
        var pending = bones.ToList();
        for (int pass = 0; pass < 12 && pending.Count > 0; pass++)
            foreach (var bone in pending.ToList())
            {
                PlacedBone? parent = null;
                if (bone.Parent.Length > 0 && !placed.TryGetValue(bone.Parent, out parent))
                {
                    if (bones.Any(b => b.Name == bone.Parent)) continue; // its parent first
                }
                double parentAngle = parent?.Angle ?? bodyAngle, parentRest = parent?.RestAngle ?? 0;
                double turn = frame?.TurnOf(poseDirection, bone.Name) ?? 0;
                double angle = parentAngle + bone.Angle + turn, rest = parentRest + bone.Angle;
                Point attach = parent is null ? root : bone.AtStart ? parent.Start : parent.End;
                var offset = Turn(new Vector(bone.OffsetX * recipe.Build, bone.OffsetY * recipe.Height), parentAngle - parentRest);
                var start = attach + offset;
                double length = bone.Length * LengthScale(bone, recipe) * (frame?.StretchOf(poseDirection, bone.Name) ?? 1);
                double width = bone.Width * (IsHead(bone) ? recipe.HeadSize : recipe.Build);
                var d = Dir(angle);
                var end = new Point(start.X + d.X * length, start.Y + d.Y * length);
                bool shown = bone.NeedsOption.Length == 0 || recipe.Options.Contains(bone.NeedsOption);
                placed[bone.Name] = new PlacedBone(bone, start, end, angle, rest, length, width, shown);
                pending.Remove(bone);
            }
        var list = bones.Where(b => placed.ContainsKey(b.Name)).Select(b => placed[b.Name]).ToList();
        if (bodyAngle != 0 && list.Count > 0)
        {
            // lying down: the body rests on the ground
            double lowest = list.Max(p => Math.Max(p.Start.Y, p.End.Y) + p.Width / 2);
            list = list.Select(p => p with { Start = new Point(p.Start.X, p.Start.Y - lowest), End = new Point(p.End.X, p.End.Y - lowest) }).ToList();
        }
        return list;
    }

    private static bool IsHead(ComposerBone b) => b.Name.StartsWith("Head") || b.Name.StartsWith("Hair");

    private static double LengthScale(ComposerBone b, ComposerRecipe r)
    {
        double s = r.Height;
        if (b.Name.StartsWith("Leg") || b.Name.StartsWith("Shin")) s *= r.LegLength;
        else if (b.Name.StartsWith("Arm") || b.Name.StartsWith("Forearm")) s *= r.ArmLength;
        else if (IsHead(b)) s *= r.HeadSize;
        return s;
    }

    public static Color ColourOf(ComposerRecipe recipe, string region)
    {
        try { if (recipe.Colours.TryGetValue(region, out var hex)) return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { }
        return Colors.Gray;
    }

    private static Color Shade(Color c, double f) => Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    /// <summary>Draws the placed bones; the caller has set the transform (figure units to pixels, East mirrored).</summary>
    public static void Draw(DrawingContext dc, ComposerLibrary lib, string direction, List<PlacedBone> bones, ComposerRecipe recipe, double pixelsPerUnit)
    {
        string partDirection = Mirrored(lib, direction) ? "West" : direction;
        double edge = 1.1 / Math.Max(0.25, pixelsPerUnit); // about one pixel of dark edge
        foreach (var p in bones.Where(b => b.Shown).OrderBy(b => b.Bone.Layer))
        {
            var part = lib.Parts.FirstOrDefault(x => x.Bone == p.Bone.Name && (x.Direction == partDirection || x.Direction == "") && (x.Option.Length == 0 || recipe.Options.Contains(x.Option))
                                                     && (x.Direction != "" || !lib.Parts.Any(y => y.Bone == p.Bone.Name && y.Direction == partDirection)));
            Color colour = ColourOf(recipe, p.Bone.Region);
            if (p.Bone.Far) colour = Shade(colour, 0.78);
            if (part is not null && PartBitmap(part, colour) is { } bitmap)
            {
                var m = Matrix.Identity;
                m.Translate(-part.PivotX, -part.PivotY);
                m.Scale(part.Scale * recipe.Height, part.Scale * recipe.Height);
                m.Rotate(-(p.Angle - p.RestAngle));
                m.Translate(p.Start.X, p.Start.Y);
                dc.PushTransform(new MatrixTransform(m));
                dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                dc.Pop();
                continue;
            }
            string shape = p.Bone.Region == "Weapon" && p.Bone.Shape == "Blade" ? recipe.WeaponShape : p.Bone.Shape;
            DrawShape(dc, shape, p, colour, edge, direction);
        }
    }

    private static void DrawShape(DrawingContext dc, string shape, PlacedBone p, Color colour, double edge, string direction)
    {
        var fill = new SolidColorBrush(colour);
        var dark = new SolidColorBrush(Shade(colour, 0.45));
        var mid = new Point((p.Start.X + p.End.X) / 2, (p.Start.Y + p.End.Y) / 2);
        switch (shape)
        {
            case "None": return;
            case "Capsule":
            {
                var outer = new Pen(dark, p.Width + edge * 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var inner = new Pen(fill, p.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(outer, p.Start, p.End);
                dc.DrawLine(inner, p.Start, p.End);
                return;
            }
            case "Box":
            {
                var d = new Vector(p.End.X - p.Start.X, p.End.Y - p.Start.Y);
                if (d.Length < 0.01) d = new Vector(0, 0.01);
                var n = new Vector(-d.Y, d.X);
                n.Normalize();
                n *= p.Width / 2;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(p.Start + n, true, true);
                    c.LineTo(p.End + n, true, false);
                    c.LineTo(p.End - n, true, false);
                    c.LineTo(p.Start - n, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(fill, new Pen(dark, edge), g);
                return;
            }
            case "Ellipse":
            case "Head":
            {
                dc.PushTransform(new RotateTransform(-p.Angle, mid.X, mid.Y));
                dc.DrawEllipse(fill, new Pen(dark, edge), mid, p.Width / 2, Math.Max(p.Length, 1) / 2);
                if (shape == "Head" && direction != "North")
                {
                    // eyes: two from the front, one on the side it looks to (the picture is mirrored for East)
                    var eye = new SolidColorBrush(Color.FromRgb(24, 16, 16));
                    double r = Math.Max(0.6, p.Width * 0.09), y = mid.Y + p.Length * 0.02;
                    // the head points up (angle 180): after the rotation above, screen "up" is +y in this frame
                    if (direction == "South") { dc.DrawEllipse(eye, null, new Point(mid.X - p.Width * 0.2, y), r, r); dc.DrawEllipse(eye, null, new Point(mid.X + p.Width * 0.2, y), r, r); }
                    else dc.DrawEllipse(eye, null, new Point(mid.X + p.Width * 0.28, y), r, r);
                }
                dc.Pop();
                return;
            }
            case "Blade":
            case "Axe":
            {
                var d = new Vector(p.End.X - p.Start.X, p.End.Y - p.Start.Y);
                if (d.Length < 0.01) return;
                var u = d; u.Normalize();
                var n = new Vector(-u.Y, u.X);
                var hilt = p.Start + u * (p.Length * 0.18);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(90, 60, 30)), p.Width * 0.8 + edge), p.Start, hilt);
                if (shape == "Blade")
                {
                    dc.DrawLine(new Pen(dark, p.Width + edge * 2) { EndLineCap = PenLineCap.Triangle }, hilt, p.End);
                    dc.DrawLine(new Pen(fill, p.Width) { EndLineCap = PenLineCap.Triangle }, hilt, p.End);
                    dc.DrawLine(new Pen(dark, p.Width * 0.9), hilt - n * p.Width * 1.4, hilt + n * p.Width * 1.4); // crossguard
                }
                else
                {
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(90, 60, 30)), p.Width * 0.8), hilt, p.End);
                    var head = p.Start + u * (p.Length * 0.8);
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(head - u * p.Width * 1.2, true, true);
                        c.LineTo(head + n * p.Width * 2.6 - u * p.Width * 2.2, true, false);
                        c.LineTo(head + n * p.Width * 2.6 + u * p.Width * 2.2, true, false);
                        c.LineTo(head + u * p.Width * 1.2, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(fill, new Pen(dark, edge), g);
                }
                return;
            }
        }
    }

    // ---------- part pictures ----------

    private static readonly Dictionary<(ComposerPart, Color, bool), BitmapSource?> _partCache = new();

    public static void ForgetPart(ComposerPart part)
    {
        foreach (var key in _partCache.Keys.Where(k => ReferenceEquals(k.Item1, part)).ToList()) _partCache.Remove(key);
    }

    public static BitmapSource? PartBitmap(ComposerPart part, Color tint)
    {
        var key = (part, part.Tint ? tint : Colors.Transparent, part.Tint);
        if (_partCache.TryGetValue(key, out var cached)) return cached;
        BitmapSource? result = null;
        try
        {
            var decoder = new PngBitmapDecoder(new MemoryStream(Convert.FromBase64String(part.Png)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (part.Tint)
            {
                // the region's colour with the part's own light and dark (palette recolouring)
                int w = source.PixelWidth, h = source.PixelHeight;
                var px = new byte[w * h * 4];
                source.CopyPixels(px, w * 4, 0);
                for (int i = 0; i < px.Length; i += 4)
                {
                    double light = (px[i] * 0.11 + px[i + 1] * 0.59 + px[i + 2] * 0.3) / 128.0;
                    px[i] = (byte)Math.Clamp(tint.B * light, 0, 255);
                    px[i + 1] = (byte)Math.Clamp(tint.G * light, 0, 255);
                    px[i + 2] = (byte)Math.Clamp(tint.R * light, 0, 255);
                }
                source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            }
            source.Freeze();
            result = source;
        }
        catch (Exception) { result = null; }
        return _partCache[key] = result;
    }

    // ---------- whole frames ----------

    /// <summary>One frame as RGBA pixels, drawn sharp (no smoothing) at the sprite's size; every frame the same size.</summary>
    public static (int Width, int Height, byte[] Rgba) RenderFrame(ComposerLibrary lib, string direction, ComposerPoseFrame? frame, ComposerRecipe recipe, int spriteHeight)
    {
        double scale = spriteHeight / Composer.ReferenceHeight;
        int w = Math.Max(1, (int)Math.Ceiling(CanvasWidth * scale)), h = Math.Max(1, (int)Math.Ceiling(CanvasHeight * scale));
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            var m = Matrix.Identity;
            if (Mirrored(lib, direction)) m.Scale(-1, 1);
            m.Translate(FeetX, FeetY);
            m.Scale(scale, scale);
            dc.PushTransform(new MatrixTransform(m));
            Draw(dc, lib, direction, Place(lib, direction, frame, recipe), recipe, scale);
            dc.Pop();
        }
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var bgra = new byte[w * h * 4];
        target.CopyPixels(bgra, w * 4, 0);
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a < 128) continue; // sharp edges: a pixel is there or not
            // un-premultiply
            rgba[i] = (byte)Math.Min(255, bgra[i + 2] * 255 / a);
            rgba[i + 1] = (byte)Math.Min(255, bgra[i + 1] * 255 / a);
            rgba[i + 2] = (byte)Math.Min(255, bgra[i] * 255 / a);
            rgba[i + 3] = 255;
        }
        return (w, h, rgba);
    }

    public static BitmapSource ToBitmap(int width, int height, byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4) { bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = rgba[i + 3]; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(int width, int height, byte[] rgba)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(width, height, rgba)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
