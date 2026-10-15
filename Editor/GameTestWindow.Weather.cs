using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Weather, as Exult's weather eggs make it (Map Editor > Map Weather, and
/// the Change Weather event command): rain falling slanted by the wind,
/// snow drifting, hail, and storms - heavy rain, lightning flashing the
/// screen white, thunder after it. Not drawn while the main character is
/// under a roof (Exult hides it indoors). Cloud shadows drift over the land
/// with the wind. It can come and go by itself, hour by hour. Like
/// everything in the world, it stops while the game is paused.
/// </summary>
public partial class GameTestWindow
{
    private sealed class Particle
    {
        public required Shape Shape;
        public double X, Y, VX, VY, Phase;
    }

    private sealed class Cloud
    {
        public required Shape Shape;
        /// <summary>Where it is over the map (world pixels).</summary>
        public double X, Y;
    }

    private WeatherSettings _weather = new();
    private readonly List<Particle> _particles = new();
    private readonly List<Cloud> _clouds = new();
    private double _lightningMs = -1;
    private int _weatherHour = -1;
    private readonly object _weatherSoundOwner = new();
    private const double ViewWidth = 320, ViewHeight = 200;

    /// <summary>At the start of play: the map's own weather.</summary>
    private void StartWeather()
    {
        var w = _map.Weather;
        _weather = new WeatherSettings
        {
            Kind = w.Kind, Intensity = w.Intensity, Clouds = w.Clouds, CloudCover = w.CloudCover, Wind = w.Wind, Changes = w.Changes,
            ChangeChancePercent = w.ChangeChancePercent, RainSoundResourceId = w.RainSoundResourceId, ThunderSoundResourceId = w.ThunderSoundResourceId
        };
        _weatherHour = _clock.Hour;
        BuildWeather();
    }

    /// <summary>The Change Weather command (and the weather changing by itself).</summary>
    private bool ChangeWeather(WeatherKind kind, int intensity, bool? clouds)
    {
        _weather.Kind = kind;
        _weather.Intensity = Math.Clamp(intensity, 1, 100);
        if (clouds is { } c) _weather.Clouds = c;
        BuildWeather();
        _lightCheckMs = 0; // the sky darkens or brightens with it
        return true;
    }

    private double WindX => _weather.Wind switch
    {
        SpriteDirection.East or SpriteDirection.NorthEast or SpriteDirection.SouthEast => 1,
        SpriteDirection.West or SpriteDirection.NorthWest or SpriteDirection.SouthWest => -1,
        _ => 0
    };
    private double WindY => _weather.Wind switch
    {
        SpriteDirection.South or SpriteDirection.SouthEast or SpriteDirection.SouthWest => 1,
        SpriteDirection.North or SpriteDirection.NorthEast or SpriteDirection.NorthWest => -1,
        _ => 0
    };

