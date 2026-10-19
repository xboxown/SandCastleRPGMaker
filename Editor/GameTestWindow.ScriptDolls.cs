using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The paperdoll script commands (CommandList group 6): close paperdolls,
/// show / hide, look, words, value, enabled, position of their parts, lists
/// filled from arrays, items into and out of slots, where a paperdoll sits.
/// "This paperdoll" is the one running the event; another is picked by name
/// among the paperdolls open now. Parts are found by their PaperDoll Editor
/// name. Formulas give the values.
/// </summary>
public partial class GameTestWindow
{
    private sealed class PartSlide
    {
        public required OpenDollGump Gump;
        public required DollPartRuntime Runtime;
        public double FromX, FromY, ToX, ToY, Ms, TotalMs;
    }

    private readonly List<PartSlide> _partSlides = new();

    private void InitializeScriptDolls()
    {
        var h = _execContext.GameHandlers;
        h[EventCommandType.ClosePaperdoll] = ClosePaperdollCommand;
        h[EventCommandType.ShowHidePart] = c => WithPart(c, (gump, part, runtime) =>
        {
            runtime.Visible = c.Get("Value") switch { "Hidden" => false, "Toggled" => !runtime.Visible, _ => true };
            return true;
        });
        h[EventCommandType.SetPartLook] = c => WithPart(c, (gump, part, runtime) =>
        {
            object look = Formula(c, "Look");
            int index = look is string name ? part.Looks.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : (int)ScriptValues.Number(look) - 1;
            if (index < 0 || index >= Math.Max(1, part.Looks.Count)) return false;
            runtime.Look = index;
            return true;
        });
        h[EventCommandType.SetPartText] = c => WithPart(c, (gump, part, runtime) =>
        {
            string text = ScriptValues.Text(Formula(c, "Value"));
            if (part.Kind == DollPartKind.TextBox) runtime.Text = text;
            else runtime.Caption = text;
            return true;
        });
        h[EventCommandType.SetPartValue] = c => WithPart(c, (gump, part, runtime) => SetPartValue(gump, part, runtime, Formula(c, "Value")));
        h[EventCommandType.GetPartValue] = c => WithPart(c, (gump, part, runtime) =>
        {
            object value = part.Kind == DollPartKind.TextBox ? runtime.Text : (double)runtime.Value;
            SetScriptVariable(c.Get("Variable"), value);
            return true;
        }, refresh: false);
        h[EventCommandType.FillListFromArray] = c => WithPart(c, (gump, part, runtime) =>
        {
            if (!_execContext.ScriptData.Arrays.TryGetValue(c.Get("Array"), out var list)) return false;
            runtime.Options = list.Select(ScriptValues.Text).ToList();
            runtime.Value = Math.Clamp(runtime.Value, 0, runtime.Options.Count);
            return true;
        });
        h[EventCommandType.GetPickedChoice] = c => WithPart(c, (gump, part, runtime) =>
        {
            var options = runtime.Options ?? part.Options;
            int picked = runtime.Value;
            SetScriptVariable(c.Get("Variable"), (double)picked);
            if (c.Get("Variable2").Length > 0) SetScriptVariable(c.Get("Variable2"), picked >= 1 && picked <= options.Count ? options[picked - 1] : "");
            return true;
        }, refresh: false);
        h[EventCommandType.EnablePart] = c => WithPart(c, (gump, part, runtime) => { runtime.Disabled = c.Get("Value") == "Disabled"; return true; });
        h[EventCommandType.MovePart] = c => WithPart(c, (gump, part, runtime) =>
        {
            double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y"), seconds = Math.Max(0, FormulaNumber(c, "Seconds"));
            _partSlides.RemoveAll(s => ReferenceEquals(s.Runtime, runtime));
            if (seconds <= 0) { runtime.X = x; runtime.Y = y; return true; }
            _partSlides.Add(new PartSlide { Gump = gump, Runtime = runtime, FromX = runtime.X ?? part.X, FromY = runtime.Y ?? part.Y, ToX = x, ToY = y, TotalMs = seconds * 1000 });
            return true;
        });
        h[EventCommandType.PutItemInSlot] = c => WithPart(c, (gump, part, runtime) =>
        {
            if (part.Kind is not (DollPartKind.Slot or DollPartKind.ItemArea)) return false;
            if (!Enum.TryParse<ReferencedItemCategory>(c.Get("Category"), out var category) || !int.TryParse(c.Get("ItemId"), out int itemId) || itemId < 0) return false;
            int count = Math.Max(1, (int)FormulaNumber(c, "Quantity"));
            bool stacks = category == ReferencedItemCategory.Object && CanCombine(_objects.Objects.FirstOrDefault(o => o.Id == itemId));
            int room = Math.Max(1, part.Capacity) - runtime.Items.Count;
            if (room <= 0) return false;
            if (stacks) runtime.Items.Add(new InventoryItemInstance { InstanceId = gump.State.NextItemInstanceId++, Category = category, ItemId = itemId, Quantity = count, ContainerX = UnplacedSpot, ContainerY = UnplacedSpot });
            else
                for (int n = 0; n < Math.Min(count, room); n++)
                    runtime.Items.Add(new InventoryItemInstance { InstanceId = gump.State.NextItemInstanceId++, Category = category, ItemId = itemId, Quantity = 1, ContainerX = UnplacedSpot, ContainerY = UnplacedSpot });
            return true;
        });
        h[EventCommandType.TakeItemFromSlot] = c => WithPart(c, (gump, part, runtime) =>
        {
            var items = runtime.Items.ToList();
            runtime.Items.Clear();
            if (c.Get("To") == "Go to the main player" && _player is not null)
                foreach (var item in items)
                    if (!HandOver(item, _player)) PutDownAt(item, _player);
            RefreshAfterMove();
            return true;
        });
        h[EventCommandType.MovePaperdoll] = MovePaperdollCommand;
        h[EventCommandType.FocusTextBox] = c => WithPart(c, (gump, part, runtime) =>
        {
            if (part.Kind != DollPartKind.TextBox) return false;
            _typing = (gump, part);
            return true;
        });
    }

