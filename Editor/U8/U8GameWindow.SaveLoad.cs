// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Saving and loading in play - Exult's File gump (Newfile_gump), as a
/// paperdoll the game's maker designs (System Settings > Save / Load). The S
/// key opens it, as in Exult; it opens with the paperdoll opening sound and,
/// like every paperdoll, pauses the game. Its built-in parts: Save slots
/// (Exult's own: each holds one save by its number - click one, type its
/// name, Save), the Save list (every save, newest first; click picks,
/// double-click loads, the wheel scrolls), the picked save's screenshot and details, and
/// the Save, Load and Delete save buttons. Writing over a save or deleting
/// one asks first (press the button again). The screenshot is taken as the
/// screen opens, before it covers the game (Exult's create_mini_screenshot).
///
/// A save holds everything the game has changed (GameSaveData); loading
/// builds a new game window from it, as a new game is built from the
/// project, and the old window closes - the editor's Game Test and a built
/// game both carry on with the new one (ReplacedBy).
/// </summary>
public partial class U8GameWindow
{
    private sealed class SaveLoadOwner : IHasDollState
    {
        public DollState? DollRuntime { get; set; }
    }

    private sealed class SaveScreen
    {
        public required OpenDollGump Gump;
        public required Guid ProjectId;
        public List<SaveSlotInfo> Saves = new();
        /// <summary>The save picked in the Save list (its row), or -1.</summary>
        public int Picked = -1;
        /// <summary>The Save slot picked (its number), or 0.</summary>
        public int PickedSlot;
        public int Scroll;
        /// <summary>The name being typed - into the picked Save slot (or, with only a Save list, the new save's).</summary>
        public string Name = string.Empty;
        public byte[]? Screenshot;
        /// <summary>A button pressed once to write over or delete a save: pressed again in time, it's done.</summary>
        public (DollPartKind Kind, string Folder, DateTime Until)? Confirming;
        public SaveSlotInfo? InSlot(int slot) => Saves.FirstOrDefault(s => s.Slot == slot);
        public SaveSlotInfo? PickedSave => PickedSlot > 0 ? InSlot(PickedSlot) : Picked >= 0 && Picked < Saves.Count ? Saves[Picked] : null;
        /// <summary>The paperdoll's Save slots' numbers, in order.</summary>
        public List<int> Slots => Gump.Design.Parts.Where(p => p.Kind == DollPartKind.SaveName).Select(p => DollPart.SlotNumber(Gump.Design, p)).Distinct().Order().ToList();

        /// <summary>Picks a Save slot: its save's name is ready to keep or type over, and the list (if any) shows its save.</summary>
        public void PickSlot(int slot)
        {
            PickedSlot = slot;
            var save = InSlot(slot);
            Name = save?.Name ?? string.Empty;
            Picked = save is null ? -1 : Saves.IndexOf(save);
            Confirming = null;
        }

        /// <summary>Picks a row of the Save list (a save in a slot picks that slot too).</summary>
        public void PickRow(int row)
        {
            Picked = row;
            PickedSlot = Saves[row].Slot > 0 && Slots.Contains(Saves[row].Slot) ? Saves[row].Slot : 0;
            Name = Saves[row].Name;
            Confirming = null;
        }
    }

    private readonly SaveLoadOwner _saveLoadOwner = new();
    private SaveScreen? _saveScreen;
    private const int MaxSaveNameLength = 40;

    /// <summary>Set when a save was loaded: the window that carries on the game (this one has closed).</summary>
    public U8GameWindow? ReplacedBy { get; private set; }


    // ================= Opening =================

