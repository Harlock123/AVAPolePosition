using AVAPolePosition.Engine;

namespace AVAPolePosition.Tests;

/// <summary>How the computer driver gets on (set POLE_PROBE to run it, for tuning).</summary>
public sealed class DriverProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_drives()
    {
        if (Environment.GetEnvironmentVariable("POLE_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "pole-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            var crashes = new Dictionary<string, int>();
            var log = new List<string>();
            var last = g.Phase;
            double lastCrash = 0;
            int t = 0;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Crashed > 1.95 && g.Crashed > lastCrash) crashes[g.Cause ?? "?"] = crashes.GetValueOrDefault(g.Cause ?? "?") + 1;
                lastCrash = g.Crashed;
                if (g.Phase != last)
                {
                    if (g.Phase == Phase.QualifyResult) log.Add($"{g.Track.Name[..4]} Q {g.QualifyTime:0.0}s P{g.Grid}");
                    if (g.Phase == Phase.Finished) log.Add($"finished P{g.Position} left {g.TimeLeft:0}s");
                    if (g.Phase == Phase.Over) log.Add($"OVER lap {g.Laps} total {g.Total / g.Track.Length:0.00}");
                }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: {t / 60}s score {g.Score} races {g.Races} [{string.Join(", ", log)}] crashes: {string.Join(", ", crashes.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
