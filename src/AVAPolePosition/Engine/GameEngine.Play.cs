namespace AVAPolePosition.Engine;

public sealed partial class GameEngine
{
    // The car: top speeds in each gear, how much speed is distance, how the bends pull.
    public const double LowTop = 190, HighTop = 320, KmhToUnits = 16;
    public const double SteerRate = 1.5, Pull = 0.37;
    public const int RaceLaps = 4;
    public const double CarLength = 180, CarWidth = 0.38;
    public static readonly int[] GridBonus = [4000, 2000, 1400, 1000, 800, 600, 400, 200];

    public Track Track { get; private set; } = Track.Make(0);
    public int TrackIndex { get; private set; }
    public int Races { get; private set; }
    public List<Car> Cars { get; } = [];
    public int Score { get; internal set; }
    public double TimeLeft { get; internal set; }

    // The player's car.
    public double Z { get; private set; }
    public double Total { get; private set; }
    public double X { get; internal set; }
    public double Speed { get; internal set; }
    /// <summary>0 for low gear, 1 for high.</summary>
    public int Gear { get; private set; }
    public double Crashed { get; private set; }
    public double Safe { get; private set; }
    public int Steering { get; private set; }
    public bool OffRoad => Math.Abs(X) > 1.0;
    public bool Skidding { get; private set; }
    public int Laps { get; private set; }
    public double LapTime { get; private set; }
    public double QualifyTime { get; private set; }
    public int Grid { get; private set; }
    public int FinishBonus { get; private set; }
    /// <summary>How far the scenery has turned, for the mountains.</summary>
    public double Scenery { get; private set; }
    public string? Cause { get; private set; }
    /// <summary>The computer driver, for the demonstration (and the tests).</summary>
    public Driver Pilot { get; private set; } = new(1);

    private bool _shiftPrev;
    private double _rumble, _squeal;
    private readonly HashSet<int> _ahead = [];

    /// <summary>Test hook: no other cars.</summary>
    internal bool Calm { get; set; }

    public double Curve => Track.At(Z).Curve;
    /// <summary>The player's place in the race: one plus the cars ahead.</summary>
    public int Position => 1 + Cars.Count(c => c.Total > Total);

    private void NewGame()
    {
        _rng = new Random(_seed + (int)(StateTime * 997));
        Score = 0;
        Races = 0;
        Pilot = new Driver(_rng.Next());
        Effects.Clear();
        StartMeeting(0);
    }

    private void StartMeeting(int track)
    {
        TrackIndex = track;
        Track = Track.Make(track);
        ResetCar(0);
        Cars.Clear();
        // A few cars out on the circuit while you qualify.
        if (!Calm)
            for (int i = 0; i < 4; i++)
                Cars.Add(NewCar(i, Track.Length * (0.15 + i * 0.2), i % 2 == 0 ? -0.5 : 0.5, startSpeed: 220));
        TimeLeft = Track.QualifyLimit;
        SetPhase(Phase.QualifyStart);
    }

    private void ResetCar(double z)
    {
        Z = Track.Mod(z, Track.Length);
        Total = 0;
        X = 0;
        Speed = 0;
        Gear = 0;
        Crashed = 0;
        Laps = 0;
        LapTime = 0;
    }

    private Car NewCar(int id, double z, double x, double startSpeed)
    {
        uint[] colours = [0xFF2A5AD0, 0xFFF0C030, 0xFF2AA04A, 0xFFE07A2A, 0xFF8A3AC0, 0xFFE8E8E8, 0xFF2AB0C0, 0xFFD02A8A];
        return new Car
        {
            Id = id, Z = Track.Mod(z, Track.Length), Total = z, X = x, TargetX = x, Speed = startSpeed,
            TopSpeed = 262 + _rng.Next(30) + Math.Min(20, Races * 5), Colour = colours[id % colours.Length], Number = id + 2,
        };
    }