    private void OpenSaveLoad()
    {
        if (_saveScreen is not null) { BringDollToFront(_saveScreen.Gump); return; }
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.SaveLoad.DollDesignId) is not { } design)
        {
            if (!GameError(GameErrors.NoSaveScreen)) StatusText.Text = "This game has no Save/Load screen yet (System Settings > Save / Load).";
            return;
        }
        if (ProjectSession.ProjectIdProvider?.Invoke() is not { } projectId)
        {
            GameError(GameErrors.ProjectNotSaved);
            return;
        }
        byte[]? shot = TakeScreenshot(); // before the screen covers the game
        _saveLoadOwner.DollRuntime = null; // it opens as designed every time
        OpenDoll(_saveLoadOwner, new WorldObject { Name = "Save / Load" }, design);
        if (_openDolls.LastOrDefault(g => ReferenceEquals(g.Owner, _saveLoadOwner)) is not { } gump) return;
        _saveScreen = new SaveScreen { Gump = gump, ProjectId = projectId, Saves = GameSaveStore.List(projectId), Screenshot = shot };
        SaveScreenOpenedMusic(); // the death music ends
        RefreshDollGump(gump);
        StatusText.Text = _saveScreen.Slots.Count > 0 ? "Click a slot: type a name and Save, or Load what it holds."
            : _saveScreen.Saves.Count == 0 ? "No saved games yet - type a name and press Save." : "Pick a save, or type a new name.";
    }

    /// <summary>The Save/Load screen closing (Close button, Escape): it forgets its list.</summary>
    private void SaveScreenClosed(OpenDollGump gump)
    {
        if (_saveScreen?.Gump == gump) _saveScreen = null;
    }

    private bool IsSaveScreen(OpenDollGump gump) => _saveScreen?.Gump == gump;

    /// <summary>The game's view as it is now, as a small PNG (the viewport's own size).</summary>
    private byte[]? TakeScreenshot()
    {
        try
        {
            if (ViewportBorder.ActualWidth <= 0 || ViewportBorder.ActualHeight <= 0) return null;
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
                dc.DrawRectangle(new VisualBrush(ViewportBorder) { Stretch = Stretch.Fill }, null, new Rect(0, 0, ViewportWidth, ViewportHeight));
            var bitmap = new RenderTargetBitmap((int)ViewportWidth, (int)ViewportHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch { return null; } // no screenshot: the save is still made
    }

    // ================= Drawing =================

    /// <summary>A Save list, name, screenshot or details part: its area, its look behind (the screenshot's frame over), what it shows.</summary>
    private FrameworkElement SaveScreenPartVisual(OpenDollGump gump, DollPart part, Size size)
    {
        var holder = new Canvas { Width = size.Width, Height = size.Height, Background = Brushes.Transparent, ClipToBounds = true };
        var look = DollLook.LookSprite(part, 0, _sprites);
        if (look is not null && part.Kind != DollPartKind.SaveScreenshot) holder.Children.Add(AnimatedLook(gump, look));
        var screen = IsSaveScreen(gump) ? _saveScreen : null;
        switch (part.Kind)
        {
            case DollPartKind.SaveList:
            {
                if (screen is null) break;
                double rowHeight = SaveRowHeight(part);
                int rows = Math.Max(1, (int)(size.Height / rowHeight));
                screen.Scroll = Math.Clamp(screen.Scroll, 0, Math.Max(0, screen.Saves.Count - rows));
                if (screen.Saves.Count == 0) AddText(holder, part, "(no saved games)", 2, 1, 0.6);
                for (int i = screen.Scroll; i < Math.Min(screen.Saves.Count, screen.Scroll + rows); i++)
                {
                    double y = (i - screen.Scroll) * rowHeight;
                    if (i == screen.Picked)
                    {
                        FrameworkElement mark = DollLook.LookSprite(part, 1, _sprites) is { } picked ? AnimatedLook(gump, picked)
                            : new Border { Width = size.Width, Height = rowHeight, Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) };
                        Canvas.SetTop(mark, y);
                        mark.IsHitTestVisible = false;
                        holder.Children.Add(mark);
                    }
                    AddText(holder, part, screen.Saves[i].Name, 2, y + 1);
                }
                holder.MouseWheel += (_, e) =>
                {
                    if (_saveScreen is not { } s) return;
                    s.Scroll += e.Delta > 0 ? -1 : 1;
                    RefreshDollGump(gump);
                    e.Handled = true;
                };
                break;
            }
            case DollPartKind.SaveName:
            {
                if (screen is null) break;
                int slot = DollPart.SlotNumber(gump.Design, part);
                bool picked = screen.PickedSlot == slot;
                if (picked)
                {
                    FrameworkElement mark = DollLook.LookSprite(part, 1, _sprites) is { } pickedLook ? AnimatedLook(gump, pickedLook)
                        : new Border { Width = size.Width, Height = size.Height, Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) };
                    mark.IsHitTestVisible = false;
                    holder.Children.Add(mark);
                }
                else if (look is null) // no sprite of its own: a faint outline, so an empty slot can still be found
                    holder.Children.Add(new Border { Width = size.Width, Height = size.Height, BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1), IsHitTestVisible = false });
                string text = picked ? screen.Name + "_" : screen.InSlot(slot)?.Name ?? string.Empty; // an empty slot is blank, as in Exult
                AddText(holder, part, text, 2, Math.Max(0, (size.Height - SaveRowHeight(part)) / 2) + 1);
                break;
            }
            case DollPartKind.SaveScreenshot:
            {
                if (screen?.PickedSave is { } save && LoadScreenshot(save.ScreenshotPath) is { } shot)
                    holder.Children.Add(new Image { Source = shot, Width = size.Width, Height = size.Height, Stretch = Stretch.Uniform, IsHitTestVisible = false });
                if (look is not null) holder.Children.Add(AnimatedLook(gump, look)); // its frame, over the picture
                break;
            }
            case DollPartKind.SaveDetails:
            {
                if (screen?.PickedSave is not { } save) break;
                double y = 1;
                foreach (var line in new[] { save.Name, save.GameTime, $"Saved {save.SavedAtUtc.ToLocalTime():g}" })
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    AddText(holder, part, line, 2, y);
                    y += SaveRowHeight(part);
                }
                break;
            }
        }
        return holder;
    }

    /// <summary>Words in the part's font (or a plain one, with no font in the project).</summary>
    private void AddText(Canvas holder, DollPart part, string text, double x, double y, double opacity = 1)
    {
        FrameworkElement words;
        if (DollLook.TextPicture(part, text, _sprites) is { } picture)
        {
            var image = new Image { Source = picture, Width = picture.PixelWidth, Height = picture.PixelHeight, Stretch = Stretch.None };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            words = image;
        }
        else words = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 8 * Math.Max(1, part.TextScale) };
        words.IsHitTestVisible = false;
        words.Opacity = opacity;
        Canvas.SetLeft(words, x);
        Canvas.SetTop(words, y);
        holder.Children.Add(words);
    }

    /// <summary>One row of a save list: its font's line, and a little room.</summary>
    private double SaveRowHeight(DollPart part) =>
        (DollLook.TextPicture(part, "Ag", _sprites)?.PixelHeight ?? 10 * Math.Max(1, part.TextScale)) + 2;

    private static BitmapImage? LoadScreenshot(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // not kept open: the save can be written over or deleted
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    // ================= Clicks and keys =================

    /// <summary>A click on one of the Save/Load screen's own parts. True = handled.</summary>
    private bool SaveScreenPartClicked(OpenDollGump gump, DollPart part, DollPartRuntime runtime)
    {
        if (!DollPart.IsSaveLoadKind(part.Kind)) return false;
        if (_saveScreen is not { } screen || screen.Gump != gump)
        {
            StatusText.Text = $"\"{part.Name}\" works on the Save/Load screen (System Settings > Save / Load).";
            return true;
        }
        switch (part.Kind)
        {
            case DollPartKind.SaveList:
            {
                if (DollPartVisual(gump, part.Id) is not { } visual) return true;
                int row = screen.Scroll + (int)(Mouse.GetPosition(visual).Y / SaveRowHeight(part));
                if (row >= 0 && row < screen.Saves.Count)
                {
                    screen.PickRow(row);
                    RefreshDollGump(gump);
                }
                return true;
            }
            case DollPartKind.SaveName:
            {
                int slot = DollPart.SlotNumber(gump.Design, part);
                if (screen.PickedSlot != slot)
                {
                    screen.PickSlot(slot);
                    RefreshDollGump(gump);
                }
                StatusText.Text = screen.InSlot(slot) is { } save ? $"Slot {slot}: \"{save.Name}\" - Load it, or type a new name and Save over it." : $"Slot {slot} is empty - type a name and press Save.";
                return true;
            }
            case DollPartKind.SaveButton or DollPartKind.LoadButton or DollPartKind.DeleteSaveButton:
                PressDollButton(gump, runtime);
                switch (part.Kind)
                {
                    case DollPartKind.SaveButton: SaveFromScreen(screen, part); break;
                    case DollPartKind.LoadButton: // its Pressed look shows first, then the game loads
                        AfterPress(() => { if (_saveScreen == screen) LoadFromScreen(screen); });
                        break;
                    default: DeleteFromScreen(screen, part); break;
                }
                if (_openDolls.Contains(gump)) FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" pressed.");
                return true;
        }
        return true;
    }

    /// <summary>A button's Pressed look for a moment.</summary>
    private void PressDollButton(OpenDollGump gump, DollPartRuntime runtime)
    {
        runtime.Look = 1;
        RefreshDollGump(gump);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            runtime.Look = 0;
            if (_openDolls.Contains(gump)) RefreshDollGump(gump);
        };
        timer.Start();
    }

    /// <summary>Runs once a pressed button has shown its Pressed look (and been drawn).</summary>
    private static void AfterPress(Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }

    private InteractionTarget? _lastDollPartClick;
    private long _lastDollPartClickTick;

    /// <summary>A part acted on at once, clicked twice in time: only a save (in the list or a slot) does more - it loads.</summary>
    private void DollPartDoubleClicked(DollPartTarget target)
    {
        if (_openDolls.Contains(target.Gump) && target.Gump.Design.Part(target.PartId) is { } part) SaveScreenPartUsed(target.Gump, part);
    }

    /// <summary>Double-clicking a save in the list, or a Save slot holding one, loads it.</summary>
    private bool SaveScreenPartUsed(OpenDollGump gump, DollPart part)
    {
        if (part.Kind is not (DollPartKind.SaveList or DollPartKind.SaveName) || _saveScreen is not { } screen || screen.Gump != gump) return false;
        if (screen.PickedSave is not null) LoadFromScreen(screen);
        return true;
    }

    /// <summary>Keys while the Save/Load screen is open: typing the name, Erase, Accept saves, Previous / Next choice pick. True = handled.</summary>
    private bool SaveScreenKey(Key key)
    {
        if (_saveScreen is not { } screen) return false;
        bool up = TypingKeyIs(key, "ScreenUp"), down = TypingKeyIs(key, "ScreenDown");
        switch (key)
        {
            case var _ when TypingKeyIs(key, "Erase"):
                if (screen.Name.Length > 0) screen.Name = screen.Name[..^1];
                break;
            case var _ when TypingKeyIs(key, "Accept"):
                SaveFromScreen(screen, null);
                return true;
            case var _ when up || down:
            {
                int step = up ? -1 : 1;
                var slots = screen.Slots;
                if (slots.Count > 0 && (screen.PickedSlot > 0 || !HasPart(screen, DollPartKind.SaveList))) // from slot to slot
                {
                    int at = slots.IndexOf(screen.PickedSlot);
                    screen.PickSlot(slots[at < 0 ? 0 : Math.Clamp(at + step, 0, slots.Count - 1)]);
                    break;
                }
                if (screen.Saves.Count == 0) return true;
                screen.PickRow(Math.Clamp(screen.Picked + step, 0, screen.Saves.Count - 1));
                if (screen.Picked < screen.Scroll) screen.Scroll = screen.Picked;
                break;
            }
            default:
                return false; // typed letters arrive as text (SaveScreenText); Cancel closes it as any paperdoll
        }
        screen.Confirming = null;
        RefreshDollGump(screen.Gump);
        return true;
    }

    private void SaveScreenText(object sender, TextCompositionEventArgs e)
    {
        if (e.Handled || _saveScreen is not { } screen || string.IsNullOrEmpty(e.Text) || TopModalDoll is not null) return;
        e.Handled = true;
        if (screen.PickedSlot == 0 && screen.Slots.Count > 0 && !HasPart(screen, DollPartKind.SaveList)) return; // no slot picked: the letters go nowhere (Exult)
        foreach (char c in e.Text)
            if (!char.IsControl(c) && screen.Name.Length < MaxSaveNameLength && !Path.GetInvalidFileNameChars().Contains(c)) screen.Name += c;
        screen.Confirming = null;
        RefreshDollGump(screen.Gump);
        e.Handled = true;
    }

    /// <summary>A second press of the same button on the same save within 4 seconds confirms it; the first one asks.</summary>
    private bool Confirmed(SaveScreen screen, DollPartKind kind, string folder, DollPart? part, string question)
    {
        if (screen.Confirming is { } pending && pending.Kind == kind && pending.Folder == folder && DateTime.Now <= pending.Until)
        {
            screen.Confirming = null;
            return true;
        }
        screen.Confirming = (kind, folder, DateTime.Now.AddSeconds(4));
        StatusText.Text = question;
        if (part is not null) ShowGumpLabel(screen.Gump.PartsCanvas, question, part.X, part.Y);
        return false;
    }

    // ================= Save, load, delete =================

    private static bool HasPart(SaveScreen screen, DollPartKind kind) => screen.Gump.Design.Parts.Any(p => p.Kind == kind);

    private void SaveFromScreen(SaveScreen screen, DollPart? button)
    {
        int slot = screen.PickedSlot;
        if (slot == 0 && screen.Slots.Count > 0 && !HasPart(screen, DollPartKind.SaveList)) { SaveScreenError(screen, GameErrors.PickSlotToSave, "", button); return; }
        string name = screen.Name.Trim();
        if (name.Length == 0) { SaveScreenError(screen, GameErrors.NeedsName, "", button); return; }
        if (WhatIsHappening() is { } happening) { SaveScreenError(screen, GameErrors.CantSaveNow, happening, button); return; }
        var existing = slot > 0 ? screen.InSlot(slot) // a slot is written over whatever its save was called
            : screen.Saves.FirstOrDefault(s => s.Slot == 0 && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null) WriteSave(screen, slot, name, null, button);
        else AskThenDo(screen, DollPartKind.SaveButton, existing.Folder, button, $"Write over \"{existing.Name}\"? Press Save again.",
            () => WriteSave(screen, slot, name, existing, button));
    }

    /// <summary>
    /// Writing over or deleting a save asks first: the "Are you sure?"
    /// paperdoll (System Settings > Save / Load) when there is one - its Yes /
    /// OK goes ahead, its No / Cancel closes it, back to the Save/Load screen
    /// - else the same button pressed again.
    /// </summary>
    private void AskThenDo(SaveScreen screen, DollPartKind kind, string folder, DollPart? part, string question, Action go)
    {
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.SaveLoad.ConfirmDollDesignId) is { } prompt)
        {
            StatusText.Text = question.Replace(" Press Save again.", "").Replace(" Press Delete again.", "");
            OpenForm(prompt, modal: true, answered: yes => { if (yes && _saveScreen == screen) go(); }, fresh: true);
            return;
        }
        if (Confirmed(screen, kind, folder, part, question)) go();
    }

    private void WriteSave(SaveScreen screen, int slot, string name, SaveSlotInfo? existing, DollPart? button)
    {
        if (WhatIsHappening() is { } happening) { SaveScreenError(screen, GameErrors.CantSaveNow, happening, button); return; }
        try
        {
            BeginDiskProgress(); // the saving progress paperdoll, if there is one (DiskProgress.cs)
            var data = CaptureSave();
            ReportDiskProgress(0.15);
            var info = new SaveSlotInfo { Name = name, SavedAtUtc = DateTime.UtcNow, GameTime = _clock.ToString(), PlayerName = _player?.Name ?? string.Empty, Slot = slot };
            GameSaveStore.Write(screen.ProjectId, data, info, screen.Screenshot, existing, f => ReportDiskProgress(0.15 + 0.85 * f));
            // Exult's Newfile_gump::save: the screen stays open, nothing is picked any more (selected = -3) and the slot shows its new save
            screen.Saves = GameSaveStore.List(screen.ProjectId);
            screen.PickedSlot = 0;
            screen.Picked = -1;
            screen.Name = string.Empty;
            screen.Confirming = null;
            RefreshDollGump(screen.Gump);
            EndDiskProgress(() =>
            {
                if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.SaveLoad.SavedDollDesignId) is { } saved)
                {
                    StatusText.Text = $"Game saved: \"{name}\".";
                    OpenForm(saved, modal: true, answered: null, fresh: true); // the maker's own "Game saved" paperdoll, closed by its own button
                }
                else SaveScreenSay(screen, $"Game saved: \"{name}\".", button);
            });
        }
        catch (Exception ex)
        {
            CancelDiskProgress();
            SaveScreenError(screen, GameErrors.Classify(ex, GameErrors.SaveFailed), ex.Message, button);
        }
    }

    /// <summary>Why a save can't be made right now, or null.</summary>
    private string? WhatIsHappening() =>
        InCinema ? "a cutscene is playing"
        : IsShowingMessage ? "a message is showing"
        : _scriptActions.Values.Where(a => !a.AfterEvent).Select(a => a.Who.Name).ToList() is { Count: > 0 } busy ? $"{string.Join(", ", busy)} {(busy.Count == 1 ? "is" : "are")} still doing an event's command"
        : _scriptWaits.Count > 0 ? "an event is waiting"
        : IsPlayingVideo ? "a video is playing"
        : _gameOver ? "the game is over"
        : _savingForbidden ? "saving isn't allowed here"
        : null;

    /// <summary>Tells the player on the Save/Load screen itself (by the button or the picked slot) and in the status bar.</summary>
    private void SaveScreenSay(SaveScreen screen, string text, DollPart? near)
    {
        StatusText.Text = text;
        var part = near
            ?? screen.Gump.Design.Parts.FirstOrDefault(p => p.Kind == DollPartKind.SaveName && DollPart.SlotNumber(screen.Gump.Design, p) == screen.PickedSlot)
            ?? screen.Gump.Design.Parts.FirstOrDefault(p => p.Kind == DollPartKind.SaveButton);
        ShowGumpLabel(screen.Gump.PartsCanvas, text, part?.X ?? 8, part?.Y ?? 8);
    }

    private void LoadFromScreen(SaveScreen screen)
    {
        if (screen.PickedSave is not { } save) { SaveScreenError(screen, screen.PickedSlot > 0 ? GameErrors.EmptySlot : GameErrors.PickSaveToLoad, "", null); return; }
        GameSaveData data;
        BeginDiskProgress(); // the loading progress paperdoll, if there is one (DiskProgress.cs)
        try { data = GameSaveStore.Read(screen.ProjectId, save, ReportDiskProgress); }
        catch (Exception ex)
        {
            CancelDiskProgress();
            GameError(GameErrors.ClassifyLoad(ex), ex.Message);
            return;
        }
        LoadGame(data, $"Loaded \"{save.Name}\".");
        EndDiskProgress();
    }

    private void DeleteFromScreen(SaveScreen screen, DollPart part)
    {
        if (screen.PickedSave is not { } save) { SaveScreenError(screen, screen.PickedSlot > 0 ? GameErrors.EmptySlot : GameErrors.PickSaveToLoad, "", part); return; }
        AskThenDo(screen, DollPartKind.DeleteSaveButton, save.Folder, part, $"Delete \"{save.Name}\"? Press Delete again.", () => DeleteSave(screen, save));
    }

    private void DeleteSave(SaveScreen screen, SaveSlotInfo save)
    {
        try { GameSaveStore.Delete(save); }
        catch (Exception ex)
        {
            GameError(ex, GameErrors.DeleteFailed);
            return;
        }
        screen.Saves = GameSaveStore.List(screen.ProjectId);
        if (screen.PickedSlot > 0) screen.PickSlot(screen.PickedSlot); // the slot stays picked, empty now
        else
        {
            screen.Picked = Math.Min(screen.Picked, screen.Saves.Count - 1);
            screen.Name = screen.PickedSave?.Name ?? string.Empty;
        }
        RefreshDollGump(screen.Gump);
        StatusText.Text = $"Deleted \"{save.Name}\".";
    }

    // ================= What a save holds =================

    private static T JsonCopy<T>(T source) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(source))
        ?? throw new InvalidOperationException($"Couldn't copy the game's {typeof(T).Name}.");

    /// <summary>The save as the game is now (CaptureSave, U8GameWindow.U8Crusader.cs, writes it with the main character controlled).</summary>
    private GameSaveData CaptureSaveAsIs()
    {
        var map = JsonCopy(_map);
        map.StartX = _playerX;
        map.StartY = _playerY;
        map.StartFloor = MapFloors.FloorOfLift(Math.Max(0, _playerZ));
        foreach (var mapCharacter in _mapCharacters) // the floor each character is on now, and which character it is, by its own placement
            if (map.Characters.FirstOrDefault(c => c.Id == mapCharacter.Placement.Id) is { } placed)
            {
                placed.Floor = MapFloors.FloorOfLift(Math.Max(0, mapCharacter.Lift));
                placed.SavedCharacterIndex = SavedIndexOf(mapCharacter.Character);
            }
        foreach (var (dead, lying) in _bodies) // the dead: which character lies in which body
            if (map.Characters.FirstOrDefault(c => c.Id == lying.Placement.Id) is { } placed) placed.SavedCharacterIndex = SavedIndexOf(dead);

        var save = new GameSaveData
        {
            Map = map,
            Characters = JsonCopy(_characters ?? new CharacterDatabase()),
            Containers = JsonCopy(_containers),
            Factions = JsonCopy(_factions),
            Day = _clock.Day, Hour = _clock.Hour, Minute = _clock.Minute, Ticks = _clock.Ticks,
            PlayerPixelX = _playerPixelX, PlayerPixelY = _playerPixelY, PlayerLift = _playerZ, PlayerFacing = _playerFacing,
            CombatMode = _combatMode,
            ReadBooks = _hasBeenReadBooks.ToList(),
            Weather = JsonCopy(_weather),
            WorldStates = _worldStates.ToDictionary(kv => kv.Key, kv => JsonCopy(kv.Value)) // the other worlds as they were left
        };
        _execContext.SaveState(save);
        foreach (var (character, vitals) in _vitals)
            save.Vitals.Add(new SavedVitals
            {
                CharacterId = character.Id, CharacterIndex = SavedIndexOf(character), Food = vitals.Food, Cold = vitals.Cold, Heat = vitals.Heat, Ailments = (int)vitals.Ailments,
                PoisonRemainingMs = vitals.PoisonRemainingMs, SleepRemainingMs = vitals.SleepRemainingMs, ParalyzeRemainingMs = vitals.ParalyzeRemainingMs,
                CharmRemainingMs = vitals.CharmRemainingMs, CurseRemainingMs = vitals.CurseRemainingMs, InvisibleRemainingMs = vitals.InvisibleRemainingMs,
                ProtectedRemainingMs = vitals.ProtectedRemainingMs, FeignRemainingMs = vitals.FeignRemainingMs, BuiltStrength = vitals.BuiltStrength, BuiltDexterity = vitals.BuiltDexterity, BuiltIntelligence = vitals.BuiltIntelligence, U8Hunger = vitals.U8Hunger, HealClockMs = vitals.HealClockMs, FeignSoundId = vitals.FeignSoundId, RiseRemainingMs = vitals.RiseRemainingMs, Dead = vitals.Dead, Unconscious = vitals.Unconscious
            });
        foreach (var (owner, counts) in _firedCounts)
        {
            if (OwnerKey(owner) is not { } key || PagesOf(owner) is not { } pages) continue;
            foreach (var (page, count) in counts)
                if (pages.IndexOf(page) is >= 0 and var index && count > 0)
                    save.FiredPages.Add(new SavedFiredPage { Owner = key, PageIndex = index, Count = count });
        }
        return save;
    }

    /// <summary>Where a character is in the game's own list (the save's list is a copy of it, in the same order): -1 if it isn't there.</summary>
    private int SavedIndexOf(CharacterDefinition character) => _characters?.Characters.FindIndex(c => ReferenceEquals(c, character)) ?? -1;

    /// <summary>An event owner as a save names it - only those a save can find again (map events, characters, books, containers, placed objects).</summary>
    private static string? OwnerKey(object owner) => owner switch
    {
        GameEvent e => $"Event:{e.Id}",
        CharacterDefinition c => $"Character:{c.Id}",
        BookData b => $"Book:{b.Id}",
        ContainerData c => $"Container:{c.Id}",
        ObjectInstance o => $"Object:{o.InstanceId}",
        _ => null
    };

    private List<BookEventPage>? PagesOf(object owner) => owner switch
    {
        GameEvent e => e.EventPages,
        CharacterDefinition c => c.EventPages,
        BookData b => b.EventPages,
        ContainerData c => c.EventPages,
        ObjectInstance o => DefinitionOf(o)?.EventPages,
        _ => null
    };

    private object? OwnerOf(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int id)) return null;
        return parts[0] switch
        {
            "Event" => _map.Events.FirstOrDefault(e => e.Id == id),
            "Character" => _characters?.Characters.FirstOrDefault(c => c.Id == id),
            "Book" => BookDef(id),
            "Container" => ContainerDef(id),
            "Object" => _map.Objects.FirstOrDefault(o => o.InstanceId == id),
            _ => null
        };
    }

    /// <summary>Builds the game anew from a save (as a new game is built from the project) and hands over to it; this window closes.</summary>
    private void LoadGame(GameSaveData data, string status)
    {
        U8GameWindow next;
        try
        {
            var (bitmap, tileWidth, tileHeight, columns) = TilesetFor(data.Map);
            next = new U8GameWindow(data.Map, bitmap, tileWidth, tileHeight, columns, data.Map.Tileset.TilesetSpriteName, _resources, _sprites, _books, _objects,
                data.Characters, _poseDatabase, _tileProperties, _variables, _stats, data.Containers, _systemSettings, data.Factions) { IsShippedGame = IsShippedGame, Worlds = Worlds };
            next.ApplySave(data);
        }
        catch (Exception ex)
        {
            GameError(GameErrors.ClassifyLoad(ex), ex.Message);
            return;
        }
        next.Title = Title;
        next.WindowStartupLocation = WindowStartupLocation.Manual;
        next.Left = Left;
        next.Top = Top;
        next.Width = Width;
        next.Height = Height;
        next.WindowState = WindowState;
        next.Loaded += (_, _) => next.StatusText.Text = status;
        ReplacedBy = next;
        Close(); // whoever opened this window shows the next one (MainWindow)
    }

    /// <summary>A save's own state on top of the freshly built game: the clock, the main character's exact spot, variables, hunger and ailments, fired events, weather.</summary>
    private void ApplySave(GameSaveData data)
    {
        _clock.Restore(data.Day, data.Hour, data.Minute, data.Ticks);
        _lastClockMinute = TotalMinutes(); // hunger and the rest go on from the saved time, not from the new game's
        _playerPixelX = data.PlayerPixelX;
        _playerPixelY = data.PlayerPixelY;
        _playerX = (int)Math.Floor(_playerPixelX / _tileWidth);
        _playerY = (int)Math.Floor(_playerPixelY / _tileHeight);
        _playerZ = data.PlayerLift;
        _playerVisualLiftZ = data.PlayerLift;
        _playerFacing = data.PlayerFacing;
        _combatMode = data.CombatMode;
        if (_idlePose is not null) ApplyPoseSprite(_idlePose);
        SnapPlayerPosition();
        UpdateCamera();

        _execContext.LoadState(data);
        foreach (int book in data.ReadBooks) _hasBeenReadBooks.Add(book);
        _rising.Clear();
        foreach (var saved in data.Vitals)
        {
            var character = saved.CharacterIndex >= 0 && _characters is { } list && saved.CharacterIndex < list.Characters.Count && list.Characters[saved.CharacterIndex].Id == saved.CharacterId
                ? list.Characters[saved.CharacterIndex] // its own copy
                : _characters?.Characters.FirstOrDefault(c => c.Id == saved.CharacterId); // a save from before copies were told apart
            if (character is null) continue;
            var vitals = VitalsOf(character);
            vitals.Food = saved.Food;
            vitals.Cold = saved.Cold;
            vitals.Heat = saved.Heat;
            vitals.Ailments = (Ailment)saved.Ailments;
            vitals.PoisonRemainingMs = saved.PoisonRemainingMs;
            vitals.SleepRemainingMs = saved.SleepRemainingMs;
            vitals.ParalyzeRemainingMs = saved.ParalyzeRemainingMs;
            vitals.CharmRemainingMs = saved.CharmRemainingMs;
            vitals.CurseRemainingMs = saved.CurseRemainingMs;
            vitals.InvisibleRemainingMs = saved.InvisibleRemainingMs;
            vitals.ProtectedRemainingMs = saved.ProtectedRemainingMs;
            vitals.Dead = saved.Dead;
            vitals.Unconscious = saved.Unconscious;
            vitals.FeignRemainingMs = saved.FeignRemainingMs;
            (vitals.BuiltStrength, vitals.BuiltDexterity, vitals.BuiltIntelligence, vitals.U8Hunger, vitals.HealClockMs) = (saved.BuiltStrength, saved.BuiltDexterity, saved.BuiltIntelligence, saved.U8Hunger, saved.HealClockMs);
            vitals.FeignSoundId = saved.FeignSoundId;
            vitals.RiseRemainingMs = saved.RiseRemainingMs;
            if (saved.Dead && saved.RiseRemainingMs > 0) _rising.Add(character);
        }
        foreach (var fired in data.FiredPages)
        {
            if (OwnerOf(fired.Owner) is not { } owner || PagesOf(owner) is not { } pages || fired.PageIndex < 0 || fired.PageIndex >= pages.Count) continue;
            if (!_firedCounts.TryGetValue(owner, out var counts)) _firedCounts[owner] = counts = new Dictionary<BookEventPage, int>();
            counts[pages[fired.PageIndex]] = fired.Count;
        }
        if (data.Weather is not null) _weather = data.Weather;
        _worldStates = data.WorldStates.ToDictionary(kv => kv.Key, kv => kv.Value);
        RestoreControl(data); // who the player controlled (U8GameWindow.U8Crusader.cs)
    }
}
