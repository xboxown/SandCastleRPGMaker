// Ultima 6 engine (U6GameWindow): cutscenes - a black screen, pictures, words and choices - Ultima 6 projects only.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's introduction and the cutscenes of Savage Empire and Martian Dreams, made with the Ultima 6 tab's Scene commands:
/// Begin Scene (the whole screen black, the HUD gone - or only the Game View, the HUD and its text area kept), Scene Picture
/// (a sprite anywhere, animated if it has frames), Scene Text (words anywhere on it, or in the text area; {Variable} puts a
/// variable's value in), Scene Wait For Key, Scene Choice (lines the player picks by their first letter, by number, or with a
/// click - which one goes into a variable), Scene Remove (one by its name, or everything) and End Scene. The world waits while a
/// scene is up; Wait still counts in it. The title menu's Introduction and Create New Character run the events given them in
/// System Settings > Ultima 6 > Title menu.
/// </summary>
public partial class U6GameWindow
{
    private Canvas? _scene;
    private readonly List<SpriteAnimator> _sceneAnimators = new();
    private readonly Dictionary<string, List<UIElement>> _sceneNamed = new();
    /// <summary>Waits that count while a scene is up (the world is paused then).</summary>
    private readonly List<(Func<double, bool?> Tick, Action<bool> Done)> _sceneWaits = new();
    private (Action Done, string? Prompt)? _sceneKeyWait;
    private (List<string> Choices, string Keys, string Variable, Action<bool> Done, double X, double Y, double LineH)? _sceneChoice;

    private void InitU6Scenes()
    {
        var h = _execContext.GameHandlers;
        var a = _execContext.GameActions;
        h[EventCommandType.U6SceneBegin] = c => { BeginScene(c.Get("Area") == "The Game View", c.Get("Colour")); return true; };
        h[EventCommandType.U6ScenePicture] = ScenePicture;
        h[EventCommandType.U6SceneText] = SceneText;
        h[EventCommandType.U6SceneRemove] = c => { SceneRemove(c.Get("Name")); return true; };
        h[EventCommandType.U6SceneEnd] = _ => { EndScene(); return true; };
        a[EventCommandType.U6SceneWaitKey] = (c, done) =>
        {
            if (_scene is null) return false;
            string prompt = c.Get("Prompt");
            if (prompt.Length > 0) U6Say(prompt);
            _sceneKeyWait = (() => done(true), prompt);
            return true;
        };
        a[EventCommandType.U6SceneChoice] = SceneChoice;
    }

    private void BeginScene(bool gameViewOnly, string colour)
    {
        if (_scene is null)
        {
            _scene = new Canvas { ClipToBounds = true };
            Panel.SetZIndex(_scene, 100500); // over the title menu too
            HudLayer.Children.Add(_scene);
        }
        _scene.Children.Clear();
        _sceneNamed.Clear();
        var view = Hud.First(HudPieceKind.GameView);
        var (x, y, w, h) = gameViewOnly && view is not null ? (view.X, view.Y, view.Width, view.Height) : (0, 0, (int)ScreenWidth, (int)ScreenHeight);
        Canvas.SetLeft(_scene, x);
        Canvas.SetTop(_scene, y);
        _scene.Width = w;
        _scene.Height = h;
        _scene.Background = ColorConverter.ConvertFromString(colour.Length == 0 ? "#000000" : colour.StartsWith('#') ? colour : "#" + colour) is Color c ? new SolidColorBrush(c) : Brushes.Black;
        _heldDirectionKeys.Clear();
    }

    private void EndScene()
    {
        if (_scene is null) return;
        foreach (var animator in _sceneAnimators) animator.Stop();
        _sceneAnimators.Clear();
        HudLayer.Children.Remove(_scene);
        _scene = null;
        _sceneNamed.Clear();
        _sceneKeyWait = null;
        _sceneChoice = null;
        GameCanvas.Focus();
    }

    private void SceneAdd(string name, UIElement element)
    {
        _scene!.Children.Add(element);
        if (name.Length == 0) return;
        if (!_sceneNamed.TryGetValue(name, out var list)) _sceneNamed[name] = list = new List<UIElement>();
        list.Add(element);
    }

    private void SceneRemove(string name)
    {
        if (_scene is null) return;
        if (name.Length == 0) { _scene.Children.Clear(); _sceneNamed.Clear(); return; }
        if (!_sceneNamed.Remove(name, out var list)) return;
        foreach (var element in list) _scene.Children.Remove(element);
    }

    private bool ScenePicture(EventCommand c)
    {
        if (_scene is null || SpriteOfLink(new SpriteLink { SpriteGroup = c.Get("SpriteGroup"), SpriteName = c.Get("SpriteName") }) is not { Frames.Count: > 0 } sprite) return false;
        var first = DollLook.Frame(sprite);
        double w = FormulaNumber(c, "Width"), h = FormulaNumber(c, "Height");
        double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y");
        if (c.Get("Fill") == "True") { x = 0; y = 0; w = _scene.Width; h = _scene.Height; }
        if (w <= 0) w = first?.PixelWidth ?? 16;
        if (h <= 0) h = first?.PixelHeight ?? 16;
        if (c.Get("Name").Length > 0) SceneRemove(c.Get("Name")); // the same name again: the picture changes
        SceneAdd(c.Get("Name"), HudImage(sprite, x, y, w, h, _sceneAnimators, fit: c.Get("Fill") == "True"));
        return true;
    }

    /// <summary>Words with {Variable} (any formula in braces) filled in.</summary>
    private string SceneWords(string text) => Regex.Replace(text, @"\{([^{}]+)\}", m =>
    {
        try { return ScriptValues.Text(ScriptExpression.Evaluate(m.Groups[1].Value, _execContext)); }
        catch (ScriptException) { return m.Value; }
    });

