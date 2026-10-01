using System.Windows;

namespace SandcastleRpgMaker.Editor;

public partial class SimpleInputWindow : Window
{
    public string ResultText { get; private set; } = string.Empty;

    public SimpleInputWindow(string prompt, string title, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>Convenience: shows the dialog and returns the entered text, or null if cancelled.</summary>
    public static string? Show(Window owner, string prompt, string title, string defaultValue = "")
    {
        var dialog = new SimpleInputWindow(prompt, title, defaultValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }
}