    internal void AddScore(int points, string? word = null)
    {
        Score += points;
        if (word is not null) Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.Width / 2, Y = 470, Text = word, Life = 1, Colour = 0xFFFFE14A });
    }

    // ───────────────────────────── the meeting ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.QualifyStart:
                Lights(dt);
                MoveCars(dt);
                if (PhaseTime > 3.6) { SetPhase(Phase.Qualifying); Music(true); }
                return;
            case Phase.Qualifying:
                Drive(c, dt);
                MoveCars(dt);
                Clock(dt);
                if (Total >= Track.Length) Qualified();
                return;
            case Phase.QualifyResult:
                Coast(dt);
                MoveCars(dt);
                if (PhaseTime > 4) LineUp();
                return;
            case Phase.RaceStart:
                Lights(dt);
                if (PhaseTime > 3.6) { SetPhase(Phase.Racing); Music(true); }
                return;
            case Phase.Racing:
                Drive(c, dt);
                MoveCars(dt);
                Clock(dt);
                if (Phase == Phase.Racing && Total >= Track.Length * (Laps + 1)) LapDone();
                return;
            case Phase.Finished:
                Coast(dt);
                MoveCars(dt);
                if (PhaseTime > 5) { Races++; StartMeeting(TrackIndex + 1); }
                return;
            case Phase.Over:
                Coast(dt);
                MoveCars(dt);
                if (PhaseTime > 3) GameOver();
                return;
        }
    }

    private void Lights(double dt)
    {
        foreach (double t in new[] { 0.6, 1.6, 2.6 })
            if (PhaseTime >= t && PhaseTime - dt < t) Play(Sfx.Light);
        if (PhaseTime >= 3.6 && PhaseTime - dt < 3.6) Play(Sfx.Go);
    }

    private void Clock(double dt)
    {
        TimeLeft -= dt;
        LapTime += dt;
        if (TimeLeft > 0) return;
        TimeLeft = 0;
        Music(false);
        Play(Sfx.TimeUp);
        Say(Phase == Phase.Qualifying ? "DID NOT QUALIFY" : "TIME UP", 3);
        SetPhase(Phase.Over);
    }

    private void Qualified()
    {
        QualifyTime = LapTime;
        // The grid by time: two seconds a place, from well inside the limit.
        double pole = Track.QualifyLimit - 18;
        Grid = Math.Clamp(1 + (int)Math.Floor((QualifyTime - pole) / 2), 1, 8);
        AddScore(GridBonus[Grid - 1]);
        Music(false);
        Play(Sfx.Qualified);
        SetPhase(Phase.QualifyResult);
    }

    /// <summary>On the grid in qualifying order, two cars to a row behind the line, everyone standing.</summary>
    private void LineUp()
    {
        Cars.Clear();
        double line = Track.Length - Track.SegmentLength * 2;
        for (int slot = 1; slot <= 8; slot++)
        {
            double z = line - (slot - 1) / 2 * 4 * Track.SegmentLength - (slot % 2 == 0 ? Track.SegmentLength * 1.5 : 0);
            double x = slot % 2 == 1 ? -0.5 : 0.5;
            if (slot == Grid)
            {
                ResetCar(z);
                X = x;
                Total = z - Track.Length;
            }
            else if (!Calm)
            {
                var car = NewCar(slot, z, x, startSpeed: 0);
                car.Total = z - Track.Length;
                Cars.Add(car);
            }
        }
        _ahead.Clear();
        foreach (var car in Cars.Where(car => car.Total > Total)) _ahead.Add(car.Id);
        TimeLeft = Track.LapExtend + 17;
        Laps = 0;
        LapTime = 0;
        Say("PREPARE TO RACE", 3);
        SetPhase(Phase.RaceStart);
    }

    private void LapDone()
    {
        Laps++;
        Play(Sfx.Lap);
        if (Laps >= RaceLaps)
        {
            Music(false);
            FinishBonus = (int)TimeLeft * 200;
            Score += FinishBonus;
            Play(Sfx.Goal);
            Say("GOAL!", 3);
            SetPhase(Phase.Finished);
            return;
        }
        TimeLeft += Track.LapExtend;
        LapTime = 0;
        Play(Sfx.Extended);
        Say(Laps == RaceLaps - 1 ? "FINAL LAP - EXTENDED PLAY" : "EXTENDED PLAY", 2);
    }

    // ───────────────────────────── driving ─────────────────────────────

    private void Drive(Controls c, double dt)
    {
        Steering = c.Steer;
        if (Safe > 0) Safe -= dt;
        if (Crashed > 0)
        {
            Crashed -= dt;
            Speed = 0;
            if (Crashed <= 0)
            {
                X = Math.Clamp(X, -0.6, 0.6);
                Safe = 1.5;
                Gear = 0;
            }
            return;
        }
        if (c.Shift && !_shiftPrev) { Gear = 1 - Gear; Play(Sfx.Shift); }
        _shiftPrev = c.Shift;
        // The accelerator: low gear pulls hard but runs out of revs; high gear is lazy until it's going.
        double accel;
        if (c.Gas)
        {
            if (Gear == 0) accel = Speed < LowTop - 5 ? 95 : -20;
            else accel = Speed < 100 ? 18 : 62 * (1 - Speed / (HighTop + 10));
        }
        else accel = -28;
        if (c.Brake) accel = -170;
        Speed = Math.Clamp(Speed + accel * dt, 0, Gear == 0 ? LowTop : HighTop);
        // Steering, and the bend pulling the car to the outside.
        double frac = Speed / HighTop;
        X += c.Steer * SteerRate * Math.Min(1, Speed / 90) * dt;
        double pull = Curve * Pull * frac * frac;
        X -= pull * dt;
        Skidding = Math.Abs(pull) > 1.0 && Math.Abs(Curve) > 2;
        _squeal -= dt;
        if (Skidding && _squeal <= 0) { _squeal = 0.45; Play(Sfx.Skid); }
        // Off the road: the grass slows you to a crawl.
        if (OffRoad)
        {
            if (Speed > 110) Speed -= 150 * dt;
            _rumble -= dt;
            if (_rumble <= 0 && Speed > 30) { _rumble = 0.35; Play(Sfx.Rumble); }
        }
        X = Math.Clamp(X, -2.8, 2.8);
        double before = Z;
        double step = Speed * KmhToUnits * dt;
        Z = Track.Mod(Z + step, Track.Length);
        Total += step;
        Scenery += Curve * frac * dt * 0.05;
        // Points for the road covered.
        int segs = (int)(Total / Track.SegmentLength) - (int)((Total - step) / Track.SegmentLength);
        if (segs > 0) Score += segs * 10;
        _ = before;
        Collide();
        Overtakes();
    }

    /// <summary>Rolling to a stop with nobody at the wheel (between sessions).</summary>
    private void Coast(double dt)
    {
        Speed = Math.Max(0, Speed - 60 * dt);
        double step = Speed * KmhToUnits * dt;
        Z = Track.Mod(Z + step, Track.Length);
        Total += step;
        X += (0 - X) * Math.Min(1, dt * 1.5);
    }

    private void Collide()
    {
        if (Safe > 0) return;
        // The things by the road.
        if (Math.Abs(X) > 1.1)
        {
            for (int k = 0; k <= 1; k++)
            {
                var seg = Track.At(Z + k * Track.SegmentLength * 0.5);
                foreach (var s in seg.Sprites)
                {
                    double half = s.Kind switch
                    {
                        SpriteKind.Billboard => 0.38, SpriteKind.Stand => 0.7, SpriteKind.Tree => 0.2, SpriteKind.Bush => 0.15,
                        SpriteKind.Chevron => 0.14, SpriteKind.Gantry => 0, _ => 0.2,
                    };
                    if (s.Kind == SpriteKind.Gantry)
                    {
                        if (Math.Abs(Math.Abs(X) - 1.3) < 0.12) { Crash("INTO THE GANTRY!"); return; }
                        continue;
                    }
                    if (Math.Abs(s.Offset - X) < half) { Crash(s.Kind == SpriteKind.Billboard ? "INTO A BILLBOARD!" : "OFF THE ROAD!"); return; }
                }
            }
        }
        // The other cars.
        foreach (var car in Cars)
        {
            double dz = Gap(car);
            if (Math.Abs(dz) < CarLength && Math.Abs(car.X - X) < CarWidth)
            {
                car.Z = Track.Mod(car.Z + CarLength * 1.5, Track.Length);
                car.Total += CarLength * 1.5;
                Crash("COLLISION!");
                return;
            }
        }
    }

    /// <summary>How far a car is ahead of the player (negative if behind), the short way round.</summary>
    public double Gap(Car car)
    {
        double d = Track.Mod(car.Z - Z, Track.Length);
        return d > Track.Length / 2 ? d - Track.Length : d;
    }

    private void Crash(string why)
    {
        Cause = why;
        Crashed = 2.0;
        Speed = 0;
        Play(Sfx.Crash);
        Say(why, 1.8);
    }

    /// <summary>Fifty points for every car passed in the race.</summary>
    private void Overtakes()
    {
        if (Phase != Phase.Racing) return;
        foreach (var car in Cars)
        {
            bool ahead = car.Total > Total;
            if (!ahead && _ahead.Remove(car.Id)) { AddScore(50, "PASS!"); Play(Sfx.Pass); }
            else if (ahead) _ahead.Add(car.Id);
        }
    }

    // ───────────────────────────── the other drivers ─────────────────────────────

    /// <summary>The fastest a car can take the bends just ahead.</summary>
    public double SafeSpeed(double z, double margin = 1.25)
    {
        double worst = 0.2;
        for (int k = 1; k <= 14; k++) worst = Math.Max(worst, Math.Abs(Track.At(z + k * Track.SegmentLength).Curve));
        return HighTop * Math.Sqrt(margin / (Pull * worst));
    }

    private void MoveCars(double dt)
    {
        foreach (var car in Cars)
        {
            double want = Math.Min(car.TopSpeed, SafeSpeed(car.Z));
            car.Speed += Math.Clamp(want - car.Speed, -120 * dt, 48 * dt);
            // Round a slower car in the same lane.
            var blocker = Cars.FirstOrDefault(o => o != car && Math.Abs(o.X - car.X) < 0.5 && Track.Mod(o.Z - car.Z, Track.Length) is > 0 and < 1500 && o.Speed < car.Speed);
            if (blocker is not null) car.TargetX = car.X > 0 ? -0.5 : 0.5;
            car.X += Math.Clamp(car.TargetX - car.X, -0.8 * dt, 0.8 * dt);
            double step = car.Speed * KmhToUnits * dt;
            car.Z = Track.Mod(car.Z + step, Track.Length);
            car.Total += step;
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Go()
    {
        if (Phase == Phase.QualifyStart) SetPhase(Phase.Qualifying);
        else if (Phase == Phase.RaceStart) SetPhase(Phase.Racing);
    }

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal void Place(double z, double x = 0, double speed = 0)
    {
        Z = Track.Mod(z, Track.Length);
        X = x;
        Speed = speed;
        Gear = speed > LowTop ? 1 : 0;
    }

    internal Car PutCar(double z, double x, double speed)
    {
        var car = NewCar(Cars.Count + 10, z, x, speed);
        car.TopSpeed = speed;
        car.Total = Total + Track.Mod(z - Z, Track.Length);
        Cars.Add(car);
        _ahead.Add(car.Id);
        return car;
    }

    internal void UseTrack(Track t)
    {
        Track = t;
        Z = 0;
    }

    internal void SetGear(int gear) => Gear = gear;
    internal void Racing(int grid = 1) { Grid = grid; LineUp(); SetPhase(Phase.Racing); }
}
