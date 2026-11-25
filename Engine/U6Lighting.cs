namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Ultima 6's light, as the original game (Nuvie's "original" lighting style - ScummVM engines/ultima/nuvie: MapWindow::
/// createLightOverlay, Screen::clearalphamap8 / drawalphamap8globe / blitalphamap8, the TileGlobe tables; GPL v3): not a
/// tinted sky (that was Ultima 7's) but darkness a cell at a time. By day nothing is dark. At night - and always in a
/// Cave zone (Ultima 6's dungeons) - every cell is dark (0) but for globes of light added up, each cell at most 4: the
/// Avatar's own (level 4 at night: a 7 x 7 globe, only its own cell fully lit; dusk and dawn step it from 10 down to 5
/// and back up in six steps; a lit light in the party, a light spell or worn magic light: level 8, an 11 x 11 globe)
/// and one for every lit light on the map (a candle-sized light level 2, 3 x 3; brighter ones level 3, 5 x 5) and every
/// other character holding a lit light (level 3). A cell's 0-4 is drawn as Ultima 6's shading tiles: 4 lit, 3 a quarter
/// dark, 2 half, 1 three quarters, 0 black - a 2 x 2 dither. Used by the Windows Ultima 6 engine (U6GameWindow.Lighting.cs)
/// and the Apple IIgs player (Player/iigs/look.s DrawDark: the same tables exported), so both darken alike.
/// </summary>
public static class U6Lighting
{
    /// <summary>Nuvie's TileGlobe: level 1 (1 x 1, magic items) ... level 10 (11 x 11, dusk at 19:00).</summary>
    public static readonly byte[][] Globes =
    {
        new byte[] { 1 },
        new byte[] { 1, 1, 1, 1, 2, 1, 1, 1, 1 },
        new byte[] { 0, 1, 1, 1, 0, 1, 2, 2, 2, 1, 1, 2, 3, 2, 1, 1, 2, 2, 2, 1, 0, 1, 1, 1, 0 },
        new byte[] { 0, 0, 1, 1, 1, 0, 0, 0, 1, 2, 2, 2, 1, 0, 1, 2, 3, 3, 3, 2, 1, 1, 2, 3, 4, 3, 2, 1, 1, 2, 3, 3, 3, 2, 1, 0, 1, 2, 2, 2, 1, 0, 0, 0, 1, 1, 1, 0, 0 },
        new byte[]
        {
            0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 1, 2, 3, 3, 3, 2, 1, 0,
            1, 2, 3, 4, 4, 4, 3, 2, 1, 1, 2, 3, 4, 4, 4, 3, 2, 1, 1, 2, 3, 4, 4, 4, 3, 2, 1,
            0, 1, 2, 3, 3, 3, 2, 1, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0
        },
        new byte[]
        {
            0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 1, 2, 2, 3, 3, 3, 2, 2, 1, 0,
            0, 1, 2, 3, 4, 4, 4, 3, 2, 1, 0, 1, 2, 3, 4, 4, 4, 4, 4, 3, 2, 1, 1, 2, 3, 4, 4, 4, 4, 4, 3, 2, 1,
            1, 2, 3, 4, 4, 4, 4, 4, 3, 2, 1, 0, 1, 2, 3, 4, 4, 4, 3, 2, 1, 0, 0, 1, 2, 2, 3, 3, 3, 2, 2, 1, 0,
            0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0
        },
        new byte[]
        {
            0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 1, 2, 2, 3, 3, 3, 2, 2, 1, 0, 1, 2, 3, 3, 4, 4, 4, 3, 3, 2, 1,
            1, 2, 3, 4, 4, 4, 4, 4, 3, 2, 1, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2,
            2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2, 1, 2, 3, 4, 4, 4, 4, 4, 3, 2, 1, 1, 2, 3, 3, 4, 4, 4, 3, 3, 2, 1,
            0, 1, 2, 2, 3, 3, 3, 2, 2, 1, 0, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0
        },
        new byte[]
        {
            2, 2, 2, 2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 3, 3, 4, 4, 4, 3, 3, 2, 2, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2,
            2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3,
            3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2, 2, 3, 4, 4, 4, 4, 4, 4, 4, 3, 2,
            2, 2, 3, 3, 4, 4, 4, 3, 3, 2, 2, 2, 2, 2, 2, 3, 3, 3, 2, 2, 2, 2
        },
        new byte[]
        {
            3, 3, 3, 3, 4, 4, 4, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3,
            3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3,
            3, 3, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 4, 4, 4, 3, 3, 3, 3
        },
        new byte[]
        {
            3, 3, 3, 4, 4, 4, 4, 4, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3,
            3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 4, 4, 4, 4, 4, 3, 3, 3
        },
    };

