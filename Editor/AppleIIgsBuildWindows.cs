using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>How an Ultima 6 Apple IIgs game is built (Game > Build...).</summary>
public enum AppleIIgsDiskKind { ProDosHardDisk, HfsHardDisk, Floppies }

/// <summary>
/// Game > Build... in Ultima 6 Apple IIgs mode: one hard disk (ProDOS, up to 32 MB; or HFS, up to 2 GB), or as many
/// 3.5-inch 800 KB floppies as the game needs, each with its own name (the Disk Swap screen asks for it by that name).
/// </summary>
internal sealed class AppleIIgsBuildWindow : Window
{
    public AppleIIgsDiskKind Kind { get; private set; } = AppleIIgsDiskKind.ProDosHardDisk;
    public List<string> DiskNames { get; } = new();

    public AppleIIgsBuildWindow(long gameBytes, int floppiesNeeded, List<string> names, string gameName)
    {
        Title = "Build the Apple IIgs game";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock { Text = $"The game is {gameBytes / 1024} KB. Build it as:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        var prodos = new RadioButton { Content = "One hard disk, ProDOS (.2mg, .po or .hdv) - up to 32 MB", IsChecked = true, IsEnabled = gameBytes < 31L * 1024 * 1024 };
        var hfs = new RadioButton { Content = "One hard disk, HFS (.2mg or .hdv) - up to 2 GB", Margin = new Thickness(0, 4, 0, 0) };
        var floppies = new RadioButton { Content = $"3.5-inch floppies, ProDOS 800 KB (.2mg each) - this game needs {floppiesNeeded}", Margin = new Thickness(0, 4, 0, 0), IsEnabled = floppiesNeeded > 0 };
        if (!prodos.IsEnabled) hfs.IsChecked = true;
        rows.Children.Add(prodos);
        rows.Children.Add(hfs);
        rows.Children.Add(floppies);
        rows.Children.Add(new TextBlock
        {
            Text = "A hard disk: double-click it on the GS/OS desktop, then SANDCASTLE. Floppies aren't bootable: start GS/OS from its own disk, put disk 1 in, double-click SANDCASTLE; when the game needs a file on another disk, the Disk Swap screen (HUD Editor) asks for that disk by its name. Copy every disk's files into one folder of a hard disk and the game runs from there and never asks.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 8)
        });
        var namesLabel = new TextBlock { Text = "The disks' names, one a line (disk 1 first) - shown on the Disk Swap screen and on the disks:", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var namesBox = new TextBox { AcceptsReturn = true, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        namesBox.Text = string.Join("\r\n", Enumerable.Range(1, Math.Max(1, floppiesNeeded)).Select(i => i - 1 < names.Count && names[i - 1].Trim().Length > 0 ? names[i - 1].Trim() : $"{gameName} {i}"));
        var namesPanel = new StackPanel { Visibility = Visibility.Collapsed };
        namesPanel.Children.Add(namesLabel);
        namesPanel.Children.Add(namesBox);
        rows.Children.Add(namesPanel);
        floppies.Checked += (_, _) => namesPanel.Visibility = Visibility.Visible;
        floppies.Unchecked += (_, _) => namesPanel.Visibility = Visibility.Collapsed;
        var ok = new Button { Content = "Build...", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Kind = floppies.IsChecked == true ? AppleIIgsDiskKind.Floppies : hfs.IsChecked == true ? AppleIIgsDiskKind.HfsHardDisk : AppleIIgsDiskKind.ProDosHardDisk;
            DiskNames.AddRange(namesBox.Text.Replace("\r", "").Split('\n').Select(n => n.Trim()));
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        Content = rows;
    }
}

/// <summary>
/// HUD Editor > Disk Swap screen (Ultima 6 Apple IIgs mode): what the game shows when it needs another floppy - a picture
/// over the whole screen, an animation on it, a sound, the words ({0}: the disk's number, {$}: its name) - and the disks' names.
/// </summary>
internal sealed class DiskSwapScreenWindow : Window
{
    public DiskSwapScreenWindow(DiskSwapScreen screen, SpriteDatabase sprites, ResourceDatabase resources)
    {
        Title = "Disk Swap screen (Apple IIgs)";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock
        {
            Text = "A game built as 3.5-inch floppies shows this when it needs a file on another disk, until that disk is in and a key is pressed. Make it as you like: a picture, an animation (a sprite with frames), a sound, your own words - {0} is the disk's number (1, 2, 3...) and {$} its name from the build's list of disk names (\"Disk A\", \"Town A\", \"Dungeon\"...): \"Please insert disk {$}\".",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        var all = sprites.Groups.SelectMany(g => g.Sprites.Select(s => (Group: g.Name, Sprite: s.Name))).ToList();
        ComboBox SpritePick(SpriteLink current)
        {
            var box = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
            box.Items.Add(new ComboBoxItem { Content = "(none)", Tag = null });
            foreach (var (group, sprite) in all)
            {
                var item = new ComboBoxItem { Content = $"{group} / {sprite}", Tag = (group, sprite) };
                box.Items.Add(item);
                if (current.SpriteGroup == group && current.SpriteName == sprite) box.SelectedItem = item;
            }
            if (box.SelectedIndex < 0) box.SelectedIndex = 0;
            return box;
        }
        TextBox Number(int value) => new() { Text = value.ToString(), Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
        void Label(string text) => rows.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        Label("Picture (the whole screen, 320 x 200):");
        var picture = SpritePick(screen.Picture);
        rows.Children.Add(picture);
        Label("Animation (a sprite; its frames play while the screen waits) and where it goes (x, y):");
        var animation = SpritePick(screen.Animation);
        rows.Children.Add(animation);
        var place = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var ax = Number(screen.AnimationX); var ay = Number(screen.AnimationY);
        place.Children.Add(ax); place.Children.Add(new TextBlock { Text = " , ", VerticalAlignment = VerticalAlignment.Center }); place.Children.Add(ay);
        rows.Children.Add(place);
        Label("The words ({0} = the disk's number, {$} = its name):");
        var message = new TextBox { Text = screen.Message };
        rows.Children.Add(message);
        Label("Where the words go (x, y) and their colour (#RRGGBB; empty: the brightest):");
        var textPlace = new StackPanel { Orientation = Orientation.Horizontal };
        var tx = Number(screen.TextX); var ty = Number(screen.TextY);
        var colour = new TextBox { Text = screen.TextColor, Width = 90, Margin = new Thickness(12, 0, 0, 0) };
        textPlace.Children.Add(tx); textPlace.Children.Add(new TextBlock { Text = " , ", VerticalAlignment = VerticalAlignment.Center }); textPlace.Children.Add(ty); textPlace.Children.Add(colour);
        rows.Children.Add(textPlace);
        Label("Sound as it comes up:");
        var sound = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        sound.Items.Add(new ComboBoxItem { Content = "(none)", Tag = -1 });
        foreach (var r in resources.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music))
        {
            var item = new ComboBoxItem { Content = r.Name, Tag = r.Id };
            sound.Items.Add(item);
            if (r.Id == screen.SoundId) sound.SelectedItem = item;
        }
        if (sound.SelectedIndex < 0) sound.SelectedIndex = 0;
        rows.Children.Add(sound);
        Label("The disks' names, one a line (disk 1 first; Build asks again and keeps them here):");
        var names = new TextBox { AcceptsReturn = true, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = string.Join("\r\n", screen.DiskNames) };
        rows.Children.Add(names);
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            SpriteLink Link(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag is ValueTuple<string, string> t ? new SpriteLink { SpriteGroup = t.Item1, SpriteName = t.Item2 } : new SpriteLink();
            screen.Picture = Link(picture);
            screen.Animation = Link(animation);
            screen.AnimationX = int.TryParse(ax.Text, out int v1) ? Math.Clamp(v1, 0, 318) & ~1 : screen.AnimationX;
            screen.AnimationY = int.TryParse(ay.Text, out int v2) ? Math.Clamp(v2, 0, 199) : screen.AnimationY;
            screen.Message = message.Text;
            screen.TextX = int.TryParse(tx.Text, out int v3) ? Math.Clamp(v3, 0, 319) : screen.TextX;
            screen.TextY = int.TryParse(ty.Text, out int v4) ? Math.Clamp(v4, 0, 199) : screen.TextY;
            screen.TextColor = colour.Text.Trim();
            screen.SoundId = (sound.SelectedItem as ComboBoxItem)?.Tag is int id ? id : -1;
            screen.DiskNames = names.Text.Replace("\r", "").Split('\n').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        Content = rows;
    }
}
