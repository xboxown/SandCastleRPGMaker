namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Game Window > Fill the screen (U7 and U8 game windows; the Sandcastle player's platform_view_size does the same):
/// the picture keeps its whole scale, so every pixel stays a square with no blur, and the view grows in game pixels till the window
/// is full - more of the world round the edges instead of black bars. Off: Ultima VII's own 320 x 200.
/// </summary>
internal static class FillScreenView
{
    public static (double Width, double Height) Size(bool fill, double hostWidth, double hostHeight, int scale, double baseWidth, double baseHeight)
    {
        if (!fill || scale < 1 || hostWidth <= 0 || hostHeight <= 0) return (baseWidth, baseHeight);
        return (Math.Max(baseWidth, Math.Floor(hostWidth / scale)), Math.Max(baseHeight, Math.Floor(hostHeight / scale)));
    }
}