    /// <summary>The paperdoll a command means: this one (the event's), or the first open one of that design.</summary>
    private List<OpenDollGump> DollGumpsFor(EventCommand command)
    {
        if (int.TryParse(command.Get("DesignId"), out int id) && id >= 0) return _openDolls.Where(g => g.Design.Id == id).Take(1).ToList();
        var context = _dollContext ?? _lastDollContext;
        return context is null ? new List<OpenDollGump>() : _openDolls.Where(g => ReferenceEquals(g.State, context.State)).ToList();
    }

    /// <summary>Finds the paperdoll and its part (by name), does it, writes a control's variable and redraws.</summary>
    private bool WithPart(EventCommand command, Func<OpenDollGump, DollPart, DollPartRuntime, bool> act, bool refresh = true)
    {
        var gumps = DollGumpsFor(command);
        if (gumps.Count == 0) { StatusText.Text = $"{ScriptCommands.Of(command.Type)?.Name}: that paperdoll isn't open."; return false; }
        var gump = gumps[0];
        string name = command.Get("PartName").Trim();
        var part = gump.Design.Parts.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (part is null) { StatusText.Text = $"{ScriptCommands.Of(command.Type)?.Name}: \"{gump.Design.Name}\" has no part \"{name}\"."; return false; }
        var runtime = gump.State.Of(part);
        if (!act(gump, part, runtime)) return false;
        if (!refresh) return true;
        WriteControlVariable(part, runtime);
        foreach (var open in _openDolls.Where(g => ReferenceEquals(g.State, gump.State)).ToList()) RefreshDollGump(open);
        return true;
    }

