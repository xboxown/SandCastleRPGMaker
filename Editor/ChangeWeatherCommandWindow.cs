using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Change Weather: rain, snow, hail, a storm or clear skies from now on (Exult's weather eggs), and the clouds.</summary>
public class ChangeWeatherCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _kind, _clouds;
    private readonly Slider _intensity;

    public ChangeWeatherCommandWindow(EventCommand? existing = null) : base("Change Weather")
    {
        AddLabel("Weather");
        _kind = new ComboBox { ItemsSource = Enum.GetValues<WeatherKind>() };
        _kind.SelectedItem = Enum.TryParse<WeatherKind>(existing?.Get("Kind"), out var k) ? k : WeatherKind.Rain;
        Rows.Children.Add(_kind);
        AddLabel("How heavy");
        _intensity = new Slider { Minimum = 1, Maximum = 100, Value = int.TryParse(existing?.Get("Intensity"), out int i) ? i : 50 };
        Rows.Children.Add(_intensity);
        AddLabel("Clouds");
        _clouds = new ComboBox { ItemsSource = new[] { "As they are", "Clouds", "No clouds" } };
        _clouds.SelectedIndex = existing?.Get("Clouds") switch { "True" => 1, "False" => 2, _ => 0 };
        Rows.Children.Add(_clouds);
        AddLabel("From now on, on this map (it may still come and go by itself, if the map's weather is set to).", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.ChangeWeather };
        command.Parameters["Kind"] = ((WeatherKind)_kind.SelectedItem).ToString();
        command.Parameters["Intensity"] = ((int)Math.Round(_intensity.Value)).ToString();
        command.Parameters["Clouds"] = _clouds.SelectedIndex switch { 1 => "True", 2 => "False", _ => "" };
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) =>
        $"{command.Get("Kind")} ({command.Get("Intensity")}%){(command.Get("Clouds") == "True" ? ", clouds" : command.Get("Clouds") == "False" ? ", no clouds" : "")}";
}