    /// <summary>Makes the drops and clouds for the weather now (and its rain sound).</summary>
    private void BuildWeather()
    {
        WeatherLayer.Children.Clear();
        CloudLayer.Children.Clear();
        _particles.Clear();
        _clouds.Clear();
        SoundPlayback.StopFor(_weatherSoundOwner);
        double heavy = Math.Clamp(_weather.Intensity, 1, 100) / 100.0;
        int count = _weather.Kind switch
        {
            WeatherKind.Rain => (int)(30 + 140 * heavy),
            WeatherKind.Storm => (int)(80 + 200 * heavy),
            WeatherKind.Snow => (int)(25 + 110 * heavy),
            WeatherKind.Hail => (int)(20 + 90 * heavy),
            _ => 0
        };
        for (int i = 0; i < count; i++)
        {
            var particle = NewParticle();
            particle.X = Rng.NextDouble() * ViewWidth;
            particle.Y = Rng.NextDouble() * ViewHeight;
            WeatherLayer.Children.Add(particle.Shape);
            _particles.Add(particle);
            PlaceParticle(particle);
        }
        if (_weather.Kind is WeatherKind.Rain or WeatherKind.Storm && _weather.RainSoundResourceId >= 0)
            SoundPlayback.TryPlayFor(_weatherSoundOwner, _resources, _weather.RainSoundResourceId, loop: true);
        _lightningMs = _weather.Kind == WeatherKind.Storm ? 3000 + Rng.Next(6000) : -1;

        int cloudCount = _weather.Clouds ? Math.Clamp(_weather.CloudCover / 8, 1, 14) : 0;
        if (_weather.Kind is WeatherKind.Storm or WeatherKind.Rain) cloudCount += 3;
        for (int i = 0; i < cloudCount; i++)
        {
            double w = 90 + Rng.Next(110), h = 50 + Rng.Next(60);
            var brush = new RadialGradientBrush(Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
            var shape = new Ellipse { Width = w, Height = h, Fill = brush, IsHitTestVisible = false };
            var cloud = new Cloud { Shape = shape, X = CameraLeft + Rng.NextDouble() * (ViewWidth + 200) - 100, Y = CameraTop + Rng.NextDouble() * (ViewHeight + 160) - 80 };
            CloudLayer.Children.Add(shape);
            _clouds.Add(cloud);
        }
        PlaceClouds();
    }

    private double CameraLeft => -CameraTransform.X;
    private double CameraTop => -CameraTransform.Y;

    private Particle NewParticle()
    {
        double wind = WindX;
        switch (_weather.Kind)
        {
            case WeatherKind.Snow:
                return new Particle { Shape = new Ellipse { Width = 2, Height = 2, Fill = Brushes.White, Opacity = 0.9, IsHitTestVisible = false }, VX = wind * 12, VY = 22 + Rng.NextDouble() * 14, Phase = Rng.NextDouble() * 6.28 };
            case WeatherKind.Hail:
                return new Particle { Shape = new Rectangle { Width = 2, Height = 2, Fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFF)), IsHitTestVisible = false }, VX = wind * 30, VY = 170 + Rng.NextDouble() * 40 };
            default: // rain, storm
            {
                double fall = _weather.Kind == WeatherKind.Storm ? 260 : 200;
                var line = new Line { Stroke = new SolidColorBrush(Color.FromArgb(170, 0xA8, 0xB8, 0xD8)), StrokeThickness = 1, IsHitTestVisible = false };
                return new Particle { Shape = line, VX = wind * (_weather.Kind == WeatherKind.Storm ? 110 : 60), VY = fall + Rng.NextDouble() * 60 };
            }
        }
    }

    private static void PlaceParticle(Particle p)
    {
        if (p.Shape is Line line)
        {
            double len = 0.025;
            line.X1 = p.X; line.Y1 = p.Y;
            line.X2 = p.X + p.VX * len; line.Y2 = p.Y + p.VY * len;
            return;
        }
        Canvas.SetLeft(p.Shape, p.X);
        Canvas.SetTop(p.Shape, p.Y);
    }

    private void PlaceClouds()
    {
        foreach (var cloud in _clouds)
        {
            Canvas.SetLeft(cloud.Shape, cloud.X - CameraLeft);
            Canvas.SetTop(cloud.Shape, cloud.Y - CameraTop);
        }
    }

    /// <summary>Each frame: drops fall, clouds drift, lightning strikes - and, hour by hour, the weather may change.</summary>
    private void UpdateWeather(double elapsedMs)
    {
        if (_weather.Changes && _clock.Hour != _weatherHour)
        {
            _weatherHour = _clock.Hour;
            if (Rng.Next(100) < Math.Clamp(_weather.ChangeChancePercent, 0, 100))
            {
                // It turns: clear, or the map's own weather (at a new strength).
                var kind = _weather.Kind == WeatherKind.Clear ? (_map.Weather.Kind == WeatherKind.Clear ? WeatherKind.Rain : _map.Weather.Kind) : WeatherKind.Clear;
                ChangeWeather(kind, 20 + Rng.Next(81), null);
            }
        }
        double seconds = elapsedMs / 1000.0;
        bool indoors = RoofLiftOverPlayer() is not null;
        WeatherLayer.Visibility = indoors ? Visibility.Hidden : Visibility.Visible;
        CloudLayer.Visibility = indoors ? Visibility.Hidden : Visibility.Visible;
        if (!indoors)
        {
            foreach (var p in _particles)
            {
                p.Phase += seconds * 2;
                p.X += (p.VX + (_weather.Kind == WeatherKind.Snow ? Math.Sin(p.Phase) * 10 : 0)) * seconds;
                p.Y += p.VY * seconds;
                if (p.Y > ViewHeight) { p.Y -= ViewHeight + 6; p.X = Rng.NextDouble() * ViewWidth; }
                if (p.X > ViewWidth) p.X -= ViewWidth; else if (p.X < 0) p.X += ViewWidth;
                PlaceParticle(p);
            }
        }
        // Clouds drift with the wind; one gone off the downwind side comes back on the other.
        double driftX = (WindX == 0 && WindY == 0 ? 1 : WindX) * 8 * seconds, driftY = WindY * 8 * seconds;
        foreach (var cloud in _clouds)
        {
            cloud.X += driftX;
            cloud.Y += driftY;
            double w = cloud.Shape.Width, h = cloud.Shape.Height;
            if (cloud.X > CameraLeft + ViewWidth + 120) cloud.X = CameraLeft - w - Rng.Next(80);
            else if (cloud.X < CameraLeft - w - 120) cloud.X = CameraLeft + ViewWidth + Rng.Next(80);
            if (cloud.Y > CameraTop + ViewHeight + 100) cloud.Y = CameraTop - h - Rng.Next(60);
            else if (cloud.Y < CameraTop - h - 100) cloud.Y = CameraTop + ViewHeight + Rng.Next(60);
        }
        PlaceClouds();

        if (_weather.Kind == WeatherKind.Storm && _lightningMs >= 0 && (_lightningMs -= elapsedMs) <= 0)
        {
            _lightningMs = 4000 + Rng.Next(9000) * (1.5 - _weather.Intensity / 100.0);
            if (!indoors)
            {
                var flash = new DoubleAnimationUsingKeyFrames();
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
                FlashOverlay.BeginAnimation(UIElement.OpacityProperty, flash);
            }
            if (_weather.ThunderSoundResourceId >= 0)
            {
                int thunder = _weather.ThunderSoundResourceId;
                var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300 + Rng.Next(1500)) };
                delay.Tick += (_, _) => { delay.Stop(); PlaySound(thunder); };
                delay.Start();
            }
        }
    }
}
