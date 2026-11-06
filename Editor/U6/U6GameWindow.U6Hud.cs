// Ultima 6 engine (U6GameWindow): the HUD (HUD Editor) in play - Ultima 6 projects only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The game's screen as the HUD Editor made it, as Ultima 6 draws its own (Nuvie): the frame over the whole 320 x 200
/// screen, the world only inside the Game View (the avatar always in its middle), the text area with what happens and
/// what the player types, the panel showing one member - inventory or portrait and stats - and the buttons. A project
/// without a Game View yet: the world fills the screen, as before.
/// </summary>
public partial class U6GameWindow
{
    private HudDesign Hud => _systemSettings.Ultima6.Hud;

    /// <summary>The whole screen (the HUD's size), and the world's part of it (the Game View).</summary>
    private double _screenW = 320, _screenH = 200, _viewW = 320, _viewH = 200;
    private double ScreenWidth => _screenW;
    private double ScreenHeight => _screenH;
    private double ViewportWidth => _viewW;
    private double ViewportHeight => _viewH;

    private readonly List<SpriteAnimator> _hudAnimators = new();
    private readonly List<SpriteAnimator> _hudPanelAnimators = new();
    /// <summary>The panel's pieces (rebuilt when the member or the view changes) and the words that change (date, text area).</summary>
    private readonly List<FrameworkElement> _hudPanelVisuals = new();
    private readonly Dictionary<int, Canvas> _hudWordPieces = new();
    private readonly Dictionary<int, (Image Image, SpriteEntry Sprite)> _hudFramePieces = new();
    private readonly Dictionary<int, Image> _hudButtons = new();
    /// <summary>The member shown in the panel (null: the main character) and which view - Ultima 6's inventory or portrait.</summary>
    private CharacterDefinition? _hudMember;
    private HudPanelView _hudView = HudPanelView.Inventory;
    /// <summary>The text area: lines already said (wrapped to its width), and the line being typed (a verb's prompt: "Move-").</summary>
    private readonly List<string> _hudLines = new();
    private string _hudPrompt = string.Empty;
    /// <summary>The verb picked (a command button or its key) waiting for its target; null: none.</summary>
    private HudVerb? _hudVerb;

    /// <summary>Sizes the screen and the world's window by the HUD, then draws it. Called once, as the window is made.</summary>
    private void InitU6Hud()
    {
        var hud = Hud;
        _screenW = Math.Max(160, hud.Width);
        _screenH = Math.Max(100, hud.Height);
        if (hud.First(HudPieceKind.GameView) is { } view)
        {
            _viewW = Math.Max(16, view.Width);
            _viewH = Math.Max(16, view.Height);
            WorldView.Margin = new Thickness(view.X, view.Y, 0, 0);
        }
        else
        {
            (_screenW, _screenH, _viewW, _viewH) = (320, 200, 320, 200); // no HUD yet: the world fills the screen
            WorldView.Margin = new Thickness(0);
        }
        WorldView.Width = _viewW;
        WorldView.Height = _viewH;
        foreach (var layer in new FrameworkElement[] { CloudLayer, WeatherLayer, LightingOverlay, FlashOverlay, MessageLayer })
        {
            layer.Width = _viewW;
            layer.Height = _viewH;
        }
        BuildHud();
    }

