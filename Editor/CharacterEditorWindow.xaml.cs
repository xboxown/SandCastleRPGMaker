using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class CharacterEditorWindow : Window
{
    /// <summary>One row of the Stats list - reads/writes directly into the selected character's StatValues on Value change, keyed by stat Id (not name), matching the model's own reasoning for that choice.</summary>
    private class StatRow : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly CharacterDefinition _character;
        private readonly int _statId;
        private readonly bool _hasMinimum;
        public string Label { get; }
        public string Value
        {
            get => _character.StatValues.TryGetValue(_statId, out var v) ? v : string.Empty;
            set
            {
                // Built-in Integer stats are never blank or below 1; the box shows the corrected value right away.
                _character.StatValues[_statId] = _hasMinimum ? StatDatabase.ClampBuiltIn(value) : value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public StatRow(CharacterDefinition character, StatDefinition stat)
        {
            _character = character;
            _statId = stat.Id;
            _hasMinimum = StatDatabase.HasMinimum(stat);
            Label = $"{stat.Name} ({stat.Type})";
        }
    }

    private readonly CharacterDatabase _database;
    private readonly StatDatabase _stats;
    private readonly PoseDatabase _poses;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private bool _suppress;
    private string? _faceFilterGroup;
    private string? _paperdollBodyFilterGroup;
    private string? _exitButtonFilterGroup;
    /// <summary>Which slot marker (if any) is currently being dragged on the paperdoll canvas - null when nothing is grabbed.</summary>
    /// <summary>Real, confirmed redesign - either an EquipmentSlot (a built-in slot) or a CustomEquipmentSlot (a designer-added one), so the exact same drag code below works for both without duplicating it.</summary>
    private object? _draggingSlot;
    /// <summary>Real, confirmed new state, per spec ("the ability to delete custom slots by selecting one and pressing Delete") - which custom slot (if any) is currently selected for rename/delete; a plain click on one selects it without necessarily starting a drag.</summary>
    private CustomEquipmentSlot? _selectedCustomSlot;
    private Point _dragStartMouse;
    private Point _dragStartSlotPos;

    public CharacterEditorWindow(CharacterDatabase database, StatDatabase stats, PoseDatabase poses, SpriteDatabase sprites, BookDatabase books, ObjectDatabase objects, ResourceDatabase resources)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        _database = database;
        _stats = stats;
        _poses = poses;
        _sprites = sprites;
        _resources = resources;
        _books = books;
        _objects = objects;
        RefreshCharacterList();
        Loaded += (_, _) =>
        {
            if (InitialCharacterId is int id && _database.Characters.FirstOrDefault(c => c.Id == id) is { } initial)
            {
                CharacterListBox.SelectedItem = initial;
                CharacterListBox.ScrollIntoView(initial);
                FactionCombo.Focus();
            }
        };
    }

    /// <summary>Set before showing to open the editor on that character (the Faction Editor's "give these a new faction" list uses it).</summary>
    public int? InitialCharacterId { get; set; }

    private static readonly Faction NoFaction = new() { Id = -1, Name = "(None)" };

    private void RefreshFactionChoices(int factionId)
    {
        var choices = new List<Faction> { NoFaction };
        choices.AddRange(ProjectSession.Factions.Factions);
        FactionCombo.ItemsSource = choices;
        FactionCombo.SelectedItem = choices.FirstOrDefault(f => f.Id == factionId) ?? NoFaction;
    }

    private void RefreshCharacterList()
    {
        var selected = CharacterListBox.SelectedItem as CharacterDefinition;
        CharacterListBox.ItemsSource = null;
        CharacterListBox.ItemsSource = _database.Characters;
        if (selected is not null && _database.Characters.Contains(selected))
            CharacterListBox.SelectedItem = selected;
    }

    private void NewCharacter_Click(object sender, RoutedEventArgs e)
    {
        var newCharacter = new CharacterDefinition { Id = _database.GetNextId(), Name = "New Character" };
        _database.Characters.Add(newCharacter);
        RefreshCharacterList();
        CharacterListBox.SelectedItem = newCharacter;
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition selected)
        {
            MessageBox.Show("Select a character first.", "Character Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = MessageBox.Show($"Delete \"{selected.Name}\"? This cannot be undone.", "Delete Character", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _database.Characters.Remove(selected);
        RefreshCharacterList();
    }

    private void CharacterListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character)
        {
            DetailsTabs.IsEnabled = false;
            return;
        }

        DetailsTabs.IsEnabled = true;
        _suppress = true;

        NameBox.Text = character.Name;
        AgeBox.Text = character.Age.ToString();
        MaleRadio.IsChecked = character.Gender == CharacterGender.Male;
        FemaleRadio.IsChecked = character.Gender == CharacterGender.Female;

        RefreshClassChoices(character.ClassName);
        RefreshFactionChoices(character.FactionId);
        BuildSchedulePanel(character);
        BuildTalkEditor(character);
        BuildHealthPanel(character);
        BuildEventTab(character);
        TestPlayerCheck.IsChecked = character.IsTestPlayer;
        InPartyCheck.IsChecked = character.IsInParty;
        // A character that has never had a size gets one filled in automatically the first time it's looked at, so the boxes always show the real numbers the engine will use.
        if (character.SizeX <= 0 || character.SizeY <= 0 || character.SizeZ <= 0)
            (character.SizeX, character.SizeY, character.SizeZ) = ActorSizing.Resolve(character, _poses, _sprites);
        ShowSize(character);

        _faceFilterGroup = string.IsNullOrEmpty(character.FaceSpriteGroup) ? null : character.FaceSpriteGroup;
        FaceFilterCheck.IsChecked = _faceFilterGroup is not null;
        PopulateFaceChoices(character.FaceSpriteGroup, character.FaceSpriteName);

        _stats.EnsureBuiltInMinimums(character); // blank built-in stats start at 1
        StatsItemsControl.ItemsSource = _stats.Stats.Select(s => new StatRow(character, s)).ToList();
        BuildPosesPanel(character);

        _paperdollBodyFilterGroup = string.IsNullOrEmpty(character.Paperdoll.BodySpriteGroup) ? null : character.Paperdoll.BodySpriteGroup;
        PaperdollFilterCheck.IsChecked = _paperdollBodyFilterGroup is not null;
        PopulatePaperdollBodyChoices(character.Paperdoll.BodySpriteGroup, character.Paperdoll.BodySpriteName);

        _exitButtonFilterGroup = string.IsNullOrEmpty(character.Paperdoll.ExitButtonSpriteGroup) ? null : character.Paperdoll.ExitButtonSpriteGroup;
        ExitButtonFilterCheck.IsChecked = _exitButtonFilterGroup is not null;
        PopulateExitButtonChoices(character.Paperdoll.ExitButtonSpriteGroup, character.Paperdoll.ExitButtonSpriteName);

        // Real Navigate Sound picker, per spec - same honest flat-list pattern every other sound picker in this project already uses (ResourceDatabase.Entries has no real group concept to filter by at all).
        var navigateSoundChoices = new List<ResourceEntry> { NoneSound };
        navigateSoundChoices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        PaperdollNavigateSoundCombo.ItemsSource = navigateSoundChoices;
        PaperdollNavigateSoundCombo.SelectedItem = navigateSoundChoices.FirstOrDefault(s => s.Id == character.Paperdoll.NavigateSoundResourceId) ?? NoneSound;
        // Its own list - two ComboBoxes given the SAME list share one current-item view in WPF and silently change each other's selection (which reset this to "(None)" and overwrote the saved sound).
        var openSoundChoices = new List<ResourceEntry>(navigateSoundChoices);
        PaperdollOpenSoundCombo.ItemsSource = openSoundChoices;
        PaperdollOpenSoundCombo.SelectedItem = openSoundChoices.FirstOrDefault(s => s.Id == character.Paperdoll.OpenSoundResourceId) ?? NoneSound;

        // Real, confirmed reset - a custom-slot selection belongs to whichever character it was made on; switching characters clears it rather than carrying a stale selection (or its rename box) over to a different character's own paperdoll.
        _selectedCustomSlot = null;
        UpdateCustomSlotNameBox();
        BuildPaperdollCanvas(character);
        RefreshInventoryList(character);
        PopulateInventoryCategoryChoices();

        _suppress = false;
    }

    /// <summary>
    /// One row per pose slot, built programmatically (not a DataTemplate)
    /// since each row needs its own independent Filter By Group state and
    /// picker button - the same interaction Face already has, just once
    /// per pose instead of once total. Reading PoseDatabase live here
    /// means a pose added in the Pose Editor shows up the next time this
    /// window opens, same "no separate sync" guarantee as Stats.
    /// </summary>
    private void AddCustomPose_Click(object sender, RoutedEventArgs e) => PromptAddCustomPose();

    private void PromptAddCustomPose()
    {
        var name = SimpleInputWindow.Show(this, "Pose name:", "Add Custom Pose", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var newPose = new PoseDefinition { Id = _poses.GetNextId(), Name = name.Trim(), IsBuiltIn = false };
        _poses.Poses.Add(newPose);

        if (CharacterListBox.SelectedItem is CharacterDefinition character) BuildPosesPanel(character);
    }

    /// <summary>Which direction's frames are currently shown for a given pose - independent per pose, since each pose's dial has its own selection. Defaults to South, a sensible default facing, the first time a pose is shown.</summary>
    private readonly Dictionary<int, SpriteDirection> _poseSelectedDirection = new();

    /// <summary>Clipboard for right-click Copy/Paste at the whole-pose level (all 8 directions + mute at once) and at the single-direction level - deep-cloned on copy so pasting more than once never has multiple targets secretly share the same link objects.</summary>
    private PoseAssignment? _copiedPoseAssignment;
    private PoseSpriteLink? _copiedDirectionLink;

    private static PoseAssignment CloneAssignment(PoseAssignment source) => new()
    {
        Muted = source.Muted,
        Volume = source.Volume,
        Directions = source.Directions.ToDictionary(kv => kv.Key, kv => new PoseSpriteLink { SpriteGroup = kv.Value.SpriteGroup, SpriteName = kv.Value.SpriteName })
    };

    /// <summary>
    /// One self-contained block per pose: bold title above, a bordered
    /// panel with a "Direction:" compass dial and the copy/paste hint
    /// text, and a 5-square frame-preview strip below showing the
    /// currently-selected direction's assigned sprite. Rebuilt whole on
    /// any change (direction selection, sprite assignment, copy/paste) -
    /// simple and correct, and cheap enough for the number of poses a
    /// character has. No inline "+" tile this pass - Add Custom Pose
    /// above the grid is the only way to add one, matching the sketch.
    /// </summary>
    private void BuildPosesPanel(CharacterDefinition character)
    {
        AnimationPreviewButton.StopAll(); // a rebuild never leaves a preview playing behind it
        PosesPanel.Children.Clear();

        foreach (var pose in _poses.Poses)
        {
            if (!character.PoseSprites.TryGetValue(pose.Id, out var assignment))
            {
                assignment = new PoseAssignment();
                character.PoseSprites[pose.Id] = assignment;
            }
            if (!_poseSelectedDirection.TryGetValue(pose.Id, out var selectedDirection))
            {
                selectedDirection = SpriteDirection.South;
                _poseSelectedDirection[pose.Id] = selectedDirection;
            }

            var block = new StackPanel { Margin = new Thickness(6) };

            block.Children.Add(new TextBlock { Text = pose.Name, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(2, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Center });

            var dialPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8)
            };
            var dialStack = new StackPanel();

            // Preview picture first (large), the direction wheel underneath it - per spec, not the other way around.
            dialStack.Children.Add(BuildPosePreviewSquare(pose, assignment, selectedDirection, character));

            dialStack.Children.Add(new TextBlock { Text = "Direction:", FontSize = 11, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
            dialStack.Children.Add(BuildDirectionDial(pose, assignment, character));

            dialStack.Children.Add(BuildVolumeRow(pose, assignment, character));

            dialStack.Children.Add(new TextBlock
            {
                Text = "Right-click pose or direction to copy/paste.",
                FontSize = 10, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, MaxWidth = 130, Margin = new Thickness(0, 6, 0, 0)
            });
            dialPanel.Child = dialStack;
            dialPanel.ContextMenu = BuildPoseContextMenu(pose, character, assignment);
            block.Children.Add(dialPanel);

            var overlay = new Grid();
            overlay.Children.Add(block);
            if (assignment.Muted)
            {
                overlay.Children.Add(new TextBlock
                {
                    Text = "\U0001F507", FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(6, 20, 0, 0), ToolTip = "Muted"
                });
            }

            PosesPanel.Children.Add(overlay);
        }
    }

    /// <summary>Mute toggle + volume slider, per pose - per spec, every pose starts muted, and raising/lowering volume doesn't itself unmute (they're independent controls).</summary>
    private UIElement BuildVolumeRow(PoseDefinition pose, PoseAssignment assignment, CharacterDefinition character)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };

        var muteButton = new TextBlock
        {
            Text = assignment.Muted ? "\U0001F507" : "\U0001F50A",
            FontSize = 14, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = assignment.Muted ? "Muted - click to unmute" : "Unmuted - click to mute"
        };
        muteButton.MouseLeftButtonDown += (_, _) =>
        {
            assignment.Muted = !assignment.Muted;
            BuildPosesPanel(character);
        };
        row.Children.Add(muteButton);

        var slider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = assignment.Volume,
            Width = 80, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Volume (doesn't itself unmute)"
        };
        slider.ValueChanged += (_, e) => assignment.Volume = (int)e.NewValue;
        row.Children.Add(slider);

        return row;
    }

    /// <summary>Copy/Paste for the whole pose (all directions + mute at once), plus Rename/Delete for custom poses only - built-ins get neither, per spec.</summary>
    private ContextMenu BuildPoseContextMenu(PoseDefinition pose, CharacterDefinition character, PoseAssignment assignment)
    {
        var menu = new ContextMenu();

        var copyItem = new MenuItem { Header = $"Copy \"{pose.Name}\"" };
        copyItem.Click += (_, _) => _copiedPoseAssignment = CloneAssignment(assignment);
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = "Paste", IsEnabled = _copiedPoseAssignment is not null };
        pasteItem.Click += (_, _) =>
        {
            if (_copiedPoseAssignment is null) return;
            character.PoseSprites[pose.Id] = CloneAssignment(_copiedPoseAssignment);
            BuildPosesPanel(character);
        };
        menu.Items.Add(pasteItem);

        if (!pose.IsBuiltIn)
        {
            menu.Items.Add(new Separator());
            var renameItem = new MenuItem { Header = "Rename..." };
            renameItem.Click += (_, _) =>
            {
                var name = SimpleInputWindow.Show(this, "Pose name:", "Rename Pose", pose.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                pose.Name = name.Trim();
                BuildPosesPanel(character);
            };
            menu.Items.Add(renameItem);

            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                var result = MessageBox.Show($"Delete \"{pose.Name}\"? Any character with a sprite linked to it will simply find nothing there anymore.",
                    "Delete Pose", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
                _poses.Poses.Remove(pose);
                BuildPosesPanel(character);
            };
            menu.Items.Add(deleteItem);
        }

        return menu;
    }

    /// <summary>
    /// The compass dial - 8 clickable direction dots around a circle, a
    /// needle pointing at whichever is currently selected, matching the
    /// sketch. SpriteDirection's own declared order (North, NorthEast,
    /// East, SouthEast, South, SouthWest, West, NorthWest) is already
    /// clockwise starting at North, so each direction's angle is simply
    /// its enum index times 45 degrees - no separate angle table needed.
    /// </summary>
    private Canvas BuildDirectionDial(PoseDefinition pose, PoseAssignment assignment, CharacterDefinition character)
    {
        const double size = 56, center = 28, dialRadius = 22, tickRadius = 20, tickSize = 8;

        var canvas = new Canvas { Width = size, Height = size };
        canvas.Children.Add(new Ellipse
        {
            Width = dialRadius * 2, Height = dialRadius * 2,
            Stroke = Brushes.Gray, StrokeThickness = 1, Fill = Brushes.White
        });
        Canvas.SetLeft(canvas.Children[0], center - dialRadius);
        Canvas.SetTop(canvas.Children[0], center - dialRadius);

        var selectedDirection = _poseSelectedDirection[pose.Id];

        (double X, double Y) PointFor(SpriteDirection d)
        {
            double angleRad = (int)d * 45.0 * Math.PI / 180.0;
            return (center + tickRadius * Math.Sin(angleRad), center - tickRadius * Math.Cos(angleRad));
        }

        var (needleX, needleY) = PointFor(selectedDirection);
        var needle = new Line { X1 = center, Y1 = center, X2 = needleX, Y2 = needleY, Stroke = Brushes.RoyalBlue, StrokeThickness = 2 };
        canvas.Children.Add(needle);

        foreach (SpriteDirection direction in Enum.GetValues<SpriteDirection>())
        {
            var (x, y) = PointFor(direction);
            bool hasSprite = assignment.Directions.ContainsKey(direction);
            bool isSelected = direction == selectedDirection;

            var tick = new Ellipse
            {
                Width = tickSize, Height = tickSize,
                Fill = isSelected ? Brushes.RoyalBlue : (hasSprite ? Brushes.MediumSeaGreen : Brushes.LightGray),
                Stroke = Brushes.DimGray, StrokeThickness = 1,
                Cursor = Cursors.Hand,
                ToolTip = $"{direction}{(hasSprite ? " (assigned)" : "")} - click to select, right-click to copy/paste"
            };
            Canvas.SetLeft(tick, x - tickSize / 2);
            Canvas.SetTop(tick, y - tickSize / 2);

            tick.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                _poseSelectedDirection[pose.Id] = direction;
                BuildPosesPanel(character);
            };
            tick.MouseRightButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                tick.ContextMenu = BuildDirectionContextMenu(direction, assignment, character);
                tick.ContextMenu.IsOpen = true;
            };

            canvas.Children.Add(tick);
        }

        return canvas;
    }

    private ContextMenu BuildDirectionContextMenu(SpriteDirection direction, PoseAssignment assignment, CharacterDefinition character)
    {
        var menu = new ContextMenu();

        var copyItem = new MenuItem { Header = $"Copy {direction}", IsEnabled = assignment.Directions.ContainsKey(direction) };
        copyItem.Click += (_, _) =>
        {
            if (assignment.Directions.TryGetValue(direction, out var link))
                _copiedDirectionLink = new PoseSpriteLink { SpriteGroup = link.SpriteGroup, SpriteName = link.SpriteName };
        };
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = $"Paste to {direction}", IsEnabled = _copiedDirectionLink is not null };
        pasteItem.Click += (_, _) =>
        {
            if (_copiedDirectionLink is null) return;
            assignment.Directions[direction] = new PoseSpriteLink { SpriteGroup = _copiedDirectionLink.SpriteGroup, SpriteName = _copiedDirectionLink.SpriteName };
            BuildPosesPanel(character);
        };
        menu.Items.Add(pasteItem);

        var clearItem = new MenuItem { Header = "Clear", IsEnabled = assignment.Directions.ContainsKey(direction) };
        clearItem.Click += (_, _) => { assignment.Directions.Remove(direction); BuildPosesPanel(character); };
        menu.Items.Add(clearItem);

        return menu;
    }

    /// <summary>
    /// One large preview square (not a filmstrip) showing the first frame
    /// of whichever sprite is assigned to the currently-selected
    /// direction. Sized to actually be readable ("proper preview size"),
    /// sitting above the direction wheel per spec. Click it to assign or
    /// change the sprite for that direction - actual frame/animation/
    /// sound editing still lives in the Sprite Editor.
    /// </summary>
    private UIElement BuildPosePreviewSquare(PoseDefinition pose, PoseAssignment assignment, SpriteDirection selectedDirection, CharacterDefinition character)
    {
        assignment.Directions.TryGetValue(selectedDirection, out var link);
        var sprite = link is not null
            ? _sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
            : null;

        var square = new Border
        {
            Width = 120, Height = 120,
            BorderBrush = Brushes.Black, BorderThickness = new Thickness(2),
            Background = Brushes.Black, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        AnimationPreviewButton? playButton = null;
        if (sprite is not null && sprite.Frames.Count > 0 && File.Exists(sprite.Frames[0].FilePath))
        {
            var picture = new Image { Stretch = Stretch.Uniform };
            square.Child = picture;
            // Still and silent until played: the pose's own mute and volume apply when it does.
            playButton = new AnimationPreviewButton(sprite, picture, _resources, assignment.Muted ? 0 : assignment.Volume / 100.0) { Margin = new Thickness(0, 4, 0, 0) };
        }
        else
        {
            // No sprite assigned - a faded camera icon on the black background instead of a blank square, so "nothing here yet" reads as an intentional empty state rather than a rendering failure.
            square.Child = new TextBlock
            {
                Text = "\U0001F4F7", FontSize = 40, Opacity = 0.25,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
        }

        square.ToolTip = sprite is not null
            ? $"{pose.Name} ({selectedDirection}): {sprite.Name} - click to change, right-click to remove"
            : $"{pose.Name} ({selectedDirection}): no sprite yet - click to assign";
        square.MouseLeftButtonDown += (_, _) => AssignDirectionSprite(pose, selectedDirection, assignment, character);
        square.MouseRightButtonDown += (_, ev) =>
        {
            ev.Handled = true;
            if (!assignment.Directions.ContainsKey(selectedDirection)) return;
            assignment.Directions.Remove(selectedDirection);
            BuildPosesPanel(character);
        };

        if (playButton is null) return square;
        var withButton = new StackPanel();
        withButton.Children.Add(square);
        withButton.Children.Add(playButton);
        return withButton;
    }

    /// <summary>Same Filter By Group + sprite combo mechanism Face already uses, scoped to one direction of one pose.</summary>
    private void AssignDirectionSprite(PoseDefinition pose, SpriteDirection direction, PoseAssignment assignment, CharacterDefinition character)
    {
        assignment.Directions.TryGetValue(direction, out var currentLink);
        string? filterGroup = string.IsNullOrEmpty(currentLink?.SpriteGroup) ? null : currentLink!.SpriteGroup;

        var window = new Window
        {
            Title = $"Assign Sprite - {pose.Name} ({direction})",
            Width = 360,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var combo = new ComboBox { DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 0, 8) };
        var filterCheck = new CheckBox { Content = "Filter By Group", IsChecked = filterGroup is not null, Margin = new Thickness(0, 0, 0, 12) };

        void PopulateChoices(string? group)
        {
            List<SpriteEntry> choices = string.IsNullOrEmpty(group)
                ? _sprites.Groups.SelectMany(g => g.Sprites).ToList()
                : _sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.ToList() ?? new List<SpriteEntry>();
            combo.ItemsSource = choices;
            combo.SelectedItem = choices.FirstOrDefault(s => s.Name == currentLink?.SpriteName);
        }
        PopulateChoices(filterGroup);

        filterCheck.Checked += (_, _) =>
        {
            var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
            string? chosen = SelectGroupWindow.Show(window, groupNames, filterGroup);
            if (chosen is null) { filterCheck.IsChecked = false; return; }
            filterGroup = chosen;
            PopulateChoices(filterGroup);
        };
        filterCheck.Unchecked += (_, _) =>
        {
            filterGroup = null;
            PopulateChoices(null);
        };

        var okButton = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(4) };
        var clearButton = new Button { Content = "Clear", Width = 70, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 70, IsCancel = true, Margin = new Thickness(4) };
        okButton.Click += (_, _) =>
        {
            if (combo.SelectedItem is SpriteEntry sprite)
            {
                string group = filterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(sprite))?.Name ?? string.Empty;
                assignment.Directions[direction] = new PoseSpriteLink { SpriteGroup = group, SpriteName = sprite.Name };
            }
            window.DialogResult = true;
        };
        clearButton.Click += (_, _) =>
        {
            assignment.Directions.Remove(direction);
            window.DialogResult = true;
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(clearButton);
        buttonRow.Children.Add(cancelButton);

        var stackContent = new StackPanel { Margin = new Thickness(12) };
        stackContent.Children.Add(new TextBlock { Text = $"Sprite for {pose.Name} ({direction}):", Margin = new Thickness(0, 0, 0, 6) });
        stackContent.Children.Add(combo);
        stackContent.Children.Add(filterCheck);
        stackContent.Children.Add(buttonRow);
        window.Content = stackContent;

        if (window.ShowDialog() == true) BuildPosesPanel(character);
    }

    private static BitmapImage? LoadStaticFrame(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>No real Class system exists yet - "(None)" plus whatever this character already had (so an old value isn't silently lost) is genuinely the whole list for now, honestly reflecting the alpha stage rather than pretending there's more here.</summary>
    private void RefreshClassChoices(string currentClassName)
    {
        var names = new List<string> { "(None)" };
        if (!string.IsNullOrEmpty(currentClassName) && currentClassName != "(None)") names.Add(currentClassName);
        ClassCombo.ItemsSource = names;
        ClassCombo.SelectedItem = currentClassName;
    }

    private void PopulateFaceChoices(string? filterGroup, string? selectedName)
    {
        List<SpriteEntry> choices;
        if (!string.IsNullOrEmpty(filterGroup))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == filterGroup);
            choices = group?.Sprites.ToList() ?? new List<SpriteEntry>();
        }
        else
        {
            choices = _sprites.Groups.SelectMany(g => g.Sprites).ToList();
        }

        FaceCombo.ItemsSource = choices;
        FaceCombo.SelectedItem = choices.FirstOrDefault(s => s.Name == selectedName);
        FaceEmptyNote.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FaceFilterCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
        string? chosen = SelectGroupWindow.Show(this, groupNames, _faceFilterGroup);
        if (chosen is null)
        {
            _suppress = true;
            FaceFilterCheck.IsChecked = false;
            _suppress = false;
            return;
        }
        _faceFilterGroup = chosen;
        PopulateFaceChoices(chosen, character.FaceSpriteName);
    }

    private void FaceFilterCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        _faceFilterGroup = null;
        PopulateFaceChoices(null, character.FaceSpriteName);
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;

        character.Name = NameBox.Text;
        int.TryParse(AgeBox.Text, out int age);
        character.Age = age;
        character.Gender = MaleRadio.IsChecked == true ? CharacterGender.Male : CharacterGender.Female;
        character.ClassName = ClassCombo.SelectedItem as string ?? "(None)";
        if (FactionCombo.SelectedItem is Faction faction) character.FactionId = faction.Id;

        bool wantsTestPlayer = TestPlayerCheck.IsChecked == true;
        if (wantsTestPlayer && !character.IsTestPlayer && !ConfirmBecomingMainPlayer(character))
        {
            _suppress = true;
            TestPlayerCheck.IsChecked = false;
            _suppress = false;
            wantsTestPlayer = false;
        }
        bool mainPlayerChanged = wantsTestPlayer != character.IsTestPlayer;
        if (wantsTestPlayer && !character.IsTestPlayer)
        {
            // Only one character can be the test player - claiming it here releases it from whoever had it.
            foreach (var other in _database.Characters) other.IsTestPlayer = false;
        }
        character.IsTestPlayer = wantsTestPlayer;
        if (mainPlayerChanged) BuildSchedulePanel(character); // the main player's schedule tab is disabled and empty
        character.IsInParty = InPartyCheck.IsChecked == true;

        if (FaceCombo.SelectedItem is SpriteEntry faceSprite)
        {
            character.FaceSpriteName = faceSprite.Name;
            character.FaceSpriteGroup = _faceFilterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(faceSprite))?.Name ?? string.Empty;
        }

        // The list shows Name, which just changed - refresh without losing the current selection.
        int index = CharacterListBox.SelectedIndex;
        CharacterListBox.Items.Refresh();
        CharacterListBox.SelectedIndex = index;
    }

    private void ShowSize(CharacterDefinition character)
    {
        bool wasSuppressed = _suppress;
        _suppress = true;
        SizeXBox.Text = character.SizeX.ToString();
        SizeYBox.Text = character.SizeY.ToString();
        SizeZBox.Text = character.SizeZ.ToString();
        _suppress = wasSuppressed;
    }

    private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (int.TryParse(SizeXBox.Text, out int x) && x > 0) character.SizeX = x;
        if (int.TryParse(SizeYBox.Text, out int y) && y > 0) character.SizeY = y;
        if (int.TryParse(SizeZBox.Text, out int z) && z > 0) character.SizeZ = z;
    }

    private void AutoSize_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        (character.SizeX, character.SizeY, character.SizeZ) = ActorSizing.ComputeAutomatic(character, _poses, _sprites);
        ShowSize(character);
    }

    private void PopulatePaperdollBodyChoices(string? filterGroup, string? selectedName)
    {
        List<SpriteEntry> choices;
        if (!string.IsNullOrEmpty(filterGroup))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == filterGroup);
            choices = group?.Sprites.ToList() ?? new List<SpriteEntry>();
        }
        else
        {
            choices = _sprites.Groups.SelectMany(g => g.Sprites).ToList();
        }
        PaperdollBodyCombo.ItemsSource = choices;
        PaperdollBodyCombo.SelectedItem = choices.FirstOrDefault(s => s.Name == selectedName);
    }

    private void PaperdollFilterCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
        string? chosen = SelectGroupWindow.Show(this, groupNames, _paperdollBodyFilterGroup);
        if (chosen is null)
        {
            _suppress = true;
            PaperdollFilterCheck.IsChecked = false;
            _suppress = false;
            return;
        }
        _paperdollBodyFilterGroup = chosen;
        PopulatePaperdollBodyChoices(chosen, character.Paperdoll.BodySpriteName);
    }

    private void PaperdollFilterCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        _paperdollBodyFilterGroup = null;
        PopulatePaperdollBodyChoices(null, character.Paperdoll.BodySpriteName);
    }

    private void PaperdollBody_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (PaperdollBodyCombo.SelectedItem is SpriteEntry bodySprite)
        {
            character.Paperdoll.BodySpriteName = bodySprite.Name;
            character.Paperdoll.BodySpriteGroup = _paperdollBodyFilterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(bodySprite))?.Name ?? string.Empty;
        }
        BuildPaperdollCanvas(character);
    }

    private void PopulateExitButtonChoices(string? filterGroup, string? selectedName)
    {
        List<SpriteEntry> choices;
        if (!string.IsNullOrEmpty(filterGroup))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == filterGroup);
            choices = group?.Sprites.ToList() ?? new List<SpriteEntry>();
        }
        else
        {
            choices = _sprites.Groups.SelectMany(g => g.Sprites).ToList();
        }
        ExitButtonCombo.ItemsSource = choices;
        ExitButtonCombo.SelectedItem = choices.FirstOrDefault(s => s.Name == selectedName);
    }

    private void ExitButtonFilterCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
        string? chosen = SelectGroupWindow.Show(this, groupNames, _exitButtonFilterGroup);
        if (chosen is null)
        {
            _suppress = true;
            ExitButtonFilterCheck.IsChecked = false;
            _suppress = false;
            return;
        }
        _exitButtonFilterGroup = chosen;
        PopulateExitButtonChoices(chosen, character.Paperdoll.ExitButtonSpriteName);
    }

    private void ExitButtonFilterCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        _exitButtonFilterGroup = null;
        PopulateExitButtonChoices(null, character.Paperdoll.ExitButtonSpriteName);
    }

    private void ExitButtonSprite_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (ExitButtonCombo.SelectedItem is SpriteEntry exitSprite)
        {
            character.Paperdoll.ExitButtonSpriteName = exitSprite.Name;
            character.Paperdoll.ExitButtonSpriteGroup = _exitButtonFilterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(exitSprite))?.Name ?? string.Empty;
        }
    }

    private static readonly ResourceEntry NoneSound = new() { Id = -1, Name = "(None)" };

    private void PaperdollOpenSound_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        character.Paperdoll.OpenSoundResourceId = (PaperdollOpenSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
    }

    private void PaperdollNavigateSound_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        character.Paperdoll.NavigateSoundResourceId = (PaperdollNavigateSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
    }

    /// <summary>
    /// Real, live paperdoll preview - the body sprite as a background
    /// image (its first frame, since the paperdoll only ever needs a
    /// static pose here, not an animated one) with one draggable marker
    /// per EquipmentSlot drawn at its own real, authored position on top.
    /// Rebuilt whole on any relevant change; cheap enough for a handful
    /// of slots and simpler than trying to patch individual markers.
    /// </summary>
    private void BuildPaperdollCanvas(CharacterDefinition character)
    {
        PaperdollCanvas.Children.Clear();

        var bodyGroup = _sprites.Groups.FirstOrDefault(g => g.Name == character.Paperdoll.BodySpriteGroup);
        var bodySprite = bodyGroup?.Sprites.FirstOrDefault(s => s.Name == character.Paperdoll.BodySpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == character.Paperdoll.BodySpriteName);
        if (bodySprite?.Frames.Count > 0)
        {
            var bmp = LoadStaticFrame(bodySprite.Frames[0].FilePath);
            if (bmp is not null)
            {
                var bodyImage = new Image { Source = bmp, Width = 320, Height = 400, Stretch = Stretch.Uniform };
                Canvas.SetLeft(bodyImage, 0);
                Canvas.SetTop(bodyImage, 0);
                Panel.SetZIndex(bodyImage, 0);
                PaperdollCanvas.Children.Add(bodyImage);
            }
        }

        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (!character.Paperdoll.SlotPositions.TryGetValue(slot, out var pos))
            {
                pos = new PaperdollSlotPosition { X = 140, Y = 10 };
                character.Paperdoll.SlotPositions[slot] = pos;
            }

            var marker = new Border
            {
                Width = 60,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(200, 70, 130, 200)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.SizeAll,
                Tag = slot,
                Child = new TextBlock
                {
                    Text = slot.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(marker, pos.X);
            Canvas.SetTop(marker, pos.Y);
            Panel.SetZIndex(marker, 10);
            PaperdollCanvas.Children.Add(marker);
        }

        // Real, confirmed new feature, per spec ("Custom slots are colored green") - same real marker shape as a built-in slot, just green, and carrying the real CustomEquipmentSlot object itself as its Tag (not just an id/name) so drag and delete can reach it directly.
        foreach (var customSlot in character.Paperdoll.CustomSlots)
        {
            var marker = new Border
            {
                Width = 60,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(200, 60, 170, 60)),
                BorderBrush = _selectedCustomSlot == customSlot ? Brushes.Yellow : Brushes.White,
                BorderThickness = new Thickness(_selectedCustomSlot == customSlot ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.SizeAll,
                Tag = customSlot,
                Child = new TextBlock
                {
                    Text = customSlot.Name,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(marker, customSlot.X);
            Canvas.SetTop(marker, customSlot.Y);
            Panel.SetZIndex(marker, 10);
            PaperdollCanvas.Children.Add(marker);
        }
    }

    private void PaperdollCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (e.OriginalSource is not DependencyObject src) return;

        // Walk up from whatever was actually clicked (the TextBlock inside the Border, typically) to find the marker Border itself and its slot Tag - either kind, built-in or custom.
        DependencyObject? node = src;
        while (node is not null && (node is not Border b || b.Tag is not (EquipmentSlot or CustomEquipmentSlot)))
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is not Border marker || marker.Tag is not (EquipmentSlot or CustomEquipmentSlot)) return;

        _draggingSlot = marker.Tag;
        _dragStartMouse = e.GetPosition(PaperdollCanvas);
        _dragStartSlotPos = marker.Tag switch
        {
            EquipmentSlot builtin => new Point(character.Paperdoll.SlotPositions[builtin].X, character.Paperdoll.SlotPositions[builtin].Y),
            CustomEquipmentSlot custom => new Point(custom.X, custom.Y),
            _ => default
        };
        PaperdollCanvas.CaptureMouse();
        PaperdollCanvas.Focus(); // real, confirmed need - Delete has to actually reach this canvas's own KeyDown handler, which only happens once it genuinely has keyboard focus

        // Real, confirmed new selection state, per spec - a real click on a custom slot selects it for rename/delete, shown via the real yellow highlight border and the real rename box; clicking a built-in slot (or empty canvas) clears it, since neither applies to those.
        _selectedCustomSlot = marker.Tag as CustomEquipmentSlot;
        UpdateCustomSlotNameBox();
        BuildPaperdollCanvas(character);
    }

    private void PaperdollCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSlot is null || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        var current = e.GetPosition(PaperdollCanvas);
        double newX = Math.Clamp(_dragStartSlotPos.X + (current.X - _dragStartMouse.X), 0, PaperdollCanvas.Width - 60);
        double newY = Math.Clamp(_dragStartSlotPos.Y + (current.Y - _dragStartMouse.Y), 0, PaperdollCanvas.Height - 22);

        switch (_draggingSlot)
        {
            case EquipmentSlot builtin:
                var pos = character.Paperdoll.SlotPositions[builtin];
                pos.X = newX; pos.Y = newY;
                break;
            case CustomEquipmentSlot custom:
                custom.X = newX; custom.Y = newY;
                break;
        }

        foreach (var child in PaperdollCanvas.Children)
        {
            if (child is Border b && Equals(b.Tag, _draggingSlot))
            {
                Canvas.SetLeft(b, newX);
                Canvas.SetTop(b, newY);
                break;
            }
        }
    }

    private void PaperdollCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSlot = null;
        PaperdollCanvas.ReleaseMouseCapture();
    }

    /// <summary>Real, confirmed new feature, per spec ("the ability to delete custom slots by selecting one and pressing Delete") - only ever removes a real, currently-selected CUSTOM slot; a built-in slot can never be deleted at all, since the fixed list itself is untouched by this feature.</summary>
    private void PaperdollCanvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Delete) return;
        if (_selectedCustomSlot is not { } slot || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        character.Paperdoll.CustomSlots.Remove(slot);
        _selectedCustomSlot = null;
        UpdateCustomSlotNameBox();
        BuildPaperdollCanvas(character);
        e.Handled = true;
    }

    private void UpdateCustomSlotNameBox()
    {
        bool hasSelection = _selectedCustomSlot is not null;
        CustomSlotNameLabel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CustomSlotNameBox.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        _suppress = true;
        CustomSlotNameBox.Text = _selectedCustomSlot?.Name ?? string.Empty;
        _suppress = false;
    }

    private void CustomSlotNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selectedCustomSlot is not { } slot || CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        slot.Name = CustomSlotNameBox.Text;
        // Real, live rename - the marker's own label updates immediately as the designer types, same principle as every other live-bound field in this editor.
        foreach (var child in PaperdollCanvas.Children)
        {
            if (child is Border b && ReferenceEquals(b.Tag, slot) && b.Child is TextBlock tb) { tb.Text = slot.Name; break; }
        }
    }

    private void AddCustomSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        var newSlot = new CustomEquipmentSlot { Id = character.Paperdoll.GetNextCustomSlotId(), Name = $"Custom Slot {character.Paperdoll.CustomSlots.Count + 1}", X = 140, Y = 10 };
        character.Paperdoll.CustomSlots.Add(newSlot);
        _selectedCustomSlot = newSlot;
        UpdateCustomSlotNameBox();
        BuildPaperdollCanvas(character);
    }

    /// <summary>One line of the Starting Inventory list: an item, where it is (a slot, or inside a container), and what holds it.</summary>
    private sealed record InventoryRow(InventoryItemInstance Instance, IHasContainerContents? HeldIn, string DisplayText);

    private ContainerDatabase Containers => ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase();

    /// <summary>Exult only weighs the party: 2 x Strength for the main character or a party member, no limit for anyone else.</summary>
    private double? MaxCarryOf(CharacterDefinition character)
    {
        if (!character.IsTestPlayer && !character.IsInParty) return null;
        var strength = _stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Strength);
        int str = strength is not null && character.StatValues.TryGetValue(strength.Id, out var text) && int.TryParse(text, out int v) ? v : 10;
        return ItemWeights.MaxCarry(str);
    }

    private void RefreshInventoryList(CharacterDefinition character)
    {
        var containers = Containers;
        string Name(InventoryItemInstance i)
        {
            string name = StartingInventoryRules.NameOf(i.Category, i.ItemId, containers, _books, _objects);
            return i.Quantity > 1 ? $"{name} x{i.Quantity}" : name;
        }
        var rows = new List<InventoryRow>();
        foreach (var item in character.Inventory)
        {
            string where = StartingInventoryRules.SlotNameOf(character, item) is { } slot ? $"[{slot}]" : "[not in a slot]";
            rows.Add(new InventoryRow(item, null, $"{where} {Name(item)}"));
            foreach (var inside in item.Contents) rows.Add(new InventoryRow(inside, item, $"        in {Name(item)}: {Name(inside)}"));
        }
        InventoryListBox.ItemsSource = rows;
        double carried = ItemWeights.Carried(character, containers, _books, _objects);
        InventoryWeightText.Text = MaxCarryOf(character) is { } max
            ? $"Carrying {carried:0.##} of {max:0.##} (2 x Strength)."
            : $"Carrying {carried:0.##} (only the main character and party members have a carry limit, as in Exult).";
    }

    private void PopulateInventoryCategoryChoices()
    {
        InventoryCategoryCombo.ItemsSource = new[] { ReferencedItemCategory.Book, ReferencedItemCategory.Object, ReferencedItemCategory.Container };
        InventoryCategoryCombo.SelectionChanged -= InventoryCategoryCombo_SelectionChanged;
        InventoryCategoryCombo.SelectionChanged += InventoryCategoryCombo_SelectionChanged;
        InventoryCategoryCombo.SelectedIndex = 0;
    }

    private void InventoryCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InventoryCategoryCombo.SelectedItem is not ReferencedItemCategory category) return;
        InventoryItemCombo.ItemsSource = category switch
        {
            ReferencedItemCategory.Book => _books.Books.Cast<object>().ToList(),
            ReferencedItemCategory.Object => _objects.Objects.Cast<object>().ToList(),
            ReferencedItemCategory.Container => Containers.Containers.Where(c => !c.IsBodyContainer).Cast<object>().ToList(), // a body isn't something you carry
            _ => new List<object>()
        };
    }

    /// <summary>Added by the same rules as picking it up in play (StartingInventoryRules): containers to their slot or a hand, everything else into the Backpack, then Pouch container while there's room, then the hands - or refused.</summary>
    private void AddInventoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (InventoryCategoryCombo.SelectedItem is not ReferencedItemCategory category) return;
        int itemId = InventoryItemCombo.SelectedItem switch
        {
            BookData book => book.Id,
            WorldObject obj => obj.Id,
            ContainerData container => container.Id,
            _ => -1
        };
        if (itemId < 0)
        {
            MessageBox.Show("Pick an item first.", "Character Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (StartingInventoryRules.TryAdd(character, category, itemId, Containers, _books, _objects, MaxCarryOf(character)) is { } refusal)
        {
            MessageBox.Show(refusal, "Character Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RefreshInventoryList(character);
    }

    private void RemoveInventoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterListBox.SelectedItem is not CharacterDefinition character) return;
        if (InventoryListBox.SelectedItem is not InventoryRow row) return;
        StartingInventoryRules.Remove(character, row.Instance, row.HeldIn); // a container goes with everything in it
        RefreshInventoryList(character);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Stat boxes save when they lose focus; closing with one still focused saves it too.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        AnimationPreviewButton.StopAll();
        if (Keyboard.FocusedElement is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        base.OnClosing(e);
    }
}