    /// <summary>Set Part Value: a slider / bar within its range, a check box, a radio button (picking it unpicks its group), a text box's words, a list's picked row.</summary>
    private bool SetPartValue(OpenDollGump gump, DollPart part, DollPartRuntime runtime, object value)
    {
        int number = (int)Math.Round(ScriptValues.Number(value));
        switch (part.Kind)
        {
            case DollPartKind.TextBox:
                runtime.Text = ScriptValues.Text(value);
                return true;
            case DollPartKind.CheckBox:
                runtime.Value = runtime.Look = number != 0 ? 1 : 0;
                return true;
            case DollPartKind.RadioButton:
                runtime.Value = runtime.Look = number != 0 ? 1 : 0;
                if (runtime.Value == 1)
                    foreach (var other in gump.Design.Parts.Where(p => p.Kind == DollPartKind.RadioButton && p.Id != part.Id && p.RadioGroup == part.RadioGroup))
                    {
                        var o = gump.State.Of(other);
                        o.Value = o.Look = 0;
                        WriteControlVariable(other, o);
                    }
                return true;
            case DollPartKind.DropDown or DollPartKind.ListBox:
                runtime.Value = Math.Clamp(number, 0, (runtime.Options ?? part.Options).Count);
                return true;
            default:
                runtime.Value = part.Max > part.Min ? Math.Clamp(number, part.Min, part.Max) : number;
                return true;
        }
    }

    private bool ClosePaperdollCommand(EventCommand command)
    {
        var gumps = command.Get("Which") switch
        {
            "Every paperdoll" => _openDolls.ToList(),
            "One paperdoll" => int.TryParse(command.Get("DesignId"), out int id) ? _openDolls.Where(g => g.Design.Id == id).ToList() : new List<OpenDollGump>(),
            _ => DollGumpsFor(new EventCommand { Parameters = { ["DesignId"] = "-1" } })
        };
        foreach (var gump in gumps) CloseDollGump(gump);
        return gumps.Count > 0;
    }

    private bool MovePaperdollCommand(EventCommand command)
    {
        var gumps = DollGumpsFor(command);
        if (gumps.Count == 0) return false;
        var gump = gumps[0];
        if (command.Get("Move") == "True")
        {
            Canvas.SetLeft(gump.GumpBorder, FormulaNumber(command, "X") * ViewportScale.ScaleX + ViewportOffset().X);
            Canvas.SetTop(gump.GumpBorder, FormulaNumber(command, "Y") * ViewportScale.ScaleY + ViewportOffset().Y);
        }
        if (command.Get("Front") == "True") BringDollToFront(gump);
        return true;
    }

    /// <summary>Where the game's picture starts inside the window (it's centred, letterboxed).</summary>
    private System.Windows.Point ViewportOffset() => ViewportBorder.TranslatePoint(new System.Windows.Point(0, 0), PaperdollGumpsCanvas);

    /// <summary>Move Part over a time: each frame a step nearer, redrawn.</summary>
    private void UpdatePartSlides(double elapsedMs)
    {
        if (_partSlides.Count == 0) return;
        var redraw = new HashSet<OpenDollGump>();
        foreach (var slide in _partSlides.ToList())
        {
            slide.Ms = Math.Min(slide.TotalMs, slide.Ms + elapsedMs);
            double t = slide.TotalMs <= 0 ? 1 : slide.Ms / slide.TotalMs;
            slide.Runtime.X = slide.FromX + (slide.ToX - slide.FromX) * t;
            slide.Runtime.Y = slide.FromY + (slide.ToY - slide.FromY) * t;
            if (_openDolls.Contains(slide.Gump)) redraw.Add(slide.Gump);
            if (t >= 1 || !_openDolls.Contains(slide.Gump)) _partSlides.Remove(slide);
        }
        foreach (var gump in redraw) RefreshDollGump(gump);
    }
}
