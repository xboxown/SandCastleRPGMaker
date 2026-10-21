namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A program's icon for the Amiga ("Game.info"), from a picture
/// (0xAARRGGBB pixels):
///  - for Amiga 68k: a Workbench DiskObject (a tool) whose own picture is 8
///    colours in the MagicWB palette (Kickstart 3.1's Workbench shows that),
///    with the picture in full colour after it - the OS 3.5 colour icon
///    (FORM ICON: FACE, IMAG), shown by AmigaOS 3.5 to 3.2 and ApolloOS;
///  - for MorphOS and AmigaOS 4, whose icons are PNG pictures, the editor
///    writes the PNG itself as Game.info.
/// </summary>
public static class AmigaIcon
{
    /// <summary>MagicWB's eight colours: the planar picture Kickstart 3.1's Workbench shows.</summary>
    private static readonly uint[] MagicWb = { 0x959595, 0x000000, 0xFFFFFF, 0x3B67A2, 0x7B7B7B, 0xAFAFAF, 0xAA907C, 0xFFA997 };

    public static byte[] DiskObject(int width, int height, uint[] pixels, int stackSize = 65536)
    {
        var o = new List<byte>();
        void W(int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
        void L(long v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }

        // struct DiskObject: magic, version, its Gadget (the icon's box and pictures), type, tools, position, stack.
        W(0xE310); W(1);
        L(0); W(0); W(0); W(width); W(height + 1); // NextGadget, Left, Top, Width, Height
        W(0x0004); W(0x0003); W(0x0001);         // Flags GADGIMAGE (+GADGHCOMP 0), Activation RELVERIFY|GADGIMMEDIATE, BOOLGADGET
        L(1); L(0); L(0); L(0); L(0);             // GadgetRender (an image follows), SelectRender, GadgetText, MutualExclude, SpecialInfo
        W(0); L(1);                               // GadgetID, UserData: revision 1 (Workbench 2.0+ icon)
        o.Add(3); o.Add(0);                       // WBTOOL
        L(0); L(0);                               // DefaultTool, ToolTypes
        L(0x80000000); L(0x80000000);             // NO_ICON_POSITION
        L(0); L(0); L(stackSize);                 // DrawerData, ToolWindow, StackSize

        // struct Image + its three bitplanes (MagicWB colours, nearest).
        const int depth = 3;
        W(0); W(0); W(width); W(height); W(depth); L(1); o.Add(7); o.Add(0); L(0);
        int rowBytes = (width + 15) / 16 * 2;
        var planes = new byte[depth][];
        for (int p = 0; p < depth; p++) planes[p] = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                uint c = pixels[y * width + x];
                int index = (c >> 24) < 128 ? 0 : Nearest(MagicWb, c);
                for (int p = 0; p < depth; p++)
                    if (((index >> p) & 1) != 0) planes[p][y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        foreach (var plane in planes) o.AddRange(plane);

        o.AddRange(ColourIcon(width, height, pixels));
        return o.ToArray();
    }

    /// <summary>The OS 3.5 colour icon: FORM ICON with FACE and one IMAG (up to 256 colours, colour 0 transparent).</summary>
    private static byte[] ColourIcon(int width, int height, uint[] pixels)
    {
        var palette = new List<uint> { 0 }; // 0: see-through
        var index = new byte[width * height];
        var known = new Dictionary<uint, int>();
        for (int i = 0; i < pixels.Length; i++)
        {
            uint c = pixels[i];
            if ((c >> 24) < 128) { index[i] = 0; continue; }
            uint rgb = c & 0xFFFFFF;
            if (!known.TryGetValue(rgb, out int at))
            {
                if (palette.Count < 256) { at = palette.Count; palette.Add(rgb); }
                else at = Nearest(palette.Skip(1).ToArray(), rgb) + 1; // more than 255 colours: the nearest one already taken
                known[rgb] = at;
            }
            index[i] = (byte)at;
        }
        var face = new List<byte> { (byte)(width - 1), (byte)(height - 1), 0, 0x11 };
        int paletteBytes = palette.Count * 3;
        face.Add((byte)((paletteBytes - 1) >> 8)); face.Add((byte)(paletteBytes - 1));
        var imag = new List<byte> { 0, (byte)(palette.Count - 1), 0x03, 0, 0, 8 }; // transparent colour 0; colours; has transparency + a palette; not packed; 8 bits
        imag.Add((byte)((index.Length - 1) >> 8)); imag.Add((byte)(index.Length - 1));
        imag.Add((byte)((paletteBytes - 1) >> 8)); imag.Add((byte)(paletteBytes - 1));
        imag.AddRange(index);
        foreach (uint c in palette) { imag.Add((byte)(c >> 16)); imag.Add((byte)(c >> 8)); imag.Add((byte)c); }

        var body = new List<byte>();
        body.AddRange("ICON"u8.ToArray());
        Chunk(body, "FACE", face);
        Chunk(body, "IMAG", imag);
        var form = new List<byte>();
        form.AddRange("FORM"u8.ToArray());
        form.Add((byte)(body.Count >> 24)); form.Add((byte)(body.Count >> 16)); form.Add((byte)(body.Count >> 8)); form.Add((byte)body.Count);
        form.AddRange(body);
        return form.ToArray();
    }

    private static void Chunk(List<byte> into, string id, List<byte> data)
    {
        into.AddRange(System.Text.Encoding.ASCII.GetBytes(id));
        into.Add((byte)(data.Count >> 24)); into.Add((byte)(data.Count >> 16)); into.Add((byte)(data.Count >> 8)); into.Add((byte)data.Count);
        into.AddRange(data);
        if (data.Count % 2 == 1) into.Add(0);
    }

    private static int Nearest(uint[] palette, uint c)
    {
        int best = 0, bestDistance = int.MaxValue;
        int r = (int)(c >> 16) & 255, g = (int)(c >> 8) & 255, b = (int)c & 255;
        for (int i = 0; i < palette.Length; i++)
        {
            int dr = r - (int)((palette[i] >> 16) & 255), dg = g - (int)((palette[i] >> 8) & 255), db = b - (int)(palette[i] & 255);
            int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }
}
