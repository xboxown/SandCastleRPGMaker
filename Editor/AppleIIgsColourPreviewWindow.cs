// Ultima 6 Apple IIgs mode only: what the Apple IIgs will really show - every colour taken to the IIgs's 12 bits and to
// the game's 16-colour palette, exactly as Build does (SuperHiRes.MakePalette / Nearest). The art itself stays 24-bit.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public sealed class AppleIIgsColourPreviewWindow : Window
{
    private readonly AppleIIgsGamePack _pack;
    private readonly AppleIIgsPreviewData _src;
    private readonly ComboBox _what = new() { MinWidth = 240, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _zoom = new() { Width = 70 };
    private readonly Image _before = new() { Stretch = Stretch.None, SnapsToDevicePixels = true };
    private readonly Image _after = new() { Stretch = Stretch.None, SnapsToDevicePixels = true };
    private readonly ScrollViewer _beforeScroll, _afterScroll;

    public AppleIIgsColourPreviewWindow(AppleIIgsGamePack pack, IReadOnlyList<string> notes)
    {
        _pack = pack;
        _src = pack.Preview ?? new AppleIIgsPreviewData();
        Title = "Apple IIgs Colour Preview";
        Width = 1180; Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RenderOptions.SetBitmapScalingMode(_before, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_after, BitmapScalingMode.NearestNeighbor);

        var root = new DockPanel { Margin = new Thickness(8) };
        // The 16 colours the build makes.
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        palette.Children.Add(new TextBlock { Text = "The game's 16 colours (12-bit): ", VerticalAlignment = VerticalAlignment.Center });
        for (int i = 0; i < pack.Palette.Length; i++)
        {
            ushort c = pack.Palette[i];
            uint argb = SuperHiRes.FromRgb12(c);
            var sw = new Border
            {
                Width = 44, Height = 34, Margin = new Thickness(1), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)),
                ToolTip = $"Colour {i}: ${c:X3}" + (i == 0 ? " (in sprites: see-through)" : "")
            };
            bool light = ((c >> 8 & 15) + (c >> 4 & 15) + (c & 15)) > 22;
            sw.Child = new TextBlock { Text = $"{i}\n${c:X3}", FontSize = 10, TextAlignment = TextAlignment.Center, Foreground = light ? Brushes.Black : Brushes.White };
            palette.Children.Add(sw);
        }
        DockPanel.SetDock(palette, Dock.Top);
        root.Children.Add(palette);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Show: ", VerticalAlignment = VerticalAlignment.Center });
        for (int m = 0; m < 1 + pack.OtherMaps.Count; m++)
            _what.Items.Add(m < _src.MapNames.Count ? $"Map {m}: {_src.MapNames[m]}" : $"Map {m}");
        _what.Items.Add("Every tile");
        _what.Items.Add("Every sprite");
        if (_src.Hud is not null) _what.Items.Add("The HUD");
        if (_src.Title is not null) _what.Items.Add("The title picture");
        _what.SelectedIndex = 0;
        _what.SelectionChanged += (_, _) => Draw();
        bar.Children.Add(_what);
        bar.Children.Add(new TextBlock { Text = "Zoom: ", VerticalAlignment = VerticalAlignment.Center });
        foreach (var z in new[] { "1x", "2x", "3x", "4x" }) _zoom.Items.Add(z);
        _zoom.SelectedIndex = 1;
        _zoom.SelectionChanged += (_, _) => Zoom();
        bar.Children.Add(_zoom);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // What the check finds about the colours.
        var found = new TextBox
        {
            Text = notes.Count == 0 ? "The colours come through the Apple IIgs's 16 well." : string.Join("\n", notes.Select(n => "- " + (n.StartsWith("Note: ") ? n[6..] : n))),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0)
        };
        DockPanel.SetDock(found, Dock.Bottom);
        root.Children.Add(found);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var l1 = new TextBlock { Text = "Your colours (24-bit, as drawn)", FontWeight = FontWeights.Bold };
        var l2 = new TextBlock { Text = "On the Apple IIgs (12-bit, the 16 colours above)", FontWeight = FontWeights.Bold };
        Grid.SetColumn(l2, 2);
        grid.Children.Add(l1);
        grid.Children.Add(l2);
        _beforeScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _before, Background = Brushes.Black };
        _afterScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _after, Background = Brushes.Black };
        // Both scroll together: the same place side by side.
        _beforeScroll.ScrollChanged += (_, e) => { _afterScroll.ScrollToHorizontalOffset(e.HorizontalOffset); _afterScroll.ScrollToVerticalOffset(e.VerticalOffset); };
        _afterScroll.ScrollChanged += (_, e) => { _beforeScroll.ScrollToHorizontalOffset(e.HorizontalOffset); _beforeScroll.ScrollToVerticalOffset(e.VerticalOffset); };
        Grid.SetRow(_beforeScroll, 1);
        Grid.SetRow(_afterScroll, 1);
        Grid.SetColumn(_afterScroll, 2);
        grid.Children.Add(_beforeScroll);
        grid.Children.Add(_afterScroll);
        root.Children.Add(grid);
        Content = root;
        Draw();
        Zoom();
    }

    private void Zoom()
    {
        double z = _zoom.SelectedIndex + 1;
        _before.LayoutTransform = new ScaleTransform(z, z);
        _after.LayoutTransform = new ScaleTransform(z, z);
    }

    private void Draw()
    {
        int sel = _what.SelectedIndex, maps = 1 + _pack.OtherMaps.Count;
        uint[] px;
        int w, h;
        if (sel < maps) (px, w, h) = MapPicture(sel);
        else if (sel == maps) (px, w, h) = Sheet(_src.Tiles.Select(t => (t, 16, 16)).ToList(), false);
        else if (sel == maps + 1) (px, w, h) = Sheet(_src.Sprites.Select(s => (s.Px, s.W, s.H)).ToList(), true);
        else if (_src.Hud is not null && sel == maps + 2) (px, w, h) = (_src.Hud, 320, 200);
        else (px, w, h) = (_src.Title ?? new uint[320 * 200], 320, 200);
        _before.Source = Bitmap(px, w, h, false);
        _after.Source = Bitmap(px, w, h, true);
    }

    /// <summary>A map as the IIgs draws it: its tiles, its things, its characters (around its start if it is big).</summary>
    private (uint[] Px, int W, int H) MapPicture(int m)
    {
        var (grid, mw, mh, sx, sy, chars) = m == 0
            ? (_pack.Map, _pack.MapWidth, _pack.MapHeight, _pack.StartX, _pack.StartY, _pack.Characters)
            : (_pack.OtherMaps[m - 1].Map, _pack.OtherMaps[m - 1].Width, _pack.OtherMaps[m - 1].Height, _pack.OtherMaps[m - 1].StartX, _pack.OtherMaps[m - 1].StartY, _pack.OtherMaps[m - 1].Characters);
        int cols = Math.Min(mw, 96), rows = Math.Min(mh, 96);
        int x0 = Math.Clamp(sx - cols / 2, 0, Math.Max(0, mw - cols)), y0 = Math.Clamp(sy - rows / 2, 0, Math.Max(0, mh - rows));
        int w = cols * 16, h = rows * 16;
        var px = new uint[w * h];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int at = (y0 + y) * mw + x0 + x;
                if (at >= grid.Length) continue;
                int t = grid[at] & 0x3FFF;
                if (t < _src.Tiles.Count) Put(px, w, h, _src.Tiles[t], 16, 16, x * 16, y * 16, false);
            }
        void Sprite(int sprite, int cx, int cy)
        {
            if (sprite < 0 || sprite >= _src.Sprites.Count) return;
            var (sp, sw, sh) = _src.Sprites[sprite];
            // As the player draws them: the bottom right on the cell's bottom right.
            Put(px, w, h, sp, sw, sh, (cx - x0) * 16 + 16 - sw, (cy - y0) * 16 + 16 - sh, true);
        }
        foreach (var o in _pack.Objects)
        {
            if ((o.Flags & (32 | 64 | 256)) != 0 || (o.Flags >> 12) != m || o.X >= 0xFFFF) continue;
            if (o.X >= x0 && o.Y >= y0 && o.X < x0 + cols + 4 && o.Y < y0 + rows + 4) Sprite(o.Sprite, o.X, o.Y);
        }
        foreach (var c in chars)
            if (c.X >= x0 && c.Y >= y0 && c.X < x0 + cols + 4 && c.Y < y0 + rows + 4) Sprite(c.Sprite, c.X, c.Y);
        if (m == 0 && _src.Avatar.Count > 0) Put(px, w, h, _src.Avatar[0], 16, 16, (sx - x0) * 16, (sy - y0) * 16, true);
        return (px, w, h);
    }

    /// <summary>Pictures side by side in rows, a gap between.</summary>
    private static (uint[] Px, int W, int H) Sheet(List<(uint[] Px, int W, int H)> pics, bool clear)
    {
        const int width = 512;
        int x = 0, y = 0, rowH = 0;
        var at = new List<(int X, int Y)>();
        foreach (var (_, pw, ph) in pics)
        {
            if (x + pw > width) { x = 0; y += rowH + 4; rowH = 0; }
            at.Add((x, y));
            x += pw + 4;
            rowH = Math.Max(rowH, ph);
        }
        int h = Math.Max(16, y + rowH);
        var px = new uint[width * h];
        for (int i = 0; i < pics.Count; i++) Put(px, width, h, pics[i].Px, pics[i].W, pics[i].H, at[i].X, at[i].Y, clear);
        return (px, width, h);
    }

    private static void Put(uint[] dst, int dw, int dh, uint[] src, int sw, int sh, int dx, int dy, bool clear)
    {
        for (int y = 0; y < sh; y++)
        {
            int ty = dy + y;
            if (ty < 0 || ty >= dh) continue;
            for (int x = 0; x < sw; x++)
            {
                int tx = dx + x;
                if (tx < 0 || tx >= dw) continue;
                uint c = src[y * sw + x];
                if (clear && (c >> 24) < 128) continue;
                dst[ty * dw + tx] = c | 0xFF000000u;
            }
        }
    }

    /// <summary>The picture as drawn, or as the IIgs shows it (each pixel its nearest of the 16, by Build's own rule).</summary>
    private BitmapSource Bitmap(uint[] px, int w, int h, bool iigs)
    {
        var data = new uint[px.Length];
        var cache = new Dictionary<uint, uint>();
        for (int i = 0; i < px.Length; i++)
        {
            uint c = px[i] | 0xFF000000u;
            if (!iigs) { data[i] = c; continue; }
            if (!cache.TryGetValue(c, out uint shown))
                cache[c] = shown = SuperHiRes.FromRgb12(_pack.Palette[SuperHiRes.Nearest(_pack.Palette, c, false)]);
            data[i] = shown;
        }
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), data, w * 4, 0);
        return bmp;
    }
}
