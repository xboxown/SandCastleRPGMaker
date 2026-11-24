using System.Windows;

namespace SandcastleRpgMaker.Editor;

/// <summary>Tools > AI Artist: sprites drawn from words on this PC's graphics card (AiArtistWindow, SandcastleRpgMaker.AI.dll).</summary>
public partial class MainWindow
{
    private void AiArtistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new AiArtistWindow(_spriteDatabase) { Owner = this }.ShowDialog();
        RefreshTilesetCombo();
    }

    /// <summary>Tools > Sprite Composer (sprite generator A).</summary>
    private void SpriteComposerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new SpriteComposerWindow(_spriteDatabase) { Owner = this }.ShowDialog();
        RefreshTilesetCombo();
    }
}
