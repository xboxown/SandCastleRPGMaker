using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>The platforms a game is built for (Game > Build...).</summary>
public enum BuildPlatform { Windows, Linux, MacModern, MacPowerPC, MorphOS, AmigaOS4, Amiga68k, AmigaVampire }

/// <summary>
/// Game > Build...: which platform to build the game for. Windows is this
/// engine itself; every other platform runs the game on the Sandcastle
/// player (written in C: SDL on Linux, Mac, MorphOS and AmigaOS 4; RTG and
/// AHI or Paula on Amiga 68k; the Vampire build drives SAGA, AMMX and the
/// sound hardware directly). Platforms whose player isn't ready yet are
/// shown, greyed, with what they will need.
/// </summary>
internal sealed class BuildPlatformWindow : Window
{
    public BuildPlatform Picked { get; private set; }

    private sealed record Choice(BuildPlatform Platform, string Name, string Needs, bool Ready);

    private static readonly Choice[] Choices =
    {
        new(BuildPlatform.Windows, "Windows", "Windows 10 or 11, 64-bit, with the free Microsoft .NET 8 Desktop Runtime.", true),
        new(BuildPlatform.Linux, "Linux (preview)", "64-bit Linux (Ubuntu and the like) with SDL2. Preview: writes the game's data for the Sandcastle player (the repo's Player folder: make, then run it on that folder) - so far it draws the world and you walk in it.", true),
        new(BuildPlatform.MacModern, "Mac - Intel and Apple chips (preview)", "macOS 10.13 or later, on an Intel or Apple (M-series) Mac. Preview, as Linux.", true),
        new(BuildPlatform.MacPowerPC, "Mac OS X PowerPC", "Mac OS X 10.4 or 10.5 on a G4 or G5.", false),
        new(BuildPlatform.MorphOS, "MorphOS", "MorphOS 3.x on a PowerPC Mac, Pegasos or Efika; 128 MB of free memory. Sound through AHI. Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
        new(BuildPlatform.AmigaOS4, "AmigaOS 4.x", "AmigaOS 4.1 on an X1000, X5000, A1222 or Sam board. Built as \"Game\" with its icon \"Game.info\".", false),
        new(BuildPlatform.Amiga68k, "Amiga 68k (RTG)", "Kickstart 3.1 or later: AmigaOS 3.1 to 3.2 with an RTG card (Picasso96 or CyberGraphX), or ApolloOS (not AROS); also WinUAE and AmiKit. A 68060 at least, a 68080 recommended, with an FPU (required); 128 MB of RAM at least (up to 1 GB for big games); a hard drive (no floppies). No OCS, ECS or AGA: RTG graphics, Paula sound (AHI if asked for). Runs in a Workbench window or full screen. Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
        new(BuildPlatform.AmigaVampire, "Amiga Vampire", "A Vampire (V4 Standalone, Firebird, V2 cards) only: its SAGA graphics, the 68080's AMMX and the sound hardware driven directly, for the most speed. Won't start on any other Amiga or in an emulator (use Amiga 68k (RTG) there). Kickstart 3.1 or later or ApolloOS; 128 MB of RAM at least; a hard drive. Built as \"Game\" with its icon \"Game.info\".", false),
    };

    /// <param name="ultima8">An Ultima 8 project: its engine runs on Windows so far - the Sandcastle player gets it later (the Amiga / MorphOS work is done on Saturdays).</param>
    public BuildPlatformWindow(bool ultima8 = false)
    {
        Title = "Build the Game";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock { Text = "Which platform is this build for?", FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
        var list = new ListBox { Height = 300 };
        foreach (var c in Choices)
        {
            bool ready = c.Ready && (!ultima8 || c.Platform == BuildPlatform.Windows);
            var item = new StackPanel { Margin = new Thickness(2, 4, 2, 4), Opacity = ready ? 1 : 0.55 };
            item.Children.Add(new TextBlock { Text = c.Name + (ready ? "" : ultima8 && c.Ready ? "   (the Ultima 8 engine comes to this player later)" : "   (its player is being written)"), FontWeight = FontWeights.SemiBold });
            item.Children.Add(new TextBlock { Text = c.Needs, TextWrapping = TextWrapping.Wrap, FontSize = 11, Width = 450, Foreground = Brushes.DimGray });
            list.Items.Add(new ListBoxItem { Content = item, Tag = c, IsEnabled = ready });
        }
        list.SelectedIndex = 0;
        rows.Children.Add(list);
        rows.Children.Add(new TextBlock { Text = "Windows runs on this engine itself. Every other platform runs the game on the Sandcastle player, written in C for it - greyed ones become available as their player is finished.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        var ok = new Button { Content = "Build...", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is not Choice { Ready: true } c) return;
            Picked = c.Platform;
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        Content = rows;
    }
}
