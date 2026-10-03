using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Body Container: the one container every dead body
/// opens as. No world sprite (a body looks like the dead character's Body
/// pose); just its paperdoll picture from the Sprite Editor, the frame its
/// belongings lie within (the Container Editor's own Rectangle / Ellipse /
/// Pencil frame tool), and its sounds. Exult's rule: take out, never put in.
/// </summary>
public partial class SystemSettingsWindow
{
    private sealed record SpriteOption(string Group, SpriteEntry Sprite)
    {
        public override string ToString() => $"{Group} / {Sprite.Name}";
    }

    private UIElement BuildBodyContainerTab()
    {
        var body = _settings.BodyContainer;
        body.IsBodyContainer = true;

        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "Every dead character's body opens as this container. It has no world sprite - in the world a body is the character's Body pose " +
                   "(its weight, volume and Too Heavy are set per character in Character Editor > Health). Choose the paperdoll shown when it's opened " +
                   "and draw the frame its belongings lie within. As in Exult, things can be taken out of a body but never put back in.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var spriteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        spriteRow.Children.Add(new TextBlock { Text = "Paperdoll:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        var options = _sprites.Groups.SelectMany(g => g.Sprites.Select(s => new SpriteOption(g.Name, s))).ToList();
        var spriteCombo = new ComboBox { Width = 300 };
        spriteCombo.Items.Add("(None)");
        foreach (var option in options) spriteCombo.Items.Add(option);
        spriteCombo.SelectedItem = options.FirstOrDefault(o => o.Group == body.PaperDollSpriteGroup && o.Sprite.Name == body.PaperDollSpriteName)
            ?? options.FirstOrDefault(o => o.Sprite.Name == body.PaperDollSpriteName) as object ?? "(None)";
        spriteRow.Children.Add(spriteCombo);
        panel.Children.Add(spriteRow);

        panel.Children.Add(SoundRow("Open sound:", () => body.OpenSoundResourceId, v => body.OpenSoundResourceId = v));
        panel.Children.Add(SoundRow("Close sound:", () => body.CloseSoundResourceId, v => body.CloseSoundResourceId = v));

        var frameEditor = new ContainerFrameEditor(body, _sprites) { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(frameEditor);

        spriteCombo.SelectionChanged += (_, _) =>
        {
            if (spriteCombo.SelectedItem is SpriteOption o) { body.PaperDollSpriteGroup = o.Group; body.PaperDollSpriteName = o.Sprite.Name; }
            else { body.PaperDollSpriteGroup = string.Empty; body.PaperDollSpriteName = string.Empty; }
            frameEditor.Refresh();
        };

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement SoundRow(string label, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { Width = 300, DisplayMemberPath = nameof(ResourceEntry.Name) };
        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        combo.ItemsSource = sounds; // its own list (shared lists make WPF combos change each other)
        combo.SelectedItem = sounds.FirstOrDefault(s => s.Id == get()) ?? sounds[0];
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
        row.Children.Add(combo);
        return row;
    }
}
