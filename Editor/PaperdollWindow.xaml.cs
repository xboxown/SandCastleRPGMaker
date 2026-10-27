using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Real, in-game paperdoll and inventory window, per Exult's own
/// confirmed access model: opened with I (current player) or a number
/// key 1-9 (a specific party member) from Game Test, and switchable
/// between party members from inside the window too via the same number
/// keys or the dropdown. Equipping here is click-to-select-then-click-
/// a-slot rather than native drag-and-drop - a deliberately simpler,
/// equally functional interaction chosen to avoid the added risk of a
/// full WPF DragDrop implementation this late in an already very large
/// session; it can become real drag-and-drop later without changing the
/// underlying data model at all.
/// </summary>
public partial class PaperdollWindow : Window
{
    private class BackpackRow
    {
        public InventoryItemInstance Instance { get; }
        private readonly BookDatabase _books;
        private readonly ObjectDatabase _objects;
        public BackpackRow(InventoryItemInstance instance, BookDatabase books, ObjectDatabase objects)
        {
            Instance = instance;
            _books = books;
            _objects = objects;
        }
        public string DisplayText
        {
            get
            {
                string name = Instance.Category switch
                {
                    ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == Instance.ItemId)?.Name ?? "(missing book)",
                    ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == Instance.ItemId)?.Name ?? "(missing object)",
                    _ => $"(no {Instance.Category} database yet)"
                };
                return Instance.Quantity > 1 ? $"{name} x{Instance.Quantity}" : name;
            }
        }
    }

    private readonly CharacterDatabase _allCharacters;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private CharacterDefinition _current;
    private int? _selectedInstanceId;

    public PaperdollWindow(CharacterDatabase allCharacters, CharacterDefinition initialCharacter, BookDatabase books, ObjectDatabase objects, SpriteDatabase sprites)
    {
        InitializeComponent();
        _allCharacters = allCharacters;
        _books = books;
        _objects = objects;
        _sprites = sprites;
        _current = initialCharacter;

        RefreshPartyMemberChoices();
        RefreshAll();

        PreviewKeyDown += PaperdollWindow_PreviewKeyDown;
    }

    /// <summary>Real Exult number-key access, per spec - 1-9 select party members by their order in the active party (matching Exult's own real "1: Avatar, 2-8: party member" keys), functional from inside this window too, not just from Game Test before it opened.</summary>
    private void PaperdollWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int? index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3, Key.D5 or Key.NumPad5 => 4, Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6, Key.D8 or Key.NumPad8 => 7, Key.D9 or Key.NumPad9 => 8,
            _ => null
        };
        if (index is null) return;
        var party = GetPartyList();
        if (index.Value >= party.Count) return;
        e.Handled = true;
        PartyMemberCombo.SelectedItem = party[index.Value];
    }

    /// <summary>The active party, per spec: the current test player is always included (matching Exult's own Avatar-always-in-party-slot-1 behavior) even if IsInParty wasn't separately checked, plus everyone else explicitly marked IsInParty.</summary>
    private List<CharacterDefinition> GetPartyList()
    {
        var list = new List<CharacterDefinition>();
        var testPlayer = _allCharacters.Characters.FirstOrDefault(c => c.IsTestPlayer);
        if (testPlayer is not null) list.Add(testPlayer);
        list.AddRange(_allCharacters.Characters.Where(c => c.IsInParty && c != testPlayer));
        return list;
    }

    private void RefreshPartyMemberChoices()
    {
        var party = GetPartyList();
        PartyMemberCombo.ItemsSource = party;
        PartyMemberCombo.SelectedItem = party.Contains(_current) ? _current : party.FirstOrDefault();
    }

    private void PartyMemberCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PartyMemberCombo.SelectedItem is not CharacterDefinition character) return;
        _current = character;
        _selectedInstanceId = null;
        RefreshAll();
    }

    private void RefreshAll()
    {
        Title = $"Inventory - {_current.Name}";
        BuildPaperdollCanvas();
        RefreshBackpackList();
        UpdateSelectedItemHint();
    }

    private void RefreshBackpackList()
    {
        var equippedIds = _current.EquippedSlots.Values.ToHashSet();
        BackpackListBox.ItemsSource = _current.Inventory
            .Where(i => !equippedIds.Contains(i.InstanceId))
            .Select(i => new BackpackRow(i, _books, _objects))
            .ToList();
    }

    private void UpdateSelectedItemHint()
    {
        if (_selectedInstanceId is not { } id) { SelectedItemHint.Text = ""; return; }
        var item = _current.Inventory.FirstOrDefault(i => i.InstanceId == id);
        var row = item is null ? null : new BackpackRow(item, _books, _objects);
        SelectedItemHint.Text = row is null ? "" : $"Selected: {row.DisplayText} - click a slot to equip it there.";
    }

    private void BackpackListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedInstanceId = (BackpackListBox.SelectedItem as BackpackRow)?.Instance.InstanceId;
        UpdateSelectedItemHint();
    }

    /// <summary>A picture file decoded once and shared (FrameCache) - not a fresh copy for every thing that shows it.</summary>
    private static BitmapImage? LoadStaticFrame(string path) => FrameCache.Get(path);

    /// <summary>Real body + real, authored per-slot positions (from the PC Editor's own paperdoll tab) - the exact same PaperdollDefinition data, so a slot moved there shows up moved here too, automatically.</summary>
    /// <summary>Resolves IsTwoHanded from whichever real database the item's Category points at - same lookup pattern as DisplayText, never guessed.</summary>
    private bool IsItemTwoHanded(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.IsTwoHanded ?? false,
        ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.IsTwoHanded ?? false,
        _ => false
    };

    /// <summary>True while RightHand holds a real two-handed item, per Exult's own confirmed mechanics - no separate "both hands" slot, this is what actually makes LeftHand unavailable instead.</summary>
    private bool IsLeftHandLocked()
    {
        if (!_current.EquippedSlots.TryGetValue(EquipmentSlot.RightHand, out int rightHandInstanceId)) return false;
        var rightHandItem = _current.Inventory.FirstOrDefault(i => i.InstanceId == rightHandInstanceId);
        return rightHandItem is not null && IsItemTwoHanded(rightHandItem);
    }

    private void BuildPaperdollCanvas()
    {
        PaperdollDisplayCanvas.Children.Clear();

        var bodyGroup = _sprites.Groups.FirstOrDefault(g => g.Name == _current.Paperdoll.BodySpriteGroup);
        var bodySprite = bodyGroup?.Sprites.FirstOrDefault(s => s.Name == _current.Paperdoll.BodySpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == _current.Paperdoll.BodySpriteName);
        if (bodySprite?.Frames.Count > 0)
        {
            var bmp = LoadStaticFrame(bodySprite.Frames[0].FilePath);
            if (bmp is not null)
            {
                var bodyImage = new Image { Source = bmp, Width = 320, Height = 400, Stretch = Stretch.Uniform };
                Canvas.SetLeft(bodyImage, 0);
                Canvas.SetTop(bodyImage, 0);
                Panel.SetZIndex(bodyImage, 0);
                PaperdollDisplayCanvas.Children.Add(bodyImage);
            }
        }

        bool leftHandLocked = IsLeftHandLocked();
        foreach (var slot in Enum.GetValues<EquipmentSlot>().Where(_current.Paperdoll.IsSlotShown))
        {
            if (!_current.Paperdoll.SlotPositions.TryGetValue(slot, out var pos))
                pos = new PaperdollSlotPosition { X = 140, Y = 10 };

            bool isLockedLeftHand = slot == EquipmentSlot.LeftHand && leftHandLocked;
            _current.EquippedSlots.TryGetValue(slot, out int equippedInstanceId);
            var equippedItem = equippedInstanceId > 0 ? _current.Inventory.FirstOrDefault(i => i.InstanceId == equippedInstanceId) : null;
            string label = isLockedLeftHand ? "(two-handed)" : slot.ToString();
            if (equippedItem is not null)
                label = new BackpackRow(equippedItem, _books, _objects).DisplayText;

            var marker = new Border
            {
                Width = 90,
                Height = 22,
                // Real two-handed lock, per Exult's own confirmed mechanics: LeftHand renders visibly disabled (dim grey, no hand cursor) whenever RightHand holds a two-handed item - not a separate "both hands" slot, this genuinely takes over the real LeftHand slot instead.
                Background = new SolidColorBrush(isLockedLeftHand ? Color.FromArgb(120, 90, 90, 90) : equippedItem is not null ? Color.FromArgb(220, 60, 160, 70) : Color.FromArgb(160, 70, 130, 200)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = isLockedLeftHand ? Cursors.No : Cursors.Hand,
                Tag = slot,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = isLockedLeftHand ? Brushes.LightGray : Brushes.White,
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            marker.MouseLeftButtonDown += (_, _) => SlotClicked(slot);
            Canvas.SetLeft(marker, pos.X);
            Canvas.SetTop(marker, pos.Y);
            Panel.SetZIndex(marker, 10);
            PaperdollDisplayCanvas.Children.Add(marker);
        }
    }

    private void SlotClicked(EquipmentSlot slot)
    {
        // Real two-handed lock, per Exult's own confirmed mechanics - LeftHand is entirely unavailable while RightHand holds a two-handed item, matching the real forum-documented behavior (a two-handed weapon or a ranged weapon needing a free hand to reload blocks the shield/off-hand slot outright).
        if (slot == EquipmentSlot.LeftHand && IsLeftHandLocked())
        {
            SelectedItemHint.Text = "Left hand is unavailable - the right hand is holding a two-handed weapon.";
            return;
        }

        bool alreadyEquipped = _current.EquippedSlots.ContainsKey(slot);
        if (alreadyEquipped)
        {
            // Unequip - back to the backpack, no confirmation needed, matching a plain click-to-toggle interaction.
            _current.EquippedSlots.Remove(slot);
        }
        else if (_selectedInstanceId is { } id && _current.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } selectedItem)
        {
            // Real requirement, confirmed from Exult's own mechanics: equipping a two-handed item into RightHand needs LeftHand genuinely empty first - the player must unequip it themselves, this never auto-unequips it for them.
            if (slot == EquipmentSlot.RightHand && IsItemTwoHanded(selectedItem) && _current.EquippedSlots.ContainsKey(EquipmentSlot.LeftHand))
            {
                SelectedItemHint.Text = "This needs both hands - unequip the left hand first.";
                return;
            }
            _current.EquippedSlots[slot] = id;
            _selectedInstanceId = null;
        }
        RefreshAll();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
