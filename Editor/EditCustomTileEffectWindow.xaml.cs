using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class EditCustomTileEffectWindow : Window
{
    public CustomTileEffect Result { get; private set; } = new();

    private readonly List<WorldObject> _availableObjects;

    public EditCustomTileEffectWindow(List<WorldObject> availableObjects, CustomTileEffect? existing = null)
    {
        InitializeComponent();
        _availableObjects = availableObjects;

        TriggerCombo.ItemsSource = Enum.GetValues<TileEffectTrigger>();
        MitigatingObjectsListBox.ItemsSource = _availableObjects;

        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            TriggerCombo.SelectedItem = existing.Trigger;
            TickIntervalBox.Text = existing.TickIntervalSeconds.ToString();
            DamagePerTickBox.Text = existing.DamagePerTick.ToString();
            RampingCheck.IsChecked = existing.RampingDamage;
            RampIncreaseBox.Text = existing.RampIncreasePerTick.ToString();
            RecoverySecondsBox.Text = existing.RecoverySeconds.ToString();
            NotesBox.Text = existing.Notes;

            foreach (var obj in _availableObjects.Where(o => existing.MitigatingObjectIds.Contains(o.Id)))
                MitigatingObjectsListBox.SelectedItems.Add(obj);
        }
        else
        {
            TriggerCombo.SelectedIndex = 0;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        double.TryParse(TickIntervalBox.Text, out double tick);
        double.TryParse(DamagePerTickBox.Text, out double dmg);
        double.TryParse(RampIncreaseBox.Text, out double ramp);
        double.TryParse(RecoverySecondsBox.Text, out double recovery);

        Result = new CustomTileEffect
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "New Effect" : NameBox.Text,
            Trigger = TriggerCombo.SelectedItem is TileEffectTrigger t ? t : TileEffectTrigger.OnStay,
            TickIntervalSeconds = tick,
            DamagePerTick = dmg,
            RampingDamage = RampingCheck.IsChecked == true,
            RampIncreasePerTick = ramp,
            MitigatingObjectIds = MitigatingObjectsListBox.SelectedItems.Cast<WorldObject>().Select(o => o.Id).ToList(),
            RecoverySeconds = recovery,
            Notes = NotesBox.Text
        };
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
