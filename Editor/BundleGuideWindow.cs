using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How the Ammo and Money sprite groups work, shown when a sprite is added
/// to either: one object (fire arrows, gold coins) has eight bundle
/// pictures, one per Exult quantity frame, each its own sprite here - so
/// each can animate and make sounds of its own.
/// </summary>
public class BundleGuideWindow : Window
{
    public BundleGuideWindow(string groupName)
    {
        bool money = groupName == SpriteDatabase.MoneyGroupName;
        bool lockpicks = groupName == SpriteDatabase.LockpickGroupName;
        string thing = money ? "coins" : lockpicks ? "lockpicks" : "arrows";
        string example = money ? "GoldCoins" : lockpicks ? "Lockpicks" : "FireArrow";
        Title = $"{groupName} Guide";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock { Text = $"How the {groupName} group works", FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(new TextBlock
        {
            Text = $"In Ultima VII a pile of {thing} looks bigger the more there are. Exult draws it with one of eight pictures, picked by how many are in the stack " +
                   $"(objs.cc, \"Verified\"): one each for 1, 2, 3, 4, 5 and 6, one for 7 to 12, and one for 13 or more. A stack holds up to {ObjectTypes.MaxQuantity}.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock { Text = "Make one sprite per picture:", FontWeight = FontWeights.SemiBold });
        var grid = new Grid { Margin = new Thickness(12, 4, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < ObjectTypes.BundleLevelNames.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var name = new TextBlock { Text = $"{example}_{i + 1}", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            var level = new TextBlock { Text = $"shown for {ObjectTypes.BundleLevelNames[i]} {thing}" };
            Grid.SetRow(name, i); Grid.SetRow(level, i); Grid.SetColumn(level, 1);
            grid.Children.Add(name);
            grid.Children.Add(level);
        }
        root.Children.Add(grid);
        root.Children.Add(new TextBlock
        {
            Text = "Each sprite is an ordinary sprite: give it several frames to animate it (burning arrows, glinting gold) and a sound on any frame. " +
                   "Then, in the Object Editor, make an " + (money ? "object of type Money" : lockpicks ? "object of type Picklock" : "object of type Ammo") +
                   " and pick these eight sprites for its eight bundle pictures. A picture left empty uses the nearest smaller one that's set, so even one sprite works.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = money
                ? "Money: its Weight is per coin (a stack weighs Weight x how many), and it's worth its currency's value per coin (Currency Editor)."
                : lockpicks ? "Picklock: its Weight is per lockpick. A failed try breaks one - the stack gets one smaller, and its picture changes with it."
                : "Ammo: its Weight is per arrow (a stack weighs Weight x how many). It goes in the paperdoll slot set in the Object Editor.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10)
        });
        var ok = new Button { Content = "Got it", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        root.Children.Add(ok);
        Content = root;
    }
}