    private void BuildHud()
    {
        foreach (var animator in _hudAnimators) animator.Stop();
        _hudAnimators.Clear();
        _hudWordPieces.Clear();
        _hudFramePieces.Clear();
        _hudButtons.Clear();
        HudBackLayer.Children.Clear();
        HudLayer.Children.Clear();
        HudBackLayer.Width = HudLayer.Width = _screenW;
        HudBackLayer.Height = HudLayer.Height = _screenH;
        var hud = Hud;
        if (hud.First(HudPieceKind.GameView) is null) return;
        if (SpriteOfLink(hud.Frame) is { } frameSprite) HudBackLayer.Children.Add(HudImage(frameSprite, 0, 0, _screenW, _screenH, _hudAnimators));

        foreach (var piece in hud.Pieces)
        {
            switch (piece.Kind)
            {
                case HudPieceKind.GameView:
                    break;
                case HudPieceKind.Picture or HudPieceKind.Panel:
                    if (SpriteOfLink(piece.Sprite) is { } picture) HudLayer.Children.Add(HudImage(picture, piece.X, piece.Y, piece.Width, piece.Height, _hudAnimators));
                    break;
                case HudPieceKind.CommandButton or HudPieceKind.CustomButton:
                {
                    var p = piece;
                    FrameworkElement look;
                    if (SpriteOfLink(piece.Sprite) is { } sprite)
                    {
                        var image = HudImage(sprite, piece.X, piece.Y, piece.Width, piece.Height, _hudAnimators);
                        _hudButtons[piece.Id] = image;
                        look = image;
                    }
                    else
                    {
                        // No sprite yet: its verb's first letter in a box, so it can be clicked and seen.
                        var box = new Border { Width = piece.Width, Height = piece.Height, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.Black };
                        box.Child = HudWords(piece.Kind == HudPieceKind.CommandButton ? piece.Verb.ToString()[..1] : piece.Name[..Math.Min(1, piece.Name.Length)], null);
                        Canvas.SetLeft(box, piece.X);
                        Canvas.SetTop(box, piece.Y);
                        look = box;
                    }
                    look.IsHitTestVisible = true;
                    look.Cursor = Cursors.Hand;
                    look.MouseLeftButtonDown += (_, e) => { e.Handled = true; HudButtonClicked(p); };
                    HudLayer.Children.Add(look);
                    break;
                }
                case HudPieceKind.Compass or HudPieceKind.SunMoon:
                    if (SpriteOfLink(piece.Sprite) is { } dial)
                    {
                        var image = HudImage(dial, piece.X, piece.Y, piece.Width, piece.Height, null);
                        _hudFramePieces[piece.Id] = (image, dial);
                        HudLayer.Children.Add(image);
                    }
                    break;
                case HudPieceKind.TextArea or HudPieceKind.DateText:
                {
                    var holder = new Canvas { Width = piece.Width, Height = piece.Height, ClipToBounds = true, IsHitTestVisible = false };
                    Canvas.SetLeft(holder, piece.X);
                    Canvas.SetTop(holder, piece.Y);
                    _hudWordPieces[piece.Id] = holder;
                    HudLayer.Children.Add(holder);
                    break;
                }
            }
        }
        RefreshHudPanel();
        RefreshHudText();
        RefreshHudClock();
    }

    // ---------- The panel: one member, inventory or portrait ----------

    private CharacterDefinition? HudMember => _hudMember is { } m && (m == _player || m.IsInParty) ? m : _player;

    /// <summary>Draws the panel's pieces for the shown member in the shown view (Ultima 6: one member at a time).</summary>
    private void RefreshHudPanel()
    {
        foreach (var animator in _hudPanelAnimators) animator.Stop();
        _hudPanelAnimators.Clear();
        foreach (var visual in _hudPanelVisuals) HudLayer.Children.Remove(visual);
        _hudPanelVisuals.Clear();
        if (Hud.First(HudPieceKind.GameView) is null || HudMember is not { } member) return;
        foreach (var piece in Hud.Pieces)
        {
            var view = HudPiece.ViewOf(piece.Kind);
            if (view != HudPanelView.Always && view != _hudView) continue;
            FrameworkElement? visual = piece.Kind switch
            {
                HudPieceKind.MemberName => HudWordsAt(piece, member.Name),
                HudPieceKind.CombatMode => HudWordsAt(piece, "Command"),
                HudPieceKind.Stats => HudWordsAt(piece, string.Join("\n", (piece.StatIds.Count > 0 ? _stats.Stats.Where(s => piece.StatIds.Contains(s.Id)) : _stats.Stats)
                    .Select(s => $"{s.Name} {StatText(member, s.Id)}"))),
                HudPieceKind.Portrait => HudPortrait(piece, member),
                HudPieceKind.BodySlot => HudBodySlot(piece, member),
                HudPieceKind.CarriedItems => HudCarried(piece, member),
                _ => null
            };
            if (visual is null) continue;
            _hudPanelVisuals.Add(visual);
            HudLayer.Children.Add(visual);
        }
    }

