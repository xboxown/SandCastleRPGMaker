using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>The toolbar's editor buttons (after Zoom): one icon per editor of the Tools menu, its name and what it's for on hover.</summary>
public partial class MainWindow
{
    private void BuildToolIcons()
    {
        var tools = new (string Name, string Purpose, RoutedEventHandler Open)[]
        {
            ("Object Editor", "Every object of the world: furniture, doors, lights, food, weapons, stations - their looks, sizes and events.", ObjectDatabaseButton_Click),
            ("Staircase Designer", "Stairs that climb from one floor to the next, step by step.", StaircaseDesignerMenuItem_Click),
            ("Wall Designer", "Walls with windows and doorways, glued once and placed as one.", WallDesignerMenuItem_Click),
            ("PaperDoll Editor", "Paperdolls and every window of the game: inventories, the save screen, the title, prompts.", PaperDollEditorMenuItem_Click),
            ("Common Events", "Command lists made once and run from anywhere with Call Common Event.", CommonEventsMenuItem_Click),
            ("Title Screen", "The Title Screen: its paperdoll, music and intro video.", TitleScreenMenuItem_Click),
            ("Currency Editor", "The money of your worlds and what each is worth.", CurrencyEditorMenuItem_Click),
            ("Resources", "The project's pictures, sounds and music.", ResourcesMenuItem_Click),
            ("Faction Editor", "Who is friend and who is foe: factions and how they get along.", FactionEditorMenuItem_Click),
            ("Sprite Editor", "Sprites, animations and tilesets.", SpriteEditorMenuItem_Click),
            ("Book Editor", "Books, scrolls and signs: their pages and events.", BookEditorMenuItem_Click),
            ("Container Editor", "Chests, bags, barrels and bodies: what they hold and how they open.", ContainerEditorMenuItem_Click),
            ("Sound Editor", "The game's sounds and music.", SoundEditorMenuItem_Click),
            ("Sound Mixer", "Layer sounds together and compose them into a new sound.", SoundMixerMenuItem_Click),
            ("Video Library", "Videos for intros and cutscenes.", VideoLibraryMenuItem_Click),
            ("Variable Editor", "Variables and switches your events count and check.", VariableEditorMenuItem_Click),
            ("Stat Editor", "Character stats: Strength, Dexterity, Magic, your own...", StatEditorMenuItem_Click),
            ("Character Editor", "Every character: looks, stats, schedules, talk, events.", CharacterEditorMenuItem_Click),
        };
        MainToolBar.Items.Add(new Separator());
        foreach (var (name, purpose, open) in tools)
        {
            var tip = new StackPanel { MaxWidth = 280 };
            tip.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold });
            tip.Children.Add(new TextBlock { Text = purpose, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var icon = new Image { Source = ToolIcons.For(name), Width = 28, Height = 28 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var button = new Button { Content = icon, ToolTip = tip, Padding = new Thickness(2), Margin = new Thickness(1, 0, 1, 0) };
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += open;
            MainToolBar.Items.Add(button);
        }
    }
}
