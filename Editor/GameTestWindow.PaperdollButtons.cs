using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The buttons on a character's paperdoll and the stats paperdoll, ported
/// from Exult (gumps/misc_buttons.cc, Actor_gump.cc, Stats_gump.cc):
///  - Combat (the player's only): toggles combat mode; frame 0 peace, 1 war.
///  - Combat behavior: each click goes to the next of Exult's attack modes
///    (Nearest, Weakest, Strongest, Berserk, Protect, Defend, Flank, Flee,
///    Random - and Manual, the player's alone); its frame is the mode.
///  - Heart: opens that character's stats paperdoll.
///  - Disk (the player's only): save / load.
///  - Halo: this one is protected - the party's Protect fighters go after its
///    attackers; turning it on turns it off for everyone else in the party.
/// The stats paperdoll (Z for the player's): the character's own layout,
/// redrawn twice a second while open.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Marks a paperdoll button picture, so a press on it is the button's - never the empty slot under it.</summary>
    private sealed record PaperdollButtonTag(PaperdollButton Button);

    /// <summary>One frame of a sprite (its last if there are fewer), or null.</summary>
    private System.Windows.Media.Imaging.BitmapSource? FrameOf(SpriteLink link, int frame)
    {
        var sprite = ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName);
        if (sprite is null) return null;
        return sprite.Frames.Count > 0 ? LoadStaticFrame(sprite.Frames[Math.Clamp(frame, 0, sprite.Frames.Count - 1)].FilePath) : LoadStaticFrame(sprite.FilePath);
    }

    /// <summary>The release happened inside the element's own box (Gump_button::mouse_up's on_button check).</summary>
    private static bool ReleasedOver(FrameworkElement element, MouseEventArgs e)
    {
        var p = e.GetPosition(element);
        return p.X >= 0 && p.Y >= 0 && p.X < element.ActualWidth && p.Y < element.ActualHeight;
    }

    private IEnumerable<CharacterDefinition> PartyAndPlayer() =>
        (_player is null ? Enumerable.Empty<CharacterDefinition>() : new[] { _player }).Concat(AllLivingCharacters().Where(c => c.IsInParty && c != _player)).Distinct();

    /// <summary>The paperdoll's buttons, each showing the frame for its state (Actor_gump's add_elem calls).</summary>
    private void AddPaperdollButtons(OpenPaperdollGump gump, double counterScale)
    {
        var character = gump.Character;
        var pd = character.Paperdoll;
        bool isPlayer = character == _player;
        void Add(PaperdollButton button, Func<int> frame, Action click)
        {
            if (!button.Enabled || FrameOf(button.Sprite, frame()) is not { } up) return;
            var image = new AlphaHitImage { Source = up, Width = up.PixelWidth * counterScale, Height = up.PixelHeight * counterScale, Stretch = Stretch.Fill, Cursor = Cursors.Hand, Tag = new PaperdollButtonTag(button) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            bool pressed = false;
            image.MouseLeftButtonDown += (_, e) =>
            {
                // Heart and disk show their pressed frame while held; the others show their state.
                if (button == pd.StatsButton || button == pd.DiskButton) image.Source = FrameOf(button.Sprite, 1) ?? up;
                pressed = true;
                image.CaptureMouse();
                e.Handled = true;
            };
            image.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!pressed) return; // Exult Gump_button: only a press that started on the button clicks it - never a release that merely ends over it
                pressed = false;
                image.ReleaseMouseCapture();
                image.Source = up;
                if (!ReleasedOver(image, e)) return; // dragged off before letting go: no click (Gump_button::mouse_up)
                PlaySound(button.ClickSoundResourceId);
                click();
                RefreshAllPaperdolls();
            };
            Canvas.SetLeft(image, button.X);
            Canvas.SetTop(image, button.Y);
            Panel.SetZIndex(image, 18);
            gump.BodyCanvas.Children.Add(image);
        }
        if (isPlayer) Add(pd.CombatButton, () => _combatMode ? 1 : 0, ToggleCombatMode);
        Add(pd.CombatModeButton, () => (int)character.AttackMode, () =>
        {
            // Combat_mode_button::activate: the next mode; only the player gets the last one (Manual).
            int modes = isPlayer ? 10 : 9;
            character.AttackMode = (AttackMode)(((int)character.AttackMode + 1) % modes);
            StatusText.Text = $"{character.Name} now fights: {character.AttackMode}.";
        });
        Add(pd.StatsButton, () => 0, () => OpenStatsDoll(character));
        if (isPlayer) Add(pd.DiskButton, () => 0, () => StatusText.Text = "Saving and loading a game come with the save system - not built yet.");
        Add(pd.HaloButton, () => character.CombatProtected ? 1 : 0, () =>
        {
            // Halo_button::activate: toggle; on, nobody else in the party is protected.
            character.CombatProtected = !character.CombatProtected;
            if (character.CombatProtected)
                foreach (var other in PartyAndPlayer().Where(c => c != character)) other.CombatProtected = false;
            StatusText.Text = character.CombatProtected ? $"The party protects {character.Name}." : $"{character.Name} is no longer protected.";
        });
    }

    private void RefreshAllPaperdolls()
    {
        foreach (var open in _openPaperdolls.ToList()) RefreshPaperdollGump(open);
    }

    // ================= Text and numbers on the paperdoll =================

    private DispatcherTimer? _labelTimer;

    private string PaperdollLabelText(PaperdollLabel label) => PaperdollReadouts.LabelText(label, _variables, _execContext.GetVariable);

    private string LabelsSignature(CharacterDefinition character) =>
        string.Join("\u0001", character.Paperdoll.Labels.Where(l => l.Kind == PaperdollLabelKind.Number).Select(PaperdollLabelText));

    /// <summary>Twice a second: an open paperdoll whose variable numbers changed is redrawn.</summary>
    private void WatchPaperdollLabels()
    {
        if (_labelTimer is not null) return;
        _labelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _labelTimer.Tick += (_, _) =>
        {
            foreach (var open in _openPaperdolls.ToList())
                if (open.Character.Paperdoll.Labels.Count > 0 && LabelsSignature(open.Character) != open.LabelsShown) RefreshPaperdollGump(open);
        };
        _labelTimer.Start();
    }

    // ================= The stats paperdoll =================

    private sealed class OpenStatsGump
    {
        public required CharacterDefinition Character;
        public required Border GumpBorder;
        public required Canvas Body;
        public Image? Close;
    }

    private readonly List<OpenStatsGump> _openStatsDolls = new();
    private DispatcherTimer? _statsTimer;

    /// <summary>Z (the player's) or the heart (anyone's): opens it - or closes it if it's already open.</summary>
    private void OpenStatsDoll(CharacterDefinition? character)
    {
        if (character is null) return;
        if (_openStatsDolls.FirstOrDefault(g => g.Character == character) is { } open) { CloseStatsDoll(open); return; }
        var doll = character.Paperdoll.StatsDoll;
        if (FrameOf(doll.Background, 0) is null && doll.Elements.Count == 0)
        {
            StatusText.Text = $"{character.Name} has no stats paperdoll yet (Character Editor > Inventory / Paperdoll > Stats paperdoll...).";
            return;
        }
        // A gump like any paperdoll: its own Open Sound, else the character's paperdoll Open Sound (System Settings' one sound for every paperdoll wins, as everywhere).
        PlayOpenSound(doll.OpenSoundResourceId >= 0 ? doll.OpenSoundResourceId : character.Paperdoll.OpenSoundResourceId, $"{character.Name}'s stats");
        var body = new Canvas { Background = null };
        var border = new Border { Child = body };
        var gump = new OpenStatsGump { Character = character, GumpBorder = border, Body = body };
        Canvas.SetLeft(border, 60 + _openStatsDolls.Count * 24);
        Canvas.SetTop(border, 40 + _openStatsDolls.Count * 20);
        // Dragged by its body like every gump (Gump_manager drag): it moves exactly as far as the mouse does, from where it was grabbed.
        Point? grabMouse = null;
        Point grabAt = default;
        body.MouseLeftButtonDown += (_, e) =>
        {
            BringStatsDollToFront(gump);
            grabMouse = e.GetPosition(PaperdollGumpsCanvas);
            grabAt = new Point(Canvas.GetLeft(border), Canvas.GetTop(border));
            body.CaptureMouse();
            e.Handled = true;
        };
        body.MouseMove += (_, e) =>
        {
            if (grabMouse is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(PaperdollGumpsCanvas);
            Canvas.SetLeft(border, grabAt.X + (p.X - start.X));
            Canvas.SetTop(border, grabAt.Y + (p.Y - start.Y));
        };
        body.MouseLeftButtonUp += (_, e) => { grabMouse = null; body.ReleaseMouseCapture(); e.Handled = true; };
        PaperdollGumpsCanvas.Children.Add(border);
        _openStatsDolls.Add(gump);
        BringStatsDollToFront(gump);
        RefreshStatsDoll(gump);
        if (_statsTimer is null)
        {
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statsTimer.Tick += (_, _) => { foreach (var g in _openStatsDolls.ToList()) RefreshStatsDoll(g); };
        }
        _statsTimer.Start();
    }

    private void BringStatsDollToFront(OpenStatsGump gump)
    {
        _openStatsDolls.Remove(gump);
        _openStatsDolls.Add(gump);
        for (int i = 0; i < _openStatsDolls.Count; i++) Panel.SetZIndex(_openStatsDolls[i].GumpBorder, 300 + i);
        TrackGumpFocus(gump); // Escape closes it like any other gump
    }

    private void CloseStatsDoll(OpenStatsGump gump)
    {
        PaperdollGumpsCanvas.Children.Remove(gump.GumpBorder);
        _openStatsDolls.Remove(gump);
        UntrackGumpFocus(gump);
        if (_openStatsDolls.Count == 0) _statsTimer?.Stop();
    }

    private string StatText(CharacterDefinition character, int statId) =>
        character.StatValues.TryGetValue(statId, out var text) && !string.IsNullOrWhiteSpace(text) ? text : "0";

    private double StatNumber(CharacterDefinition character, int statId) =>
        character.StatValues.TryGetValue(statId, out var text) && double.TryParse(text, out double v) ? v : 0;

    /// <summary>Stats_gump::paint: the background, then every element with the character's values now.</summary>
    private void RefreshStatsDoll(OpenStatsGump gump)
    {
        var character = gump.Character;
        var doll = character.Paperdoll.StatsDoll;
        // Everything but the close button is redrawn; the button stays, so holding it down across a redraw still closes it.
        foreach (var old in gump.Body.Children.OfType<UIElement>().Where(c => c != gump.Close).ToList()) gump.Body.Children.Remove(old);
        gump.Body.LayoutTransform = new ScaleTransform(ViewportScale.ScaleX, ViewportScale.ScaleY); // drawn at its own pixels, scaled like the world
        var background = FrameOf(doll.Background, 0);
        gump.Body.Width = background?.PixelWidth ?? 160;
        gump.Body.Height = background?.PixelHeight ?? 120;
        if (background is not null)
        {
            var image = new AlphaHitImage { Source = background, Width = background.PixelWidth, Height = background.PixelHeight, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            gump.Body.Children.Add(image);
        }
        else gump.Body.Background = new SolidColorBrush(Color.FromArgb(230, 40, 26, 16));
        _vitals.TryGetValue(character, out var vitals);
        int food = vitals?.Food ?? character.Survival.StartingFood;
        bool hungry = character.Survival.CanGetHungry && food <= character.Survival.HungryAtOrBelow;
        foreach (var element in doll.Elements)
        {
            double? fraction = element.Kind == StatsElementKind.Hunger ? food / (double)SurvivalSettings.MaxFood : null;
            var visual = StatsDollLook.Element(element, _sprites, character.Name, StatText(character, element.StatId), id => StatNumber(character, id),
                fraction, element.Kind == StatsElementKind.Ailment && Has(character, element.Ailment), hungry);
            if (visual is null) continue;
            visual.IsHitTestVisible = false;
            gump.Body.Children.Add(visual);
        }
        if (gump.Close is null && FrameOf(doll.CloseButton, 0) is { } up)
        {
            var close = new AlphaHitImage { Source = up, Width = up.PixelWidth, Height = up.PixelHeight, Stretch = Stretch.Fill, Cursor = Cursors.Hand };
            RenderOptions.SetBitmapScalingMode(close, BitmapScalingMode.NearestNeighbor);
            bool pressed = false;
            close.MouseLeftButtonDown += (_, e) => { close.Source = FrameOf(doll.CloseButton, 1) ?? up; pressed = true; close.CaptureMouse(); e.Handled = true; };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!pressed) return; // only a press that started on it
                pressed = false;
                close.ReleaseMouseCapture();
                close.Source = up;
                if (ReleasedOver(close, e)) CloseStatsDoll(gump);
            };
            Canvas.SetLeft(close, doll.CloseX);
            Canvas.SetTop(close, doll.CloseY);
            Panel.SetZIndex(close, 10);
            gump.Body.Children.Add(close);
            gump.Close = close;
        }
    }

    // ================= Combat behavior (combat.cc find_foe) =================

    /// <summary>Combat_schedule::find_foe(mode): of the foes it would fight, the one its attack mode picks.</summary>
    private CharacterDefinition? PickFoe(CharacterDefinition me, List<CharacterDefinition> foes)
    {
        if (foes.Count == 0) return null;
        CharacterDefinition Nearest() => foes.OrderBy(f => TilesBetween(me, f)).First();
        CharacterDefinition Weakest() => foes.OrderBy(f => GetHp(f) ?? 99).First();
        switch (me.AttackMode)
        {
            case AttackMode.Weakest: return Weakest();
            case AttackMode.Strongest: return foes.OrderByDescending(f => GetHp(f) ?? 0).First();
            case AttackMode.Random: return foes[Rng.Next(foes.Count)];
            case AttackMode.Berserk: return Rng.Next(2) == 0 ? foes[Rng.Next(foes.Count)] : Nearest(); // random or nearest, by chance
            case AttackMode.Protect:
            {
                // find_protected_attacker: the nearest one attacking the party member under the halo.
                var protectedOne = PartyAndPlayer().FirstOrDefault(c => c.CombatProtected);
                var attacker = protectedOne is null ? null : foes.Where(f => TargetOf(f) == protectedOne).OrderBy(f => TilesBetween(me, f)).FirstOrDefault();
                return attacker ?? foes[0];
            }
            case AttackMode.Defend:
                // Strong (more than half its health): the nearest; weak: the weakest.
                return Stat(me, BuiltInStatKind.Strength, 10) <= 2 * (GetHp(me) ?? 99) ? Nearest() : Weakest();
            case AttackMode.Flank:
                // find_attacked_opponent: one an ally is already fighting.
                return foes.Where(f => AllLivingCharacters().Any(a => a != me && SameSide(a, me) && TargetOf(a) == f)).OrderBy(f => TilesBetween(me, f)).FirstOrDefault() ?? Nearest();
            default:
                return Nearest();
        }
    }
}