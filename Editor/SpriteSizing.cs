namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 7's own real sizing principle, per spec: a sprite's on-screen
/// size comes from its OWN real pixel dimensions, quantized up to the
/// nearest whole multiple of a base unit - never forced to fill a fixed
/// tile box regardless of its own real proportions. In the real Ultima 7,
/// every non-ground "shape" was sized as a whole number of its 8px base
/// tile unit (1 to 8 units per axis) - this uses that same 8px unit, so a
/// tiny sprite genuinely renders tiny, a multi-tile sprite genuinely
/// spans multiple tiles, and nothing gets stretched or squashed to match
/// a box it was never drawn for. Shared by BOTH the Map Editor and Game
/// Test specifically so what you see while placing something is what you
/// actually see in play - the exact mismatch that would exist if only
/// one of them used this.
/// </summary>
internal static class SpriteSizing
{
    /// <summary>Ultima 7's own real ground-tile size in pixels - the one place this number lives, so it's a single change if it ever needs to move.</summary>
    public const int BaseUnit = 8;

    /// <summary>Rounds a sprite's real pixel dimensions UP to the nearest whole multiple of BaseUnit - e.g. 12x13 real pixels becomes a 16x16 on-screen footprint, 32x32 stays exactly 32x32.</summary>
    public static (int Width, int Height) Quantize(int realWidth, int realHeight)
    {
        int width = ((Math.Max(1, realWidth) + BaseUnit - 1) / BaseUnit) * BaseUnit;
        int height = ((Math.Max(1, realHeight) + BaseUnit - 1) / BaseUnit) * BaseUnit;
        return (width, height);
    }
}
