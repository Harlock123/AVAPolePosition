using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPolePosition.Engine;
using AVAPolePosition.Views;

namespace AVAPolePosition.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set POLE_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapoleposition-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("POLE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPolePosition-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void DriveUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 300)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("APX", 98250, 3);
        g.HighScores.Insert("AVA", 52000, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Tick(default);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void A_race_meeting()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        g.Idle(150);
        c.Advance(0.5);
        Shot(w, c, "qualify-lights");
        DriveUntil(g, c, g => g.Phase == Phase.Qualifying && g.Total > g.Track.Length * 0.2 && Math.Abs(g.Curve) > 2);
        Shot(w, c, "bend");
        DriveUntil(g, c, g => g.Phase == Phase.QualifyResult && g.PhaseTime > 0.5);
        Shot(w, c, "qualified");
        // From the back of the grid, for company.
        g.Racing(8);
        g.Idle(1);
        c.Advance(0.1);
        Shot(w, c, "grid");
        DriveUntil(g, c, g => g.Phase == Phase.Racing && g.Cars.Count(car => g.Gap(car) is > 400 and < 9000) >= 3 && g.Speed > 150);
        Shot(w, c, "traffic");
        DriveUntil(g, c, g => g.Phase == Phase.Racing && g.Track.At(g.Z).Sprites.Any(s => s.Kind == SpriteKind.Billboard) && false || g.Phase == Phase.Racing && g.Laps >= 1 && Math.Abs(g.Curve) > 2.5);
        Shot(w, c, "racing-bend");
        // A crash, on purpose: steer off into the boards.
        for (int i = 0; i < 60 * 20 && g.Crashed <= 0; i++) g.Run(1, new Engine.Controls(-1, Gas: true));
        g.Idle(20);
        c.Advance(0.3);
        Shot(w, c, "crash");
        DriveUntil(g, c, g => g.Phase == Phase.Finished && g.PhaseTime > 0.5);
        Shot(w, c, "goal");
    }
}
