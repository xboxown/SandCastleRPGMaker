// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's paperdoll (Pentagram's PaperdollGump): beside the body its stats - Strength, Intelligence, Dexterity,
/// armour class, hit points, mana, the weight carried / 10 - each label and its value (System Settings > Ultima 8: the
/// words and where). Its equipment is the character's paperdoll slots (weapon - the right hand, shield - the left,
/// head, arm - the hands, body, legs) and the backpack; the world goes on while it's open (U8GameWindow.Pause.cs).
/// </summary>
public partial class U8GameWindow
{
    private void AddU8PaperdollStats(OpenPaperdollGump gump)
    {
        var u8 = _systemSettings.Ultima8;
        if (!u8.ShowPaperdollStats) return;
        var c = gump.Character;
        int armour = WornArmor(c).Sum(a => Math.Max(0, a.Armor.ArmorPoints)) + (WieldedWeapon(c)?.Def?.Weapon.U8ArmourBonus ?? 0);
        double weight = ItemWeights.Carried(c, _containers, _books, _objects, excluding: null);
        int[] values =
        {
            Stat(c, BuiltInStatKind.Strength, 0), Stat(c, BuiltInStatKind.Intelligence, 0), Stat(c, BuiltInStatKind.Dexterity, 0),
            armour, GetHp(c) ?? 0, Stat(c, BuiltInStatKind.Mana, 0), (int)(weight / Math.Max(1, u8.WeightDivisor))
        };
        for (int i = 0; i < values.Length; i++)
        {
            string label = i < u8.StatLabels.Count ? u8.StatLabels[i] : string.Empty;
            double y = u8.StatsTopY + i * u8.StatsLineHeight;
            if (label.Length > 0) Place(GameTextLabel(label), u8.StatsLabelX, y, rightAligned: false);
            Place(GameTextLabel(values[i].ToString()), u8.StatsValueX, y, rightAligned: true);
        }

        void Place(FrameworkElement text, double x, double y, bool rightAligned)
        {
            // The paperdoll's layout is drawn at half size; the game's words at the world's: twice as big here, as its item pictures.
            text.IsHitTestVisible = false;
            text.LayoutTransform = new System.Windows.Media.ScaleTransform(2, 2);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(text, rightAligned ? x - text.DesiredSize.Width : x);
            Canvas.SetTop(text, y);
            Panel.SetZIndex(text, 5);
            gump.BodyCanvas.Children.Add(text);
        }
    }
}
