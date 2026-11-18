using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>The platforms a game is built for (Game > Build...).</summary>
public enum BuildPlatform { Windows, Linux, MacModern, MacPowerPC, MorphOS, AmigaOS4, Amiga68k, AmigaVampire, PlayStation2, PlayStation3 }

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
        new(BuildPlatform.PlayStation2, "PlayStation 2", "A disc image (ISO), at most 8 GB, with the ESR patch already on it: burn it to a DVD (dual-layer above 4.7 GB) and start it with ESR from FreeMcBoot - or run the ISO in PCSX2 or OPL. Played with the PS2 controller; Ultima 6 games need a USB keyboard and mouse plugged into the PS2.", true),
        new(BuildPlatform.PlayStation3, "PlayStation 3", "One installer (.pkg), never a disc image: copy it to a USB stick (or the PS3's /dev_hdd0/packages), then on a PS3 running HEN or a custom firmware use Package Manager > Install Package Files. Installed from the hard drive it deletes itself when done. Played with the PS3 controller (a USB keyboard and mouse work too); saves go beside the game on the PS3's hard drive.", true),
        new(BuildPlatform.Linux, "Ubuntu (24.04 and newer)", "One installer (.deb) for 64-bit Ubuntu 24.04 or newer: double-click it, Ubuntu's App Center installs it (and anything it needs with it), and the game appears with the other applications. Saves are kept in the player's home folder.", true),
        new(BuildPlatform.MacModern, "Mac - Intel and Apple chips (preview)", "macOS 10.13 or later, on an Intel or Apple (M-series) Mac. Preview, as Linux.", true),
        new(BuildPlatform.MacPowerPC, "Mac OS X PowerPC", "Mac OS X 10.4 or 10.5 on a G4 or G5.", false),
        new(BuildPlatform.MorphOS, "MorphOS", "MorphOS 3.x on a PowerPC Mac, Pegasos or Efika; 128 MB of free memory. Sound through AHI. Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
        new(BuildPlatform.AmigaOS4, "AmigaOS 4.x", "AmigaOS 4.1 (Final Edition recommended) on an X1000, X5000, A1222, Sam440/460, Pegasos II or AmigaOne; 128 MB of free memory. Sound through AHI (SDL2 is built into the game - nothing to install). Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
        new(BuildPlatform.Amiga68k, "Amiga 68k (RTG)", "Kickstart 3.1 or later: AmigaOS 3.1 to 3.2 with an RTG card (Picasso96 or CyberGraphX), or ApolloOS (not AROS); also WinUAE and AmiKit. A 68060 at least, a 68080 recommended, with an FPU (required); 128 MB of RAM at least (up to 1 GB for big games); a hard drive (no floppies). No OCS, ECS or AGA: RTG graphics, Paula sound (AHI if asked for). Runs in a Workbench window or full screen. Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
        new(BuildPlatform.AmigaVampire, "Amiga Vampire", "A Vampire (Apollo 68080) only - V4 Standalone, V4 Firebird, V1200, V600, V500 - with its SAGA driver (P96): the game takes over SAGA's display itself (640 x 400, flipped at the vertical blank), draws with the 68080's AMMX and plays through the Vampire's own 16-bit sound (no AHI, no Paula). Won't start on any other Amiga or in an emulator (use Amiga 68k (RTG) there). Kickstart 3.1 or later or ApolloOS; a hard drive. Built as a drawer: \"Game\" with its icon \"Game.info\", and Game_Data beside it.", true),
    };

    /// <param name="ultima8">An Ultima 8 project: every platform the Sandcastle player runs on (its Ultima 8 engine: Player/src/u8.c and the diamond view in world.c).</param>
    /// <param name="ultima6">An Ultima 6 project: every platform the Sandcastle player runs on (its Ultima 6 engine: Player/src/u6.c).</param>
    public BuildPlatformWindow(bool ultima8 = false, bool ultima6 = false)
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
            bool ready = c.Ready && (c.Platform != BuildPlatform.PlayStation3 || File.Exists(Path.Combine(AppContext.BaseDirectory, "Runtimes", "PS3", "EBOOT.elf"))) && (c.Platform != BuildPlatform.Linux || File.Exists(Path.Combine(AppContext.BaseDirectory, "Runtimes", "Linux", "Game"))) && (c.Platform != BuildPlatform.AmigaVampire || File.Exists(Path.Combine(AppContext.BaseDirectory, "Runtimes", "AmigaVampire", "Game"))) && (c.Platform != BuildPlatform.AmigaOS4 || File.Exists(Path.Combine(AppContext.BaseDirectory, "Runtimes", "AmigaOS4", "Game"))); // Ultima 6 and 8: every platform the Sandcastle player runs on; the PS3 once its player is built
            var item = new StackPanel { Opacity = ready ? 1 : 0.55 };
            item.Background = Backdrop(c.Platform);
            item.Children.Add(new TextBlock { Text = c.Name + (ready ? "" : ultima8 && c.Ready ? "   (the Ultima 8 engine comes to this player later)" : ultima6 && c.Ready ? "   (the Ultima 6 engine comes to this player later)" : "   (its player is being written)"), FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 3, 6, 0) });
            item.Children.Add(new TextBlock { Text = c.Needs, TextWrapping = TextWrapping.Wrap, FontSize = 11, Width = 430, Foreground = new SolidColorBrush(Color.FromRgb(225, 228, 235)), Margin = new Thickness(6, 0, 6, 4) }); // readable on its band
            // a frame round each band: lit while the pointer is on it, glowing (animated) while it's the one picked
            var frame = new Border { Child = item, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Colors.Transparent), Margin = new Thickness(1, 3, 1, 3), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            var row = new ListBoxItem { Content = frame, Tag = c, IsEnabled = ready, Padding = new Thickness(0) };
            row.Selected += (_, _) => Highlight(frame, picked: true, hover: row.IsMouseOver);
            row.Unselected += (_, _) => Highlight(frame, picked: false, hover: row.IsMouseOver);
            row.MouseEnter += (_, _) => Highlight(frame, row.IsSelected, hover: true);
            row.MouseLeave += (_, _) => Highlight(frame, row.IsSelected, hover: false);
            list.Items.Add(row);
        }
        list.SelectedIndex = 0;
        rows.Children.Add(list);
        var logos = new TextBlock { FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        var open = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Logos folder"));
        open.Click += (_, _) => { Directory.CreateDirectory(LogoFolder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogoFolder) { UseShellExecute = true }); };
        logos.Inlines.Add("Logos: put a picture named windows, ps2, linux, mac, macppc, morphos, os4, amiga68k or vampire (.png or .jpg) in the ");
        logos.Inlines.Add(open);
        logos.Inlines.Add(" and it shows on that platform's band (reopen this window).");
        rows.Children.Add(logos);
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

    /// <summary>
    /// The frame round a band: the one picked glows - its border pulsing gold to white and the band a little bigger, so it's
    /// plain which one is picked; the one under the pointer gets a quiet light border; the others none.
    /// </summary>
    private static void Highlight(Border frame, bool picked, bool hover)
    {
        var brush = (SolidColorBrush)frame.BorderBrush;
        var scale = (ScaleTransform)frame.RenderTransform;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (picked)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(Color.FromRgb(255, 190, 30), Colors.White, TimeSpan.FromMilliseconds(650))
            { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            frame.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(255, 200, 60), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
        }
        else
        {
            brush.Color = hover ? Color.FromArgb(170, 200, 210, 230) : Colors.Transparent;
            frame.Effect = null;
        }
        double to = picked ? 1.02 : hover ? 1.01 : 1;
        var ease = new System.Windows.Media.Animation.QuadraticEase();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
    }

    /// <summary>
    /// The logos' folder, beside the editor: a picture there named for a platform (windows, ps2, linux, mac, macppc, morphos,
    /// os4, amiga68k, vampire - .png or .jpg) is shown on that platform's band in place of its lettering. The pictures are
    /// the user's own files: the editor doesn't come with the makers' logos (they are the makers' trademarks).
    /// </summary>
    public static string LogoFolder => Path.Combine(AppContext.BaseDirectory, "Logos");

    private static string LogoName(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Windows => "windows", BuildPlatform.PlayStation2 => "ps2", BuildPlatform.Linux => "linux",
        BuildPlatform.MacModern => "mac", BuildPlatform.MacPowerPC => "macppc", BuildPlatform.MorphOS => "morphos",
        BuildPlatform.AmigaOS4 => "os4", BuildPlatform.Amiga68k => "amiga68k", BuildPlatform.AmigaVampire => "vampire", _ => "",
    };

    private static System.Windows.Media.Imaging.BitmapImage? Logo(BuildPlatform platform)
    {
        foreach (string ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = Path.Combine(LogoFolder, LogoName(platform) + ext);
            if (!File.Exists(path)) continue;
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // read now: the file isn't kept open
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { /* not a picture: the lettering instead */ }
        }
        return null;
    }

    /// <summary>
    /// Behind each choice: a band in the platform's own colours with the platform's logo on it if there's one in the Logos
    /// folder (LogoFolder), else large lettering of our own ("PS2", "WIN", "68K"...).
    /// </summary>
    private static Brush Backdrop(BuildPlatform platform)
    {
        var (word, from, to, italic) = platform switch
        {
            BuildPlatform.Windows => ("WIN", Color.FromRgb(0, 60, 120), Color.FromRgb(0, 110, 200), false),
            BuildPlatform.PlayStation2 => ("PS2", Color.FromRgb(0, 32, 96), Color.FromRgb(0, 70, 160), true),
            BuildPlatform.PlayStation3 => ("PS3", Color.FromRgb(20, 20, 24), Color.FromRgb(70, 74, 84), true),
            BuildPlatform.Linux => ("LINUX", Color.FromRgb(24, 24, 24), Color.FromRgb(120, 92, 10), false),
            BuildPlatform.MacModern => ("MAC", Color.FromRgb(40, 42, 48), Color.FromRgb(110, 114, 124), false),
            BuildPlatform.MacPowerPC => ("PPC", Color.FromRgb(20, 50, 80), Color.FromRgb(60, 130, 170), true),
            BuildPlatform.MorphOS => ("MorphOS", Color.FromRgb(10, 40, 70), Color.FromRgb(20, 110, 140), true),
            BuildPlatform.AmigaOS4 => ("OS4", Color.FromRgb(90, 20, 10), Color.FromRgb(200, 80, 20), false),
            BuildPlatform.Amiga68k => ("68K", Color.FromRgb(110, 0, 0), Color.FromRgb(200, 30, 30), false),
            BuildPlatform.AmigaVampire => ("V4", Color.FromRgb(30, 0, 40), Color.FromRgb(110, 20, 90), true),
            _ => ("", Color.FromRgb(40, 40, 40), Color.FromRgb(80, 80, 80), false),
        };
        var text = new TextBlock { Text = word, FontSize = 40, FontWeight = FontWeights.Black, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal, Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) };
        var band = new Grid { Width = 450, Height = 56, Background = new LinearGradientBrush(from, to, 0) };
        FrameworkElement mark = text;
        if (Logo(platform) is { } logo)
            mark = new System.Windows.Controls.Image { Source = logo, Height = 46, MaxWidth = 140, Stretch = Stretch.Uniform, Opacity = 0.9 };
        mark.HorizontalAlignment = HorizontalAlignment.Right;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(0, 0, 10, 0);
        band.Children.Add(mark);
        return new VisualBrush(band) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, Opacity = 0.9 };
    }
}
