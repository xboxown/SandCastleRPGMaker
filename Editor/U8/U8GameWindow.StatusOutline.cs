// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The coloured outline round a character with a lasting condition, ported
/// from Exult (actors.cc Actor::paint; colours from shapeid.cc
/// special_pixels, VGA 0-63 values times 4). Only one shows, in Exult's
/// order: charmed (light blue), paralyzed (purple), protected (pale white),
/// cursed (yellow), poisoned (green). A hit's red outline shows over it for
/// its moment (paint: "if (hit) ... else if (flags & ...)"). The invisible
/// aren't outlined - in the party they're drawn as a shimmer, otherwise not
/// at all.
/// </summary>
public partial class U8GameWindow
{
    private static readonly Color PoisonColor = Color.FromRgb(16, 252, 16);    // find_color(4, 63, 4)
    private static readonly Color ProtectColor = Color.FromRgb(248, 248, 220); // find_color(62, 62, 55)
    private static readonly Color CursedColor = Color.FromRgb(248, 248, 20);   // find_color(62, 62, 5)
    private static readonly Color CharmedColor = Color.FromRgb(120, 160, 252); // find_color(30, 40, 63)
    private static readonly Color ParalyzeColor = Color.FromRgb(196, 108, 196); // find_color(49, 27, 49)

    private sealed class StatusOutline
    {
        public required Image Outline;
        public ImageSource? Frame;
        public Color Color;
    }

    private readonly Dictionary<CharacterDefinition, StatusOutline> _statusOutlines = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(ImageSource Frame, Color Color), BitmapSource> _outlineCache = new();

    /// <summary>Actor::paint's order: charmed, paralyzed, protection, cursed, then poisoned. Null: no outline.</summary>
    private Color? StatusOutlineColor(CharacterDefinition character)
    {
        if (IsDead(character) || Has(character, Ailment.Invisible)) return null;
        if (Has(character, Ailment.Charmed)) return CharmedColor;
        if (Has(character, Ailment.Paralyzed)) return ParalyzeColor;
        if (Has(character, Ailment.Protected)) return ProtectColor;
        if (Has(character, Ailment.Cursed)) return CursedColor;
        if (Has(character, Ailment.Poisoned)) return PoisonColor;
        return null;
    }

    /// <summary>Every frame: each character's outline for its condition, round its current frame, wherever it is - or none.</summary>
    private void UpdateStatusOutlines()
    {
        var seen = new HashSet<CharacterDefinition>(ReferenceEqualityComparer.Instance);
        foreach (var character in AllLivingCharacters())
        {
            seen.Add(character);
            var visual = character == _player ? _playerImage : MapCharacterOf(character)?.Visual as Image;
            var color = StatusOutlineColor(character);
            bool show = color is not null && visual?.Source is BitmapSource && visual.Visibility == Visibility.Visible && visual.Opacity > 0 && visual.Width > 0
                        && !_hitOutlines.Any(h => ReferenceEquals(h.Visual, visual)); // the red hit outline instead, for its moment
            _statusOutlines.TryGetValue(character, out var outline);
            if (!show)
            {
                if (outline is not null) outline.Outline.Visibility = Visibility.Collapsed;
                continue;
            }
            var frame = (BitmapSource)visual!.Source;
            if (outline is null)
            {
                var image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                GameCanvas.Children.Add(image);
                _statusOutlines[character] = outline = new StatusOutline { Outline = image };
            }
            if (!ReferenceEquals(outline.Frame, frame) || outline.Color != color!.Value)
            {
                if (!_outlineCache.TryGetValue((frame, color!.Value), out var bitmap))
                {
                    if (_outlineCache.Count > 512) _outlineCache.Clear();
                    _outlineCache[(frame, color.Value)] = bitmap = OutlineOf(frame, color.Value);
                }
                outline.Outline.Source = bitmap;
                outline.Frame = frame;
                outline.Color = color.Value;
            }
            double scale = visual.Width / Math.Max(1, frame.PixelWidth);
            var source = (BitmapSource)outline.Outline.Source;
            outline.Outline.Width = source.PixelWidth * scale;
            outline.Outline.Height = source.PixelHeight * scale;
            Canvas.SetLeft(outline.Outline, Canvas.GetLeft(visual) - scale);
            Canvas.SetTop(outline.Outline, Canvas.GetTop(visual) - scale);
            Panel.SetZIndex(outline.Outline, Panel.GetZIndex(visual) + 1);
            outline.Outline.Visibility = Visibility.Visible;
        }
        foreach (var gone in _statusOutlines.Keys.Where(c => !seen.Contains(c)).ToList())
        {
            GameCanvas.Children.Remove(_statusOutlines[gone].Outline);
            _statusOutlines.Remove(gone);
        }
    }
}