    private FrameworkElement? HudPortrait(HudPiece piece, CharacterDefinition member)
    {
        if (member.FaceSpriteName.Length == 0 || FindSprite(member.FaceSpriteGroup, member.FaceSpriteName) is not { Frames.Count: > 0 } face) return null;
        return HudImage(face, piece.X, piece.Y, piece.Width, piece.Height, _hudPanelAnimators, fit: true);
    }

    private FrameworkElement HudBodySlot(HudPiece piece, CharacterDefinition member)
    {
        var holder = new Canvas { Width = piece.Width, Height = piece.Height, IsHitTestVisible = false };
        Canvas.SetLeft(holder, piece.X);
        Canvas.SetTop(holder, piece.Y);
        var worn = member.EquippedSlots.TryGetValue(piece.Slot, out int id) ? member.Inventory.FirstOrDefault(i => i.InstanceId == id) : null;
        var sprite = worn is not null ? DollLook.ItemSprite(worn.Category, worn.ItemId, _objects, _books, _containers, _sprites) : SpriteOfLink(piece.Sprite);
        if (sprite is not null) holder.Children.Add(HudImage(sprite, 0, 0, piece.Width, piece.Height, null, fit: true));
        return holder;
    }

    /// <summary>What the member carries and doesn't wear - and what's in the bag on their back - cell by cell.</summary>
    private FrameworkElement HudCarried(HudPiece piece, CharacterDefinition member)
    {
        int cell = Math.Max(4, piece.CellSize);
        var holder = new Canvas { Width = piece.Columns * cell, Height = piece.Rows * cell, IsHitTestVisible = false, ClipToBounds = true };
        Canvas.SetLeft(holder, piece.X);
        Canvas.SetTop(holder, piece.Y);
        var empty = SpriteOfLink(piece.Sprite);
        for (int i = 0; i < piece.Columns * piece.Rows; i++)
            if (empty is not null) holder.Children.Add(HudImage(empty, i % piece.Columns * cell, i / piece.Columns * cell, cell, cell, null));
        int n = 0;
        foreach (var item in HudCarriedItems(member))
        {
            if (n >= piece.Columns * piece.Rows) break;
            if (DollLook.ItemSprite(item.Category, item.ItemId, _objects, _books, _containers, _sprites) is { } sprite)
                holder.Children.Add(HudImage(sprite, n % piece.Columns * cell, n / piece.Columns * cell, cell, cell, null, fit: true));
            n++;
        }
        return holder;
    }

    private static IEnumerable<InventoryItemInstance> HudCarriedItems(CharacterDefinition member)
    {
        var worn = member.EquippedSlots.Values.ToHashSet();
        foreach (var item in member.Inventory.Where(i => !worn.Contains(i.InstanceId))) yield return item;
        if (member.EquippedSlots.TryGetValue(EquipmentSlot.Backpack, out int pack) && member.Inventory.FirstOrDefault(i => i.InstanceId == pack) is { } bag)
            foreach (var inside in bag.Contents) yield return inside;
    }

    // ---------- The text area and the date ----------

    /// <summary>Ultima 6: what happens is told in the text area ("You see a wall.", "Chicken attacks rat.").</summary>
    private void U6Say(string text)
    {
        if (Hud.First(HudPieceKind.TextArea) is not { } area) return;
        foreach (var paragraph in text.Replace("\r", "").Split('\n')) _hudLines.AddRange(HudWrap(paragraph, area.Width, area.FontName));
        int keep = Math.Max(1, area.Height / Math.Max(1, HudLineHeight(area.FontName)) * 4); // a few screens of history
        if (_hudLines.Count > keep) _hudLines.RemoveRange(0, _hudLines.Count - keep);
        RefreshHudText();
    }

