namespace AVAPolePosition.Engine;

/// <summary>
/// A circuit: a loop of road segments, each bending a little, built from straights and curves
/// that ease in and out, with billboards, trees and warning chevrons along the way. The circuits
/// and the names on the boards are this game's own.
/// </summary>
public sealed class Track
{
    public const double SegmentLength = 200;
    public const double RoadWidth = 2000;
    public string Name = "";
    public string Country = "";
    public List<Segment> Segments = [];
    /// <summary>The time to qualify in, and how much a completed lap adds in the race.</summary>
    public double QualifyLimit, LapExtend;
    public uint Grass = 0xFF3A8A2A, Mountain = 0xFF5A6A8A;

    public double Length => Segments.Count * SegmentLength;
    public Segment At(double z) => Segments[(int)(Mod(z, Length) / SegmentLength) % Segments.Count];
    public static double Mod(double a, double b) => (a % b + b) % b;

    public static int Count => 3;

    private static readonly string[] Boards = ["GRIDLOCK TYRES", "TURBO TEA", "KESTREL OIL", "AVA MOTORS", "FIZZ", "APEX WATCHES", "PITSTOP PIZZA", "VELOCITY FM"];

    public static Track Make(int index) => (index % Count) switch
    {
        0 => Build("KESTREL RING", "THE NORTHERN HILLS", 0xFF3A8A2A, 0xFF5A6A8A, 59, 46,
        [
            (160, 0), (60, 2), (50, 0), (80, -3), (40, 0), (50, 6), (120, 0), (60, -2), (60, 2), (90, 0), (140, 1.5), (50, 0), (70, -4), (180, 0),
        ]),
        1 => Build("PORT HALVARD", "THE HARBOUR CIRCUIT", 0xFF4A9A3A, 0xFF7A8AA0, 61, 48,
        [
            (140, 0), (50, -5), (60, 0), (50, 5), (80, 0), (100, -2), (40, 0), (60, 4), (60, -4), (100, 0), (80, 3), (40, 0), (60, -6), (200, 0),
        ]),
        _ => Build("VALLEY OF ASH", "THE DESERT CIRCUIT", 0xFFA89A5A, 0xFF9A6A4A, 62, 50,
        [
            (200, 0), (80, 3), (30, 0), (80, 3), (60, 0), (50, -6), (90, 0), (120, -1.5), (60, 0), (50, 5), (50, -5), (140, 0), (90, 2.5), (150, 0),
        ]),
    };

    private static Track Build(string name, string country, uint grass, uint mountain, double qualify, double extend, (int Length, double Curve)[] pieces)
    {
        var t = new Track { Name = name, Country = country, Grass = grass, Mountain = mountain, QualifyLimit = qualify, LapExtend = extend };
        foreach (var (len, curve) in pieces)
        {
            // Curves ease in over the first quarter and out over the last.
            int ease = curve == 0 ? 0 : len / 4;
            for (int i = 0; i < len; i++)
            {
                double k = curve;
                if (i < ease) k = curve * (i / (double)ease);
                else if (i >= len - ease) k = curve * ((len - i) / (double)ease);
                t.Segments.Add(new Segment { Index = t.Segments.Count, Curve = k });
            }
        }
        var rng = new Random(name.Sum(ch => ch * 31));
        for (int i = 0; i < t.Segments.Count; i++)
        {
            var s = t.Segments[i];
            // The start gantry; boards and trees along the straights; chevrons on the outside before the bends.
            if (i == 0) s.Sprites.Add(new Sprite(SpriteKind.Gantry, 0, "START"));
            if (i is > 4 and < 40 && i % 6 == 0) { s.Sprites.Add(new Sprite(SpriteKind.Stand, -2.2)); s.Sprites.Add(new Sprite(SpriteKind.Stand, 2.2)); }
            double ahead = Enumerable.Range(1, 20).Select(j => t.Segments[(i + j) % t.Segments.Count].Curve).OrderByDescending(Math.Abs).First();
            if (Math.Abs(ahead) >= 3 && Math.Abs(s.Curve) < 0.5 && i % 4 == 0)
                s.Sprites.Add(new Sprite(SpriteKind.Chevron, ahead > 0 ? -1.35 : 1.35, ahead > 0 ? ">" : "<"));
            else if (i % 25 == 10)
            {
                int side = (i / 25) % 2 == 0 ? 1 : -1;
                s.Sprites.Add(new Sprite(SpriteKind.Billboard, side * (1.45 + rng.NextDouble() * 0.3), Boards[rng.Next(Boards.Length)], (uint)(0xFF000000 | (uint)rng.Next(0x404040, 0xF0F0F0))));
            }
            if (i % 9 == 3) s.Sprites.Add(new Sprite(rng.Next(3) == 0 ? SpriteKind.Bush : SpriteKind.Tree, (rng.Next(2) == 0 ? -1 : 1) * (2.4 + rng.NextDouble() * 2.5)));
        }
        return t;
    }
}
