// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The PaperDoll Editor's form controls in play (drawn by DollControls):
/// Yes / OK and No / Cancel buttons, text boxes, check boxes, radio buttons,
/// drop-down lists, list boxes, progress bars - each one's value kept in its
/// variable, read when its paperdoll opens and written whenever it changes,
/// so events read and set them as any variable. A paperdoll can also be
/// opened by an event (Open Paperdoll) or by the game itself (the save
/// prompts) - modal or not. A modal one is the only thing that can be
/// clicked or typed in until it closes; whatever it was opened over comes
/// back as it was. Its Yes / OK button answers yes, its No / Cancel button
/// (or Escape, or its Close button) answers no.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>The holder of a paperdoll opened by an event or by the game, not by an object: one per design, so it remembers how it was left.</summary>
    private sealed class FormOwner : IHasDollState
    {
        public DollState? DollRuntime { get; set; }
    }

    private readonly Dictionary<int, FormOwner> _formOwners = new();
    /// <summary>The text box being typed in.</summary>
    private (OpenDollGump Gump, DollPart Part)? _typing;
    /// <summary>The drop-down list open now.</summary>
    private (OpenDollGump Gump, int PartId)? _openDropDown;

    private OpenDollGump? TopModalDoll => _openDolls.Where(g => g.ModalLayer > 0).MaxBy(g => g.ModalLayer);

    // ================= Opening =================

    /// <summary>
    /// Opens a PaperDoll Editor paperdoll that isn't an object's - for an
    /// event, a save prompt. Modal: nothing else can be clicked until it
    /// closes. <paramref name="answered"/> hears yes (its Yes / OK button) or
    /// no (No / Cancel, Escape, closed any other way). Fresh: as designed,
    /// not as it was last left.
    /// </summary>
    private OpenDollGump? OpenForm(DollDesign design, bool modal, Action<bool>? answered, bool fresh, bool title = false)
    {
        if (!_formOwners.TryGetValue(design.Id, out var owner)) _formOwners[design.Id] = owner = new FormOwner();
        if (fresh)
        {
            foreach (var open in _openDolls.Where(g => ReferenceEquals(g.Owner, owner)).ToList()) CloseDollGump(open);
            owner.DollRuntime = null;
        }
        OpenDoll(owner, new WorldObject { Name = design.Name }, design, title);
        if (_openDolls.LastOrDefault(g => ReferenceEquals(g.Owner, owner)) is not { } gump) return null;
        if (answered is not null)
        {
            var before = gump.Answered; // opened again while still asking: the first asker hears no
            gump.Answered = answered;
            before?.Invoke(false);
        }
        if (modal && gump.ModalLayer == 0)
        {
            gump.ModalLayer = 1000 + 2 * _openDolls.Count(g => g.ModalLayer > 0);
            // Everything under it - the game, other windows - is out of reach while it's open.
            gump.Blocker = new Border { Width = 40000, Height = 40000, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            Canvas.SetLeft(gump.Blocker, -20000);
            Canvas.SetTop(gump.Blocker, -20000);
            Panel.SetZIndex(gump.Blocker, gump.ModalLayer);
            PaperdollGumpsCanvas.Children.Add(gump.Blocker);
            // In the middle of the game's view, as a question box.
            double width = design.Width * DollGumpScale, height = design.Height * DollGumpScale;
            Canvas.SetLeft(gump.GumpBorder, Math.Max(0, (PaperdollGumpsCanvas.ActualWidth - width) / 2));
            Canvas.SetTop(gump.GumpBorder, Math.Max(0, (PaperdollGumpsCanvas.ActualHeight - height) / 2));
            BringDollToFront(gump);
        }
        return gump;
    }

    /// <summary>Its Yes / OK or No / Cancel: it closes and whoever opened it hears the answer.</summary>
    private void AnswerDoll(OpenDollGump gump, bool yes)
    {
        var answered = gump.Answered;
        gump.Answered = null;
        if (_openDolls.Contains(gump)) CloseDollGump(gump);
        answered?.Invoke(yes);
    }

    /// <summary>A paperdoll closing: its typing, its open list, its block over the game end; an unanswered question is a no.</summary>
    private void DollControlsClosed(OpenDollGump gump)
    {
        if (_typing is { } typing && typing.Gump == gump) _typing = null;
        if (_openDropDown is { } list && list.Gump == gump) _openDropDown = null;
        if (gump.Blocker is not null) PaperdollGumpsCanvas.Children.Remove(gump.Blocker);
        gump.Blocker = null;
        if (gump.Answered is { } answered)
        {
            gump.Answered = null;
            answered(false);
        }
    }

    /// <summary>The controls bound to variables show what the variables hold (a variable nothing has set yet takes the control's start).</summary>
    private void ReadControlVariables(OpenDollGump gump)
    {
        foreach (var part in gump.Design.Parts.Where(p => DollPart.HasVariable(p.Kind) && !string.IsNullOrWhiteSpace(p.VariableName)))
        {
            var runtime = gump.State.Of(part);
            if (!_execContext.HasVariable(part.VariableName)) { WriteControlVariable(part, runtime); continue; }
            double value = _execContext.GetVariable(part.VariableName);
            switch (part.Kind)
            {
                case DollPartKind.CheckBox or DollPartKind.RadioButton:
                    runtime.Value = runtime.Look = value != 0 ? 1 : 0;
                    break;
                case DollPartKind.TextBox:
                    if (part.NumbersOnly) runtime.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case DollPartKind.Slider or DollPartKind.Display or DollPartKind.ProgressBar:
                    runtime.Value = Math.Clamp((int)Math.Round(value), part.Min, Math.Max(part.Min, part.Max));
                    break;
                default:
                    runtime.Value = (int)Math.Round(value);
                    break;
            }
        }
    }

    private void WriteControlVariable(DollPart part, DollPartRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(part.VariableName) || !DollPart.HasVariable(part.Kind)) return;
        if (part.Kind == DollPartKind.TextBox)
        {
            // A text box keeps a number (typed with Numbers only, or words that happen to be one).
            if (double.TryParse(runtime.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number))
                _execContext.SetVariable(part.VariableName, number);
            else if (part.NumbersOnly) _execContext.SetVariable(part.VariableName, 0);
            return;
        }
        _execContext.SetVariable(part.VariableName, runtime.Value);
    }

    // ================= Drawing =================

    private FrameworkElement DollControlVisual(OpenDollGump gump, DollPart part, DollPartRuntime runtime)
    {
        bool typing = _typing is { } t && t.Gump == gump && t.Part.Id == part.Id;
        bool open = IsOpenDropDown(gump, part);
        int? bar = part.Kind == DollPartKind.ProgressBar && part.ShowsPartId >= 0 && gump.Design.Part(part.ShowsPartId) is { } source ? gump.State.Of(source).Value : null;
        return DollControls.Build(part, new DollControls.Shown(runtime.Look, runtime.Value, runtime.Text, typing, open), _sprites, sprite => AnimatedLook(gump, sprite), bar);
    }

    private bool IsOpenDropDown(OpenDollGump gump, DollPart part) => _openDropDown is { } list && list.Gump == gump && list.PartId == part.Id;

    // ================= Clicks =================

    /// <summary>The click sound (System Settings > Sounds > Drop in a window) - every button, box, slot and list of a PaperDoll Editor paperdoll.</summary>
    private void PlayControlClick(DollPart part)
    {
        if (part.Kind is DollPartKind.Slot or DollPartKind.ItemArea or DollPartKind.Lid or DollPartKind.Picture or DollPartKind.Light or DollPartKind.Display
            or DollPartKind.Text or DollPartKind.SaveScreenshot or DollPartKind.SaveDetails or DollPartKind.ProgressBar) return;
        PlaySound(_systemSettings.Sounds.NavigateSoundId);
    }

    /// <summary>A click on a form control. True = handled.</summary>
    private bool DollControlClicked(OpenDollGump gump, DollPart part, DollPartRuntime runtime)
    {
        if (runtime.Disabled) return true; // greyed out: nothing happens
        part = part.WithRuntime(runtime); // a list's choices as an event set them
        if (_typing is { } typing && (typing.Gump != gump || typing.Part.Id != part.Id)) StopTyping(); // clicking anything else ends the typing
        if (_openDropDown is { } list && (list.Gump != gump || list.PartId != part.Id))
        {
            _openDropDown = null;
            if (_openDolls.Contains(list.Gump)) RefreshDollGump(list.Gump);
        }
        switch (part.Kind)
        {
            case DollPartKind.NewGameButton or DollPartKind.ContinueButton or DollPartKind.LoadGameButton or DollPartKind.QuitButton:
            {
                var kind = part.Kind;
                PressDollButton(gump, runtime);
                AfterPress(() =>
                {
                    if (!_openDolls.Contains(gump)) return;
                    FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" pressed.");
                    if (_openDolls.Contains(gump) || kind == DollPartKind.QuitButton) TitleButton(gump, kind); // U8GameWindow.Title.cs
                });
                return true;
            }
            case DollPartKind.ConfirmButton or DollPartKind.CancelButton:
            {
                bool yes = part.Kind == DollPartKind.ConfirmButton;
                PressDollButton(gump, runtime);
                AfterPress(() =>
                {
                    if (!_openDolls.Contains(gump)) return;
                    FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" pressed.");
                    if (_openDolls.Contains(gump)) AnswerDoll(gump, yes);
                });
                return true;
            }
            case DollPartKind.TextBox:
                _typing = (gump, part);
                RefreshDollGump(gump);
                StatusText.Text = $"\"{part.Name}\": type, then Enter.";
                return true;
            case DollPartKind.CheckBox:
                runtime.Value = runtime.Look = runtime.Value != 0 ? 0 : 1;
                ControlChanged(gump, part, runtime, runtime.Value == 1 ? "ticked" : "unticked");
                return true;
            case DollPartKind.RadioButton:
            {
                if (runtime.Value == 1) return true;
                foreach (var other in gump.Design.Parts.Where(p => p.Kind == DollPartKind.RadioButton && p.Id != part.Id && p.RadioGroup == part.RadioGroup))
                {
                    var otherRuntime = gump.State.Of(other);
                    if (otherRuntime.Value == 0) continue;
                    otherRuntime.Value = otherRuntime.Look = 0;
                    WriteControlVariable(other, otherRuntime);
                }
                runtime.Value = runtime.Look = 1;
                ControlChanged(gump, part, runtime, "picked");
                return true;
            }
            case DollPartKind.DropDown:
            {
                if (!IsOpenDropDown(gump, part))
                {
                    _openDropDown = (gump, part.Id);
                    RefreshDollGump(gump);
                    return true;
                }
                _openDropDown = null;
                var box = DollLook.SizeOf(part, 0, _sprites);
                if (DollPartVisual(gump, part.Id) is { } visual && Mouse.GetPosition(visual).Y is var y && y >= box.Height)
                {
                    int row = (int)((y - box.Height) / DollControls.RowHeight(part, _sprites));
                    if (row >= 0 && row < part.Options.Count && row + 1 != runtime.Value)
                    {
                        runtime.Value = row + 1;
                        ControlChanged(gump, part, runtime, $"\"{part.Options[row]}\" picked");
                        return true;
                    }
                }
                RefreshDollGump(gump);
                return true;
            }
            case DollPartKind.ListBox:
            {
                if (DollPartVisual(gump, part.Id) is not { } visual) return true;
                var size = DollLook.SizeOf(part, 0, _sprites);
                double rowHeight = DollControls.RowHeight(part, _sprites);
                int first = DollControls.FirstRow(part, runtime.Value, Math.Max(1, (int)(size.Height / rowHeight)));
                int row = first + (int)(Mouse.GetPosition(visual).Y / rowHeight);
                if (row >= 0 && row < part.Options.Count && row + 1 != runtime.Value)
                {
                    runtime.Value = row + 1;
                    ControlChanged(gump, part, runtime, $"\"{part.Options[row]}\" picked");
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>A control's value changed: into its variable, drawn, and its By Use event runs (as a button's press does).</summary>
    private void ControlChanged(OpenDollGump gump, DollPart part, DollPartRuntime runtime, string what)
    {
        WriteControlVariable(part, runtime);
        RefreshDollGump(gump);
        if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" {what}.")) StatusText.Text = $"\"{part.Name}\" {what}.";
        if (_openDolls.Contains(gump)) RefreshDollGump(gump);
    }

    // ================= Typing =================

    /// <summary>A key while a text box is typed in: Backspace, Enter / Tab / Escape end it. True = handled.</summary>
    private bool DollTypingKey(Key key)
    {
        if (_typing is not { } typing) return false;
        switch (key)
        {
            case Key.Back:
            {
                var runtime = typing.Gump.State.Of(typing.Part);
                if (runtime.Text.Length > 0) runtime.Text = runtime.Text[..^1];
                RefreshDollGump(typing.Gump);
                break;
            }
            case Key.Enter or Key.Tab or Key.Escape:
                StopTyping();
                break;
        }
        return true; // letters arrive as text (DollTypingText); no other key does anything while typing
    }

    private void DollTypingText(object sender, TextCompositionEventArgs e)
    {
        if (_typing is not { } typing || string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        var runtime = typing.Gump.State.Of(typing.Part);
        foreach (char c in e.Text)
        {
            if (char.IsControl(c) || runtime.Text.Length >= Math.Max(1, typing.Part.MaxLength)) continue;
            if (typing.Part.NumbersOnly && !(char.IsDigit(c) || (c == '-' && runtime.Text.Length == 0))) continue;
            runtime.Text += c;
        }
        RefreshDollGump(typing.Gump);
    }

    /// <summary>The typing is over (Enter, a click elsewhere): into its variable, and its By Use event runs.</summary>
    private void StopTyping()
    {
        if (_typing is not { } typing) return;
        _typing = null;
        if (!_openDolls.Contains(typing.Gump)) return;
        ControlChanged(typing.Gump, typing.Part, typing.Gump.State.Of(typing.Part), "typed in");
    }

    /// <summary>A key while a modal paperdoll is open: Escape answers no, Enter presses its Yes / OK button. Nothing else reaches the game.</summary>
    private void ModalDollKey(OpenDollGump gump, Key key)
    {
        if (key == Key.Escape) { if (!ReferenceEquals(gump, _titleGump)) AnswerDoll(gump, false); return; } // Escape doesn't close the Title Screen
        if (key == Key.Enter && gump.Design.Parts.FirstOrDefault(p => p.Kind == DollPartKind.ConfirmButton && gump.State.Of(p).Visible) is { } confirm)
        {
            PlayControlClick(confirm);
            DollControlClicked(gump, confirm, gump.State.Of(confirm));
        }
    }

    // ================= Open Paperdoll (event command) =================

    /// <summary>Open Paperdoll: any PaperDoll Editor paperdoll, modal or not; waits for it to close if asked, its answer (Yes / OK = 1, anything else = 0) into a variable.</summary>
    private bool OpenPaperdollCommand(EventCommand command, Action<bool> done)
    {
        if (!int.TryParse(command.Get("DesignId"), out int id) || _objects.DollDesigns.FirstOrDefault(d => d.Id == id) is not { } design)
            return Cancel($"Open Paperdoll: \"{command.Get("DesignName")}\" isn't in the PaperDoll Editor any more.");
        bool wait = command.Get("Wait") == "True";
        string variable = command.Get("AnswerVariable");
        void Answered(bool yes)
        {
            if (!string.IsNullOrWhiteSpace(variable)) _execContext.SetVariable(variable, yes ? 1 : 0);
            if (wait) done(true);
        }
        bool asks = wait || !string.IsNullOrWhiteSpace(variable);
        if (OpenForm(design, command.Get("Modal") == "True", asks ? Answered : null, command.Get("Fresh") == "True") is null)
            return Cancel($"Open Paperdoll: \"{design.Name}\" couldn't be opened.");
        if (!wait) done(true);
        return true;
    }
}
