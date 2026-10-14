namespace AVAPolePosition.Engine;

/// <summary>
/// The computer driver: changes up when low gear runs out, brakes for the bends it can see coming,
/// picks a line that misses the slower cars ahead, and steers against the pull of the bends to hold
/// it. Used for the demonstration and the tests.
/// </summary>
public sealed class Driver(int seed)
{
    private readonly Random _rng = new(seed);
    private double _line;
    private bool _shiftHeld;

    public Controls Decide(GameEngine g)
    {
        if (g.Phase is not (Phase.Qualifying or Phase.Racing) || g.Crashed > 0) { _shiftHeld = false; return default; }
        // Gears: up at the top of low, down if it's crawling in high.
        bool shift = false;
        if (!_shiftHeld && (g.Gear == 0 && g.Speed > GameEngine.LowTop - 12 || g.Gear == 1 && g.Speed < 80)) shift = true;
        _shiftHeld = shift;

        // A line clear of the slower cars just ahead.
        double line = 0;
        foreach (var car in g.Cars.OrderBy(c => g.Gap(c)))
        {
            double gap = g.Gap(car);
            if (gap < -GameEngine.CarLength || gap > 2600 || car.Speed > g.Speed + 5 && gap > 0) continue;
            if (Math.Abs(car.X - line) < 0.55) line = car.X > 0 ? car.X - 0.62 : car.X + 0.62;
        }
        _line += (Math.Clamp(line, -0.8, 0.8) - _line) * 0.15;

        double safe = g.SafeSpeed(g.Z, 1.3) - 6;
        bool brake = g.Speed > safe + 12;
        bool gas = !brake && g.Speed < safe;
        // Steer for the line, allowing for the bend's pull.
        double frac = g.Speed / GameEngine.HighTop;
        double pull = g.Curve * GameEngine.Pull * frac * frac;
        double needed = (_line - g.X) * 3 + pull;
        int steer = Math.Abs(needed) > 0.25 ? Math.Sign(needed) : 0;
        return new Controls(steer, gas, brake, shift);
    }
}