    /// <summary>The night's level (the Avatar's globe alone, no lights).</summary>
    public const int NightLevel = 4;
    /// <summary>A lit light in the party, a light spell, worn magic light: the Avatar's globe at least this.</summary>
    public const int PartyLightLevel = 8;

    /// <summary>A globe's half-width: level 1 = 0 (1 x 1) ... level 6 and up = 5 (11 x 11).</summary>
    public static int Radius(int level) => level < 6 ? level - 1 : 5;

    /// <summary>
    /// The Avatar's own globe for the time of day (0 = day: no darkness at all): night 4; from dusk's start to night's
    /// six steps 10, 9 ... 5; from dawn's start to day's 5, 6 ... 10. A Cave: always dark (4).
    /// </summary>
    public static int AmbientLevel(int hour, int minute, DayNightSettings d, bool cave)
    {
        if (cave) return NightLevel;
        if (!d.Enabled) return 0;
        int now = hour * 60 + minute;
        int dawn = d.DawnStartHour * 60, day = Math.Max(d.DayStartHour * 60, dawn + 1), dusk = d.DuskStartHour * 60, night = Math.Max(d.NightStartHour * 60, dusk + 1);
        if (now >= day && now < dusk) return 0;
        if (now >= dusk && now < night) return 10 - Math.Min(5, (now - dusk) * 6 / (night - dusk));
        if (now >= dawn && now < day) return 5 + Math.Min(5, (now - dawn) * 6 / (day - dawn));
        return NightLevel;
    }

    /// <summary>A lit light source's own globe by its brightness (Exult's: a candle 1, a sconce 5, a lamp 7): candle-sized 2, brighter 3.</summary>
    public static int ObjectLevel(int brightness) => brightness <= 3 ? 2 : 3;

    /// <summary>Adds a globe of <paramref name="level"/> centred on cell (cx, cy) of a w x h grid (each cell at most 4).</summary>
    public static void AddGlobe(byte[] shade, int w, int h, int cx, int cy, int level)
    {
        if (level < 1) return;
        var globe = Globes[Math.Min(level, Globes.Length) - 1];
        int r = Radius(level), side = r * 2 + 1;
        for (int j = 0; j < side; j++)
        {
            int y = cy - r + j;
            if (y < 0 || y >= h) continue;
            for (int i = 0; i < side; i++)
            {
                int x = cx - r + i;
                if (x < 0 || x >= w || globe[j * side + i] == 0) continue;
                shade[y * w + x] = (byte)Math.Min(4, shade[y * w + x] + globe[j * side + i]);
            }
        }
    }

    /// <summary>Whether a pixel of a cell with light 0-4 is drawn dark: Ultima 6's shading tiles as a 2 x 2 dither.</summary>
    public static bool DarkPixel(int light, int px, int py)
    {
        if (light >= 4) return false;
        int b = (py & 1) == 0 ? ((px & 1) == 0 ? 0 : 2) : ((px & 1) == 0 ? 3 : 1);
        return b < 4 - light;
    }
}