    private bool SceneText(EventCommand c)
    {
        string text = SceneWords(c.Get("Text"));
        if (c.Get("Where") == "In the text area" || _scene is null) { U6Say(text); return true; }
        double width = FormulaNumber(c, "Width");
        if (width <= 0) width = _scene.Width - FormulaNumber(c, "X");
        var piece = new HudPiece { X = (int)FormulaNumber(c, "X"), Y = (int)FormulaNumber(c, "Y"), Width = (int)width, Height = (int)_scene.Height, FontName = c.Get("Font"), TextColor = c.Get("Colour") is { Length: > 0 } col ? (col.StartsWith('#') ? col : "#" + col) : "" };
        var lines = text.Replace("\r", "").Split('\n').SelectMany(l => HudWrap(l, piece.Width, piece.FontName));
        if (c.Get("Name").Length > 0) SceneRemove(c.Get("Name")); // the same name again: the words change
        SceneAdd(c.Get("Name"), HudWordsAt(piece, string.Join("\n", lines)));
        return true;
    }

    /// <summary>Scene Choice: its lines drawn, the player picks one (its first letter, its number, or a click); its number goes into the variable.</summary>
    private bool SceneChoice(EventCommand c, Action<bool> done)
    {
        var choices = SceneWords(c.Get("Choices")).Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (choices.Count == 0) return false;
        if (_scene is null)
        {
            // No scene up: asked in the text area - the answers numbered; their number or first letter answers.
            if (SceneWords(c.Get("Question")) is { Length: > 0 } asked) U6Say(asked);
            for (int i = 0; i < choices.Count; i++) U6Say($"{i + 1}. {choices[i]}");
            string letters = string.Concat(choices.Select(s => char.ToUpperInvariant(s[0])));
            string allowed = string.Concat(Enumerable.Range(1, Math.Min(9, choices.Count)).Select(i => (char)('0' + i))) + letters;
            AskLetter("> ", allowed, key =>
            {
                int pick = char.IsDigit(key) ? key - '1' : letters.IndexOf(key);
                SetScriptVariable(c.Get("Variable"), (double)(pick + 1));
                done(true);
            });
            return true;
        }
        double x = FormulaNumber(c, "X"), y = FormulaNumber(c, "Y");
        string question = SceneWords(c.Get("Question"));
        int lineH = HudLineHeight("") + 2;
        var piece = new HudPiece { X = (int)x, Y = (int)y, Width = (int)(_scene.Width - x), Height = (int)_scene.Height };
        var lines = new List<string>();
        if (question.Length > 0) lines.AddRange(HudWrap(question, piece.Width, ""));
        lines.AddRange(choices);
        SceneRemove("choice");
        SceneAdd("choice", HudWordsAt(piece, string.Join("\n", lines)));
        string keys = string.Concat(choices.Select(s => char.ToUpperInvariant(s[0])));
        _sceneChoice = (choices, keys, c.Get("Variable"), done, x, y + (lines.Count - choices.Count) * (lineH - 2), lineH - 2);
        return true;
    }

    private void AnswerSceneChoice(int index)
    {
        if (_sceneChoice is not { } ask || index < 0 || index >= ask.Choices.Count) return;
        _sceneChoice = null;
        SceneRemove("choice");
        SetScriptVariable(ask.Variable, (double)(index + 1));
        ask.Done(true);
    }

    /// <summary>Keys while a scene waits: any key goes on; a choice takes its letter or number.</summary>
    private bool SceneKey(KeyEventArgs e)
    {
        if (_scene is null) return false;
        e.Handled = true;
        if (e.IsRepeat) return true;
        var key = U8KeyCapture.RealKey(e);
        if (_sceneChoice is { } ask)
        {
            if (key is >= Key.D1 and <= Key.D9) AnswerSceneChoice(key - Key.D1);
            else if (key is >= Key.NumPad1 and <= Key.NumPad9) AnswerSceneChoice(key - Key.NumPad1);
            else if (key is >= Key.A and <= Key.Z) { int i = ask.Keys.IndexOf((char)('A' + (key - Key.A))); if (i >= 0) AnswerSceneChoice(i); }
            return true;
        }
        if (_sceneKeyWait is { } wait) { _sceneKeyWait = null; wait.Done(); }
        return true;
    }

    private void SceneClick(Point onScreen)
    {
        if (_scene is null) return;
        if (_sceneChoice is { } ask)
        {
            var inScene = new Point(onScreen.X - Canvas.GetLeft(_scene), onScreen.Y - Canvas.GetTop(_scene));
            int i = (int)Math.Floor((inScene.Y - ask.Y) / Math.Max(1, ask.LineH));
            if (inScene.X >= ask.X && i >= 0 && i < ask.Choices.Count) AnswerSceneChoice(i);
            return;
        }
        if (_sceneKeyWait is { } wait) { _sceneKeyWait = null; wait.Done(); }
    }

    /// <summary>Each frame, paused or not: Wait counts while a scene is up.</summary>
    private void TickSceneWaits(double ms)
    {
        foreach (var wait in _sceneWaits.ToList())
        {
            if (wait.Tick(ms) is not true) continue;
            _sceneWaits.Remove(wait);
            wait.Done(true);
        }
    }

    /// <summary>The title menu's Introduction / Create New Character: their events (System Settings > Ultima 6), true if there were any.</summary>
    private bool RunTitleEvent(List<EventCommand> commands, Action after)
    {
        if (commands.Count == 0) return false;
        var run = commands.ToList();
        run.Add(new EventCommand { Type = EventCommandType.U6SceneEnd });
        _execContext.SelfBookId = -1;
        EventCommandInterpreter.Run(run, _execContext, new List<string>(), finished: after);
        return true;
    }
}