    private void RefreshHudText()
    {
        if (Hud.First(HudPieceKind.TextArea) is not { } area || !_hudWordPieces.TryGetValue(area.Id, out var holder)) return;
        holder.Children.Clear();
        int lineH = HudLineHeight(area.FontName);
        var lines = _hudLines.ToList();
        lines.AddRange(HudWrap(_hudPrompt + (_hudVerb is not null || _hudPrompt.Length > 0 ? "_" : ""), area.Width, area.FontName));
        int fit = Math.Max(1, area.Height / Math.Max(1, lineH));
        double y = 0;
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - fit)))
        {
            var words = HudWords(line, area);
            Canvas.SetTop(words, y);
            holder.Children.Add(words);
            y += lineH;
        }
    }

    /// <summary>Each game minute: the date and wind words, the compass and the sun and moons.</summary>
    private void RefreshHudClock()
    {
        string wind = HudWind();
        foreach (var piece in Hud.Pieces)
        {
            if (piece.Kind == HudPieceKind.DateText && _hudWordPieces.TryGetValue(piece.Id, out var holder))
            {
                holder.Children.Clear();
                int month = _clock.Day / 28 % 12 + 1, day = _clock.Day % 28 + 1, year = 161 + _clock.Day / 336; // Britannia: 12 months of 28 days, from 161
                holder.Children.Add(HudWords($"{month}-{day}-{year:0000} Wind:{wind}", piece));
            }
            else if (_hudFramePieces.TryGetValue(piece.Id, out var dial))
            {
                int frames = Math.Max(1, dial.Sprite.Frames.Count);
                int frame = piece.Kind == HudPieceKind.Compass
                    ? Array.IndexOf(WindNames, wind) is int w and >= 0 ? w * frames / 8 : 0
                    : (_clock.Hour * 60 + _clock.Minute) * frames / (24 * 60);
                if (DollLook.Frame(dial.Sprite, Math.Clamp(frame, 0, frames - 1)) is { } bitmap) dial.Image.Source = bitmap;
            }
        }
    }

    private static readonly string[] WindNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>The wind: turns now and then through the day (Ultima 6 shows it beside the date); "C" while calm.</summary>
    private string HudWind()
    {
        int hourOfGame = _clock.Day * 24 + _clock.Hour;
        int roll = (int)((uint)(hourOfGame / 3 * 2654435761u) >> 24) % 10;
        return roll == 0 ? "C" : WindNames[roll % 8];
    }

    // ---------- The buttons ----------

    private void HudButtonClicked(HudPiece piece)
    {
        if (piece.Kind == HudPieceKind.CommandButton) StartHudVerb(piece.Verb);
        else RunHudCustomButton(piece);
    }

    /// <summary>A verb picked: its prompt in the text area ("Move-"), waiting for what it's done to.</summary>
    private void StartHudVerb(HudVerb verb)
    {
        _hudVerb = verb;
        _hudPrompt = VerbWord(verb) + "-";
        ShowHudPressed(verb);
        RefreshHudText();
    }

    private void EndHudVerb()
    {
        _hudVerb = null;
        _hudPrompt = string.Empty;
        ShowHudPressed(null);
        RefreshHudText();
    }

    private static string VerbWord(HudVerb verb) => verb switch
    {
        HudVerb.Combat => "Combat",
        _ => verb.ToString()
    };

    /// <summary>The picked verb's button in its pressed look (its pressed sprite, else its own darkened).</summary>
    private void ShowHudPressed(HudVerb? verb)
    {
        foreach (var piece in Hud.Pieces.Where(p => p.Kind == HudPieceKind.CommandButton))
        {
            if (!_hudButtons.TryGetValue(piece.Id, out var image)) continue;
            bool pressed = verb == piece.Verb;
            var sprite = pressed && SpriteOfLink(piece.PressedSprite) is { } down ? down : SpriteOfLink(piece.Sprite);
            if (pressed && DollLook.Frame(sprite) is { } bitmap) image.Source = bitmap;
            image.Opacity = pressed && !piece.PressedSprite.IsSet ? 0.55 : 1;
        }
    }

    private readonly HashSet<int> _hudButtonsRunning = new();

    /// <summary>A custom button: its event, once - again on the next click once it has finished (Always, Only Once).</summary>
    private void RunHudCustomButton(HudPiece piece)
    {
        if (_hudButtonsRunning.Contains(piece.Id) || piece.EventPages.Count == 0 || piece.EventPages[0].Commands.Count == 0) return;
        _hudButtonsRunning.Add(piece.Id);
        RunHudButtonCommands(piece.EventPages[0].Commands, () => _hudButtonsRunning.Remove(piece.Id));
    }

    // ---------- Drawing helpers ----------

    private SpriteEntry? SpriteOfLink(SpriteLink link) => link.IsSet ? DollLook.SpriteOf(link, _sprites) : null;

    /// <summary>A sprite on the HUD at its place and size, animated when it has frames and a list to keep its animator in.</summary>
    private Image HudImage(SpriteEntry sprite, double x, double y, double w, double h, List<SpriteAnimator>? animators, bool fit = false)
    {
        var image = new Image { Width = w, Height = h, Stretch = fit ? Stretch.Uniform : Stretch.Fill, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        if (sprite.Frames.Count > 1 && animators is not null) animators.Add(new SpriteAnimator(sprite, image, _resources, 0));
        else image.Source = DollLook.Frame(sprite);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        return image;
    }

    private SpriteFont? HudFont(string name) => name.Length > 0 ? SpriteFont.For(_sprites, name) ?? InGameFont : InGameFont;

    private int HudLineHeight(string fontName) =>
        HudFont(fontName)?.RenderText("Ag", 1, null) is { } probe ? Math.Max(6, probe.PixelHeight + 1) : 8;

    /// <summary>One line of words in the piece's font and colour (InGameFont when none; a plain small font until it has glyphs).</summary>
    private FrameworkElement HudWords(string text, HudPiece? piece)
    {
        Brush? tint = piece is { TextColor.Length: > 0 } && ColorConverter.ConvertFromString(piece.TextColor) is Color c ? new SolidColorBrush(c) : null;
        if (text.Length > 0 && HudFont(piece?.FontName ?? "")?.RenderText(text, 1, tint) is { } bitmap)
        {
            var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.None, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }
        return new TextBlock { Text = text, Foreground = tint ?? Brushes.White, FontSize = 7, FontFamily = new FontFamily("Consolas"), IsHitTestVisible = false };
    }

    private FrameworkElement HudWordsAt(HudPiece piece, string text)
    {
        var holder = new Canvas { Width = piece.Width, Height = piece.Height, ClipToBounds = true, IsHitTestVisible = false };
        Canvas.SetLeft(holder, piece.X);
        Canvas.SetTop(holder, piece.Y);
        int lineH = HudLineHeight(piece.FontName);
        double y = 0;
        foreach (var line in text.Split('\n'))
        {
            var words = HudWords(line, piece);
            Canvas.SetTop(words, y);
            holder.Children.Add(words);
            y += lineH;
        }
        return holder;
    }

    private double HudTextWidth(string text, string fontName)
    {
        if (text.Length == 0) return 0;
        if (HudFont(fontName)?.RenderText(text, 1, null) is { } bitmap) return bitmap.PixelWidth;
        return text.Length * 4.2; // the plain 7-point stand-in
    }

    /// <summary>Words wrapped to the width, word by word (a word longer than the line is cut).</summary>
    private List<string> HudWrap(string text, double width, string fontName)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (HudTextWidth(candidate, fontName) <= width || line.Length == 0)
            {
                line = candidate;
                continue;
            }
            lines.Add(line);
            line = word;
        }
        lines.Add(line);
        return lines;
    }
}

public partial class U6GameWindow
{
    /// <summary>A custom button's commands, the main character setting them off; done() once they have all finished (waits and all).</summary>
    private void RunHudButtonCommands(List<EventCommand> commands, Action done)
    {
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = null;
        _activatedByCharacterStep = false;
        try
        {
            _execContext.SelfBookId = -1;
            EventCommandInterpreter.Run(commands, _execContext, new List<string>(), finished: done);
        }
        catch { done(); throw; }
        finally { (_activator, _activatedByCharacterStep) = (previous, previousStep); }
    }
}
