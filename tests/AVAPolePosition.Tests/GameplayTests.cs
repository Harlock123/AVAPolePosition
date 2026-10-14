using AVAPolePosition.Engine;
using Engine = AVAPolePosition.Engine;

namespace AVAPolePosition.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapoleposition-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Qualifying under way, the lights skipped, and no other cars.</summary>
    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.Go();
        return g;
    }

    /// <summary>A plain oval: long straights and two gentle bends.</summary>
    private static Track Oval(double bend = 2)
    {
        var t = new Track { Name = "TEST", QualifyLimit = 120, LapExtend = 60 };
        for (int i = 0; i < 400; i++) t.Segments.Add(new Segment { Index = i, Curve = i is >= 150 and < 200 or >= 350 ? bend : 0 });
        return t;
    }

    private static Engine.Controls Gas(int steer = 0, bool shift = false) => new(steer, Gas: true, Shift: shift);

    // ───────────────────────────── the car ─────────────────────────────

    [Fact]
    public void Low_gear_pulls_hard_but_tops_out_and_high_gear_goes_faster()
    {
        var g = Game();
        g.UseTrack(Oval(0));
        g.Run(60 * 3, Gas());
        Assert.InRange(g.Speed, GameEngine.LowTop - 10, GameEngine.LowTop);
        g.Run(1, Gas(shift: true));
        Assert.Equal(1, g.Gear);
        g.Run(60 * 10, Gas());
        Assert.True(g.Speed > 280);
        // High gear from a standstill is a crawl.
        var h = Game();
        h.UseTrack(Oval(0));
        h.SetGear(1);
        h.Run(60 * 2, Gas());
        Assert.True(h.Speed < 50);
    }

    [Fact]
    public void The_brake_stops_the_car_and_lifting_off_slows_it()
    {
        var g = Game();
        g.UseTrack(Oval(0));
        g.Place(0, 0, 280);
        g.Run(60, default);
        Assert.InRange(g.Speed, 230, 260);
        g.Run(120, new Engine.Controls(0, Brake: true));
        Assert.Equal(0, g.Speed);
    }

    [Fact]
    public void Steering_moves_the_car_across_the_road_and_the_grass_slows_it()
    {
        var g = Game();
        g.UseTrack(Oval(0));
        g.Place(0, 0, 200);
        g.Run(30, Gas(steer: 1));
        Assert.True(g.X > 0.5);
        g.Place(g.Z, 1.6, 250);
        g.Run(60, Gas());
        Assert.True(g.OffRoad);
        Assert.True(g.Speed < 170);
    }

    [Fact]
    public void A_bend_pulls_a_fast_car_to_the_outside()
    {
        var g = Game();
        var t = Oval(5);
        g.UseTrack(t);
        g.Place(150 * Track.SegmentLength + 10, 0, 300);
        g.SetGear(1);
        g.Run(20, Gas());
        Assert.True(g.X < -0.4);
        var slow = Game();
        slow.UseTrack(t);
        slow.Place(150 * Track.SegmentLength + 10, 0, 120);
        slow.Run(20, new Engine.Controls(0));
        Assert.True(slow.X > -0.2);
    }

    [Fact]
    public void Hitting_a_billboard_or_a_car_crashes_and_the_car_restarts_on_the_road()
    {
        var g = Game();
        var t = Oval(0);
        t.Segments[20].Sprites.Add(new Sprite(SpriteKind.Billboard, -1.5, "BOARD"));
        g.UseTrack(t);
        g.Place(19 * Track.SegmentLength, -1.5, 150);
        g.Run(30, Gas());
        Assert.True(g.Crashed > 0);
        Assert.Equal(0, g.Speed);
        g.Idle(60 * 3);
        Assert.True(g.Crashed <= 0);
        Assert.InRange(g.X, -0.6, 0.6);
        // Into the back of a slower car.
        var h = Game();
        h.UseTrack(Oval(0));
        h.Place(1000, 0, 250);
        h.SetGear(1);
        h.PutCar(2500, 0, 120);
        h.Run(60, Gas());
        Assert.True(h.Crashed > 0);
        Assert.Equal("COLLISION!", h.Cause);
    }

    // ───────────────────────────── the meeting ─────────────────────────────

    [Fact]
    public void The_lights_go_out_then_qualifying_starts()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(Phase.QualifyStart, g.Phase);
        g.Run(60 * 2, Gas());
        Assert.Equal(0, g.Speed);
        g.Idle(60 * 2);
        Assert.Equal(Phase.Qualifying, g.Phase);
    }

    [Fact]
    public void A_fast_qualifying_lap_takes_pole_and_its_bonus()
    {
        var g = Game();
        for (int t = 0; t < 60 * 120 && g.Phase == Phase.Qualifying; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(Phase.QualifyResult, g.Phase);
        Assert.Equal(1, g.Grid);
        Assert.True(g.Score >= GameEngine.GridBonus[0]);
        g.Idle(60 * 5);
        Assert.Equal(Phase.RaceStart, g.Phase);
    }

    [Fact]
    public void Too_slow_to_qualify_ends_the_game()
    {
        var g = Game();
        // Low gear all the way round is too slow.
        for (int i = 0; i < 60 * 85 && g.Phase == Phase.Qualifying; i++) g.Run(1, new Engine.Controls(0, Gas: true));
        Assert.Equal(Phase.Over, g.Phase);
        Assert.Contains("QUALIFY", g.Message);
        g.Idle(60 * 4);
        Assert.Contains(g.State, new[] { GameState.GameOver, GameState.EnterInitials });
    }

    [Fact]
    public void Each_lap_buys_more_time_and_the_fourth_is_the_flag()
    {
        var g = Game();
        g.Racing();
        double time = g.TimeLeft;
        g.Place(g.Track.Length - 100, 0, 200);
        g.Run(10, Gas());
        // Through the line for the first time: the race lap begins.
        Assert.Equal(0, g.Laps);
        for (int lap = 1; lap < GameEngine.RaceLaps; lap++)
        {
            g.Place(g.Track.Length - 100, 0, 200);
            typeof(GameEngine).GetProperty("Total")!.SetValue(g, g.Track.Length * lap - 50);
            double before = g.TimeLeft;
            g.Run(10, Gas());
            Assert.Equal(lap, g.Laps);
            Assert.True(g.TimeLeft > before + g.Track.LapExtend - 1);
        }
        g.Place(g.Track.Length - 100, 0, 200);
        typeof(GameEngine).GetProperty("Total")!.SetValue(g, g.Track.Length * GameEngine.RaceLaps - 50);
        g.Run(10, Gas());
        Assert.Equal(Phase.Finished, g.Phase);
        Assert.True(g.FinishBonus > 0);
        _ = time;
    }

    [Fact]
    public void Passing_a_car_in_the_race_scores_fifty()
    {
        var g = Game();
        g.Racing();
        g.Cars.Clear();
        g.Place(1000, -0.5, 280);
        g.SetGear(1);
        g.PutCar(1600, 0.5, 100);
        int score = g.Score;
        g.Run(60, Gas());
        Assert.True(g.Score >= score + 50);
        Assert.Equal(1, g.Position);
    }

    [Fact]
    public void Out_of_time_in_the_race_ends_the_game()
    {
        var g = Game();
        g.Racing();
        g.TimeLeft = 1;
        g.Idle(70);
        Assert.Equal(Phase.Over, g.Phase);
        g.Idle(60 * 4);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_other_drivers_slow_for_the_bends()
    {
        var g = Game();
        var t = Oval(6);
        g.UseTrack(t);
        Assert.True(g.SafeSpeed(140 * Track.SegmentLength) < g.SafeSpeed(20 * Track.SegmentLength));
    }

    [Fact]
    public void Every_circuit_is_a_proper_lap_with_its_start_gantry()
    {
        for (int i = 0; i < Track.Count; i++)
        {
            var t = Track.Make(i);
            Assert.InRange(t.Segments.Count, 1000, 1500);
            Assert.Contains(t.Segments[0].Sprites, s => s.Kind == SpriteKind.Gantry);
            Assert.Contains(t.Segments, s => Math.Abs(s.Curve) >= 4);
            Assert.Equal(Track.Make(i).Segments.Sum(s => s.Sprites.Count), t.Segments.Sum(s => s.Sprites.Count));
        }
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_driver_qualifies_and_wins_the_first_race()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        for (int t = 0; t < 60 * 400 && g.Races == 0 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(1, g.Races);
    }

    [Fact]
    public void The_attract_mode_drives_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 15);
        Assert.True(g.Speed > 100);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 10) && g.State == GameState.Attract; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void A_good_score_asks_for_initials()
    {
        var g = Game();
        g.Score = 99999;
        g.TimeLeft = 0.5;
        g.Idle(60 * 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('A');
        g.Initials.Type('P');
        g.Initials.Type('X');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("APX", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 90; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score}:{g.Phase}:{g.Total:0}:{g.X:0.000}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 3);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 3), HighScoreTable.Load(folder).Entries[0]);
    }
}
