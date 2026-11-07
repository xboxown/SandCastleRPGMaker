// Ultima 6 engine (U6GameWindow): making a character on the title's Create New Character scene (or any scene) - Ultima 6 projects only.
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Ultima 6 tab's commands for making a character - all usable in any event, on a scene or in the text area:
///  - Scene Type Text: the player types words (a name) on the scene or in the text area; Enter keeps them in a variable.
///  - Scene Pick Picture: the pictures of a sprite group, one at a time - Left / Right to look, Enter or a click to pick; its
///    number goes into a variable, and it can be someone's portrait at once.
///  - Set Gender: male or female (or by a variable).
///  - Scene Distribute Points: points (a formula - a global variable) spread over the stats listed, each between its least and
///    most: Up / Down a stat, Left / Right take or give a point, Enter done (all spent, if so asked); what's left into a variable.
/// With Change Name, Change Portrait, Change Stat and Scene Choice this is Ultima 6's character creation, any way the maker likes.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>A scene command waiting for keys of its own (typing, a picture, the points).</summary>
    private Func<Key, bool>? _sceneInput;

    private void InitU6Create()
    {
        var a = _execContext.GameActions;
        a[EventCommandType.U6SceneTypeText] = SceneTypeText;
        a[EventCommandType.U6ScenePickPicture] = ScenePickPicture;
        a[EventCommandType.U6SceneDistributePoints] = SceneDistributePoints;
        _execContext.GameHandlers[EventCommandType.U6SetGender] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
            string how = c.Get("Gender");
            bool female = how == "Female" || (how.StartsWith("By") && (int)ScriptValues.Number(ScriptExpression.GetVariable(_execContext, c.Get("Variable"))) == 2);
            who.Gender = female ? CharacterGender.Female : CharacterGender.Male;
            return true;
        };
    }

    /// <summary>Words shown on the scene under a name (replaced each time), or in the text area's prompt with no scene.</summary>
    private void SceneShow(string name, double x, double y, string text)
    {
        if (_scene is null) { _hudPrompt = text; RefreshHudText(); return; }
        SceneRemove(name);
        var piece = new HudPiece { X = (int)x, Y = (int)y, Width = (int)Math.Max(8, _scene.Width - x), Height = (int)_scene.Height };
        SceneAdd(name, HudWordsAt(piece, text));
    }

    private void SceneInputDone(string name)
    {
        _sceneInput = null;
        _sceneClick = null;
        if (_scene is null) { _hudPrompt = string.Empty; RefreshHudText(); }
        else SceneRemove(name);
    }

    /// <summary>A line before an asking command (typing, a choice) counts as read: its words are already in the text area - the answer's line comes straight after, no extra Enter.</summary>
    private void LineBeforeAskRead()
    {
        if (U6Talking && _talkMore.Count == 0 && _currentMessage is { HasReplies: false }) AdvanceMessage();
    }

    private bool SceneTypeText(EventCommand c, Action<bool> done)
    {
        LineBeforeAskRead();
        string prompt = SceneWords(c.Get("Prompt"));
        int longest = Math.Clamp((int)FormulaNumber(c, "Letters"), 1, 60);
        string typed = ScriptValues.Text(Formula(c, "Start"));
        if (typed.Length > longest) typed = typed[..longest];
        double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y");
        void Show() => SceneShow("typing", x, y, prompt + typed + "_");
        Show();
        _sceneInput = key =>
        {
            if (key is Key.Enter or Key.Return)
            {
                if (typed.Trim().Length == 0) return true;
                SceneInputDone("typing");
                if (_scene is null) U6Say(prompt + typed);
                SetScriptVariable(c.Get("Variable"), typed.Trim());
                done(true);
                return true;
            }
            if (key == Key.Back) { if (typed.Length > 0) typed = typed[..^1]; }
            else if (LetterOf(key) is char letter && typed.Length < longest) typed += letter;
            Show();
            return true;
        };
        return true;
    }

    private bool ScenePickPicture(EventCommand c, Action<bool> done)
    {
        var group = _sprites.Groups.FirstOrDefault(g => string.Equals(g.Name, c.Get("Group").Trim(), StringComparison.OrdinalIgnoreCase));
        var pictures = group?.Sprites.Where(s => s.Frames.Count > 0).ToList() ?? new List<SpriteEntry>();
        if (pictures.Count == 0 || group is null) return false;
        double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y"), w = FormulaNumber(c, "Width"), h = FormulaNumber(c, "Height");
        int at = Math.Clamp((int)FormulaNumber(c, "Start") - 1, 0, pictures.Count - 1);
        string prompt = SceneWords(c.Get("Prompt"));
        void Show()
        {
            var sprite = pictures[at];
            var first = DollLook.Frame(sprite);
            double pw = w > 0 ? w : first?.PixelWidth ?? 16, ph = h > 0 ? h : first?.PixelHeight ?? 16;
            if (_scene is not null)
            {
                SceneRemove("picking");
                SceneAdd("picking", HudImage(sprite, x, y, pw, ph, _sceneAnimators, fit: true));
                SceneShow("picking words", x, y + ph + 2, $"{prompt}< {at + 1} / {pictures.Count} >");
            }
            else SceneShow("picking words", x, y, $"{prompt}< {sprite.Name} {at + 1} / {pictures.Count} >");
        }
        void Pick()
        {
            SceneInputDone("picking words");
            SceneRemove("picking");
            SetScriptVariable(c.Get("Variable"), (double)(at + 1));
            if (c.Get("Portrait") == "True" && CharacterDefinitionFor(c.Get("Who")) is { } who)
            {
                if (who.U6OwnFace.Length == 0) who.U6OwnFace = who.FaceSpriteGroup + "/" + who.FaceSpriteName;
                who.FaceSpriteGroup = group.Name;
                who.FaceSpriteName = pictures[at].Name;
                RefreshHudPanel();
            }
            done(true);
        }
        Show();
        _sceneInput = key =>
        {
            if (key is Key.Left) { at = (at + pictures.Count - 1) % pictures.Count; Show(); }
            else if (key is Key.Right) { at = (at + 1) % pictures.Count; Show(); }
            else if (key is Key.Enter or Key.Return or Key.Space) Pick();
            return true;
        };
        _sceneClick = Pick;
        return true;
    }

    /// <summary>A click while a picture is being picked: that one.</summary>
    private Action? _sceneClick;

    private bool SceneDistributePoints(EventCommand c, Action<bool> done)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        var stats = c.Get("Stats").Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0)
            .Select(n => _stats.Stats.FirstOrDefault(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase))
                ?? _stats.Stats.FirstOrDefault(s => s.Kind != BuiltInStatKind.None && string.Equals(s.Kind.ToString(), n, StringComparison.OrdinalIgnoreCase))).OfType<StatDefinition>().ToList(); // STR or Strength
        if (stats.Count == 0) return false;
        int points = Math.Max(0, (int)FormulaNumber(c, "Points"));
        int least = (int)FormulaNumber(c, "Least"), most = (int)FormulaNumber(c, "Most");
        if (most <= 0) most = 99;
        bool spendAll = c.Get("SpendAll") != "False";
        var values = stats.Select(s => who.StatValues.TryGetValue(s.Id, out var t) && int.TryParse(t, out int v) ? Math.Max(v, least) : least).ToList();
        int row = 0;
        double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y");
        string title = SceneWords(c.Get("Prompt"));
        void Show()
        {
            var lines = new List<string>();
            if (title.Length > 0) lines.Add(title);
            for (int i = 0; i < stats.Count; i++) lines.Add($"{(i == row ? ">" : " ")} {stats[i].Name}: {values[i]}");
            lines.Add($"{W("PointsLeft")} {points}");
            SceneShow("points", x, y, string.Join(_scene is null ? "  " : "\n", lines));
        }
        Show();
        _sceneInput = key =>
        {
            switch (key)
            {
                case Key.Up: row = (row + stats.Count - 1) % stats.Count; break;
                case Key.Down: row = (row + 1) % stats.Count; break;
                case Key.Right or Key.OemPlus or Key.Add when points > 0 && values[row] < most: values[row]++; points--; break;
                case Key.Left or Key.OemMinus or Key.Subtract when values[row] > least: values[row]--; points++; break;
                case Key.Enter or Key.Return when !spendAll || points == 0:
                    SceneInputDone("points");
                    for (int i = 0; i < stats.Count; i++) who.StatValues[stats[i].Id] = values[i].ToString();
                    if (c.Get("LeftVariable").Length > 0) SetScriptVariable(c.Get("LeftVariable"), (double)points);
                    RefreshHudPanel();
                    done(true);
                    return true;
            }
            Show();
            return true;
        };
        return true;
    }
}
