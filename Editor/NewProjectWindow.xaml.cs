using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Creates a project's folder plus its three married files: the encrypted
/// .msrp manifest, and empty shapes.sdbx / objects.sdbx databases stamped
/// with the same project ID from the moment the project exists - not only
/// once the person first hits Save.
/// </summary>
public partial class NewProjectWindow : Window
{
    public bool Result { get; private set; }
    public string? ProjectFilePath { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public NewProjectWindow()
    {
        InitializeComponent();
        PopulatePlatforms();
        UpdatePreviews();
    }

    private void PopulatePlatforms()
    {
        void Add(string label, bool enabled, string? tooltip = null)
        {
            var item = new ComboBoxItem { Content = label, IsEnabled = enabled };
            if (tooltip is not null) item.ToolTip = tooltip;
            PlatformCombo.Items.Add(item);
        }

        Add("Windows", true);
        Add("Macintosh (PPC / x86 / 2026 Apple chip) (Not Implemented Yet)", false);
        Add("Linux (Not Implemented Yet)", false);
        Add("HTML5 (Not Implemented Yet)", false);
        Add("PlayStation 3 (Not Implemented Yet)", false);
        Add("PlayStation 2 (Not Implemented Yet)", false);
        Add("PlayStation 4 (Not Implemented Yet)", false);
        Add("Amiga 68k (68020-68060, OCS to AGA, Kickstart 1.3-3.1) (Not Implemented Yet)", false,
            "Will configure custom chipset/RAM/Kickstart/CPU (WinUAE-style) and the hardware limits that come with them, once this platform is implemented.");
        Add("Vamperized Amiga 68k (Not Implemented Yet)", false);
        Add("MorphOS (PPC Mac) (Not Implemented Yet)", false);

        PlatformCombo.SelectedIndex = 0;
    }

    private void ProjectNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreviews();
    private void LocationBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreviews();

    private void UpdatePreviews()
    {
        string folderName = ComputeFolderName(ProjectNameBox.Text);
        FolderNamePreviewText.Text = string.IsNullOrEmpty(folderName) ? "(enter a project name)" : folderName;

        string location = LocationBox.Text.Trim();
        FullPathPreviewText.Text = string.IsNullOrEmpty(location) || string.IsNullOrEmpty(folderName)
            ? string.Empty
            : $"Will create: {Path.Combine(location, folderName)}\\";
    }

    /// <summary>Spaces become dots (so the folder/file name never matches the human-readable project name exactly, avoiding the Windows same-name-different-purpose confusion the project name alone could cause); any character Windows won't allow in a folder name becomes an underscore instead of silently vanishing.</summary>
    private static string ComputeFolderName(string projectName)
    {
        string trimmed = projectName.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c)) sb.Append('.');
            else if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
            else sb.Append(c);
        }

        string collapsed = Regex.Replace(sb.ToString(), "\\.{2,}", ".");
        return collapsed.Trim('.', ' ');
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where to create the project folder"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            LocationBox.Text = dialog.SelectedPath;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string projectName = ProjectNameBox.Text.Trim();
        if (projectName.Length == 0)
        {
            MessageBox.Show("Enter a project name.", "New Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string location = LocationBox.Text.Trim();
        if (location.Length == 0 || !Directory.Exists(location))
        {
            MessageBox.Show("Choose a valid, existing location folder.", "New Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string folderName = ComputeFolderName(projectName);
        if (folderName.Length == 0)
        {
            MessageBox.Show("That name doesn't leave any valid folder characters - try a different project name.", "New Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PlatformCombo.SelectedItem is not ComboBoxItem { IsEnabled: true } platformItem)
        {
            MessageBox.Show("That platform isn't implemented yet - pick Windows for now.", "New Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string projectFolder = Path.Combine(location, folderName);
        if (Directory.Exists(projectFolder) && Directory.EnumerateFileSystemEntries(projectFolder).Any())
        {
            MessageBox.Show($"\"{projectFolder}\" already exists and isn't empty. Choose a different name or location.", "New Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(projectFolder);
            string msrpPath = Path.Combine(projectFolder, folderName + ".msrp");
            string shapePath = Path.Combine(projectFolder, "shapes.sdbx");
            string objectsPath = Path.Combine(projectFolder, "objects.sdbx");
            string audiosPath = Path.Combine(projectFolder, "audios.sdbx");

            var id = Guid.NewGuid();
            var manifest = new ProjectManifest
            {
                ProjectId = id,
                ProjectName = projectName,
                Platform = platformItem.Content?.ToString() ?? "Windows",
                CreatedAtUtc = DateTime.UtcNow
            };

            // All four files are married from the moment the project exists,
            // not only once the person first hits Save - every one of them is
            // encrypted and stamped with the same ProjectId. .msrp itself
            // now carries everything that "glues the project together" and
            // isn't specifically sprite/audio/object data - factions,
            // variables, stats, tile properties - not just identity fields.
            var payload = new ManifestPayload
            {
                Manifest = manifest,
                Factions = new FactionDatabase(),
                Variables = new VariableDatabase(),
                Stats = StatDatabase.CreateDefault(),
                Poses = PoseDatabase.CreateDefault(),
                TileProperties = new TilePropertiesDatabase()
            };
            ProjectFiles.SaveManifest(msrpPath, payload);
            var freshSprites = new SpriteDatabase();
            freshSprites.EnsureBuiltInGroups();
            ProjectFiles.SaveShapeSdbx(shapePath, id, freshSprites);
            ProjectFiles.SaveObjectsSdbx(objectsPath, id, new BookDatabase());
            ProjectFiles.SaveAudiosSdbx(audiosPath, id, new ResourceDatabase());

            ProjectFilePath = msrpPath;
            ProjectId = id;
            ProjectName = projectName;
            Platform = manifest.Platform;
            CreatedAtUtc = manifest.CreatedAtUtc;
            Result = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create the project: {ex.Message}", "New Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
