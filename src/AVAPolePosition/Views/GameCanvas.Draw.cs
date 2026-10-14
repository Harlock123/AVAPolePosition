using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPolePosition.Engine;

namespace AVAPolePosition.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPolePosition/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Ink = 0xFF101014, Green = 0xFF5AE05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Quad(DrawingContext dc, uint fill, double x1, double y1, double w1, double x2, double y2, double w2)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x1 - w1, y1), true);
            c.LineTo(new Point(x1 + w1, y1));
            c.LineTo(new Point(x2 + w2, y2));
            c.LineTo(new Point(x2 - w2, y2));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(fill), null, g);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, 1.5) : null, g);
    }

    // The camera: high behind the car, looking down the road.
    private const double CameraHeight = 1500, CameraDepth = 0.84, CameraBack = 2000, Focal = 512;
    private const int DrawSegments = 220;

    /// <summary>A road segment's near edge on screen: centre, height and half-width, and its scale.</summary>
    private readonly record struct Edge(double X, double Y, double W, double Scale);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawSky(dc);
        DrawRoad(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver) Text(dc, "GAME OVER", new Point(Field.Width / 2, 250), 100, Red, center: true);
        if (g.State == GameState.Attract && g.Phase is Phase.Qualifying or Phase.Racing)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, 140), 26, Yellow, center: true);
    }

    // ───────────────────────────── the scenery ─────────────────────────────

    private void DrawSky(DrawingContext dc)
    {
        var g = Engine;
        for (int i = 0; i < 18; i++)
        {
            double t = i / 17.0;
            uint c = 0xFF000000 | (uint)(60 + 110 * t) << 16 | (uint)(120 + 100 * t) << 8 | (uint)(220 + 20 * t);
            dc.FillRectangle(Brush(c), new Rect(0, Field.Top + i * 16, Field.Width, 17));
        }
        // Clouds, mountains and hills, turning with the road (the nearer, the faster).
        double turn = g.Scenery;
        for (int i = 0; i < 6; i++)
        {
            double x = ((Hash(i, 1, 3) % 1400) - turn * 160) % 1400;
            if (x < -200) x += 1400;
            double y = Field.Top + 30 + Hash(i, 2, 3) % 90;
            dc.DrawEllipse(Brush(0xD0FFFFFF), null, new Point(x, y), 60, 14);
            dc.DrawEllipse(Brush(0xD0FFFFFF), null, new Point(x + 40, y - 8), 40, 14);
        }
        Range(dc, g.Track.Mountain, turn * 300, 120, 7, snow: true);
        Range(dc, Shade(g.Track.Grass, 0.7), turn * 600, 45, 11, snow: false);
    }

    private static void Range(DrawingContext dc, uint colour, double shift, double height, int salt, bool snow)
    {
        double period = 1024;
        double off = (shift % period + period) % period;
        var pts = new List<Point> { new(-10, Field.Horizon + 2) };
        for (int i = -1; i <= 14; i++)
        {
            double x = i * 80 - off;
            double h = height * (0.35 + (Hash((i + (int)Math.Floor(shift / period) * 13 + 100) % 13, salt, 5) % 100) / 100.0 * 0.65);
            pts.Add(new Point(x, Field.Horizon - h));
            pts.Add(new Point(x + 40, Field.Horizon - h * 0.55));
        }
        pts.Add(new Point(Field.Width + 10, Field.Horizon + 2));
        Poly(dc, colour, null, pts.ToArray());
        if (!snow) return;
        for (int i = 1; i < pts.Count - 1; i += 2)
        {
            var p = pts[i];
            if (Field.Horizon - p.Y < height * 0.75) continue;
            Poly(dc, 0xFFF0F4FF, null, p, new Point(p.X + 14, p.Y + 16), new Point(p.X - 14, p.Y + 16));
        }
    }

    // ───────────────────────────── the road ─────────────────────────────

    private void DrawRoad(DrawingContext dc)
    {
        var g = Engine;
        var track = g.Track;
        double camZ = g.Z - CameraBack;
        int baseSeg = (int)Math.Floor(Track.Mod(camZ, track.Length) / Track.SegmentLength);
        double part = Track.Mod(camZ, track.Length) % Track.SegmentLength;
        // The road bends: each segment shifts sideways by an amount that grows with the curve.
        int n = DrawSegments;
        var centre = new double[n + 2];
        double x = 0, dx = -track.Segments[baseSeg].Curve * part / Track.SegmentLength;
        for (int i = 0; i <= n + 1; i++)
        {
            centre[i] = x;
            x += dx;
            dx += track.Segments[(baseSeg + i) % track.Segments.Count].Curve;
        }
        // The camera sits over the player's car, wherever it is on the road.
        double playerSeg = CameraBack / Track.SegmentLength + part / Track.SegmentLength;
        int ps = (int)playerSeg;
        double camX = g.X * Track.RoadWidth + centre[ps] + (centre[ps + 1] - centre[ps]) * (playerSeg - ps);
        Edge Project(int i)
        {
            double z = Math.Max(1, i * Track.SegmentLength - part);
            double s = CameraDepth / z;
            return new Edge(Field.Width / 2 + s * (centre[i] - camX) * Focal, Field.Horizon + s * CameraHeight * Focal, s * Track.RoadWidth * Focal, s);
        }
        var edges = new Edge[n + 1];
        for (int i = 0; i <= n; i++) edges[i] = Project(i);
        dc.FillRectangle(Brush(track.Grass), new Rect(0, Field.Horizon, Field.Width, Field.Height - Field.Horizon));
        for (int i = n - 1; i >= 0; i--)
        {
            var a = edges[i];
            var b = edges[i + 1];
            if (a.Y < Field.Horizon || b.Y > Field.Height + 400) continue;
            var seg = track.Segments[(baseSeg + i) % track.Segments.Count];
            bool light = seg.Light;
            uint grass = light ? Shade(track.Grass, 1.08) : track.Grass;
            dc.FillRectangle(Brush(grass), new Rect(0, b.Y, Field.Width, Math.Max(1, a.Y - b.Y + 1)));
            Quad(dc, light ? 0xFFF0F0F0 : 0xFFD02A2A, a.X, a.Y, a.W * 1.14, b.X, b.Y, b.W * 1.14);
            Quad(dc, light ? 0xFF6A6A72 : 0xFF626269, a.X, a.Y, a.W, b.X, b.Y, b.W);
            if (light)
                foreach (double lane in new[] { -1 / 3.0, 1 / 3.0 })
                    Quad(dc, 0xFFE8E8E8, a.X + a.W * lane, a.Y, a.W * 0.02, b.X + b.W * lane, b.Y, b.W * 0.02);
            if (seg.Index is 0 or 1)
            {
                // The chequered line.
                for (int k = 0; k < 12; k++)
                {
                    if ((k + seg.Index) % 2 == 1) continue;
                    double f0 = -1 + k / 6.0, f1 = f0 + 1 / 6.0;
                    var g2 = new StreamGeometry();
                    using (var c = g2.Open())
                    {
                        c.BeginFigure(new Point(a.X + a.W * f0, a.Y), true);
                        c.LineTo(new Point(a.X + a.W * f1, a.Y));
                        c.LineTo(new Point(b.X + b.W * f1, b.Y));
                        c.LineTo(new Point(b.X + b.W * f0, b.Y));
                        c.EndFigure(true);
                    }
                    dc.DrawGeometry(Brush(Ink), null, g2);
                }
            }
        }
        // Roadside things and the other cars, far to near.
        var cars = g.Cars.Select(c => (Car: c, Rel: g.Gap(c) + CameraBack)).Where(c => c.Rel > 300 && c.Rel < n * Track.SegmentLength).ToList();
        for (int i = n - 1; i >= 1; i--)
        {
            var e = edges[i];
            var seg = track.Segments[(baseSeg + i) % track.Segments.Count];
            // Things right over the camera have gone past already.
            if (i >= 4) foreach (var sp in seg.Sprites) DrawSprite(dc, sp, e);
            foreach (var (car, rel) in cars)
            {
                double fi = (rel + part) / Track.SegmentLength;
                if ((int)fi != i) continue;
                int ci = (int)fi;
                double cx = centre[ci] + (centre[ci + 1] - centre[ci]) * (fi - ci);
                double s = CameraDepth / Math.Max(1, rel);
                double sx = Field.Width / 2 + s * (cx + car.X * Track.RoadWidth - camX) * Focal;
                double sy = Field.Horizon + s * CameraHeight * Focal;
                DrawRacer(dc, sx, sy, s * 900 * Focal, car.Colour, 0, car.Total, car.Number);
            }
        }
        DrawPlayer(dc);
    }

    private void DrawSprite(DrawingContext dc, Sprite sp, Edge e)
    {
        double px(double units) => units * e.Scale * Focal;
        double x = e.X + e.Scale * sp.Offset * Track.RoadWidth * Focal, y = e.Y;
        if (y < Field.Horizon) return;
        switch (sp.Kind)
        {
            case SpriteKind.Billboard:
            {
                double w = px(1700), h = px(950), leg = px(700);
                dc.FillRectangle(Brush(0xFF5A4A3A), new Rect(x - w * 0.35, y - leg, Math.Max(1, w * 0.05), leg));
                dc.FillRectangle(Brush(0xFF5A4A3A), new Rect(x + w * 0.30, y - leg, Math.Max(1, w * 0.05), leg));
                var r = new Rect(x - w / 2, y - leg - h, w, h);
                dc.DrawRectangle(Brush(sp.Colour), Pen(Ink, Math.Max(1, w * 0.012)), r);
                if (w > 30) Text(dc, sp.Text, r.Center, Math.Max(8, Math.Min(h * 0.42, w * 1.9 / Math.Max(4, sp.Text.Length))), (sp.Colour & 0xFF) + ((sp.Colour >> 8) & 0xFF) > 260 ? Ink : White, center: true);
                break;
            }
            case SpriteKind.Chevron:
            {
                double w = px(420), h = px(520);
                var r = new Rect(x - w / 2, y - h, w, h * 0.7);
                dc.DrawRectangle(Brush(0xFFF0F0F0), Pen(Ink, 1), r);
                double d = sp.Text == ">" ? 1 : -1;
                Poly(dc, 0xFFD02A2A, null, new Point(r.Center.X - d * w * 0.3, r.Top + 2), new Point(r.Center.X + d * w * 0.3, r.Center.Y), new Point(r.Center.X - d * w * 0.3, r.Bottom - 2), new Point(r.Center.X - d * w * 0.05, r.Center.Y));
                dc.FillRectangle(Brush(Ink), new Rect(x - w * 0.04, r.Bottom, Math.Max(1, w * 0.08), h * 0.3));
                break;
            }
            case SpriteKind.Tree:
            {
                double w = px(900), h = px(1700);
                dc.FillRectangle(Brush(0xFF5A3A1A), new Rect(x - w * 0.08, y - h * 0.4, Math.Max(1, w * 0.16), h * 0.4));
                Poly(dc, 0xFF1E5A1E, null, new Point(x, y - h), new Point(x + w / 2, y - h * 0.3), new Point(x - w / 2, y - h * 0.3));
                Poly(dc, 0xFF2A6A2A, null, new Point(x, y - h * 1.05), new Point(x + w * 0.35, y - h * 0.55), new Point(x - w * 0.35, y - h * 0.55));
                break;
            }
            case SpriteKind.Bush:
            {
                double w = px(700);
                dc.DrawEllipse(Brush(0xFF2A6A2A), null, new Point(x, y - w * 0.25), w / 2, w * 0.3);
                break;
            }
            case SpriteKind.Stand:
            {
                double w = px(2600), h = px(1000);
                var r = new Rect(x - w / 2, y - h, w, h);
                dc.DrawRectangle(Brush(0xFF6A6A78), Pen(Ink, 1), r);
                for (int row = 0; row < 4; row++)
                    for (int k = 0; k < 14; k++)
                        dc.FillRectangle(Brush(Hash(k, row, (int)sp.Offset + 9) % 3 == 0 ? 0xFFE0C040 : Hash(k, row, 3) % 2 == 0 ? 0xFFD06A4A : 0xFF4A6AD0), new Rect(r.X + w * (k + 0.2) / 14, r.Y + h * (row + 0.15) / 4.5, w / 20, h / 7));
                dc.FillRectangle(Brush(0xFFD02A2A), new Rect(r.X, r.Y - h * 0.15, w, h * 0.15));
                break;
            }
            case SpriteKind.Gantry:
            {
                double l = e.X - e.W * 1.3, rr = e.X + e.W * 1.3, h = px(1700), t = Math.Max(2, px(120));
                dc.FillRectangle(Brush(0xFF3A3A44), new Rect(l - t / 2, y - h, t, h));
                dc.FillRectangle(Brush(0xFF3A3A44), new Rect(rr - t / 2, y - h, t, h));
                var r = new Rect(l, y - h - px(320), rr - l, px(320));
                dc.DrawRectangle(Brush(0xFFF0F0F0), Pen(Ink, 1), r);
                if (r.Width > 60) Text(dc, sp.Text, r.Center, Math.Max(10, r.Height * 0.7), 0xFFC02020, center: true);
                break;
            }
        }
    }

    // ───────────────────────────── cars ─────────────────────────────

    /// <summary>An open-wheel racer from behind: tyres, body, the driver's helmet, the rear wing.</summary>
    private static void DrawRacer(DrawingContext dc, double x, double y, double w, uint colour, int steer, double spin, int number)
    {
        if (w < 3) return;
        double h = w * 0.42;
        double tyreW = w * 0.2, tyreH = h * 0.62;
        // Rear tyres, turning.
        foreach (double side in new[] { -1.0, 1.0 })
        {
            var t = new Rect(x + side * (w / 2 - tyreW / 2) - tyreW / 2, y - tyreH, tyreW, tyreH);
            dc.DrawRectangle(Brush(0xFF161618), null, t, tyreW * 0.2, tyreW * 0.2);
            if (w > 40)
                for (int k = 0; k < 3; k++)
                {
                    double ty = t.Y + ((spin * 0.01 + k / 3.0) % 1) * t.Height;
                    dc.DrawLine(Pen(0xFF3A3A40, Math.Max(1, w * 0.008)), new Point(t.X + 2, ty), new Point(t.Right - 2, ty));
                }
        }
        // Body, sidepods and engine cover.
        double lean = steer * w * 0.03;
        Poly(dc, colour, Ink, new Point(x - w * 0.3 + lean, y - h * 0.15), new Point(x + w * 0.3 + lean, y - h * 0.15), new Point(x + w * 0.28 + lean, y - h * 0.55), new Point(x - w * 0.28 + lean, y - h * 0.55));
        Poly(dc, Shade(colour, 0.8), Ink, new Point(x - w * 0.12 + lean, y - h * 0.55), new Point(x + w * 0.12 + lean, y - h * 0.55), new Point(x + w * 0.08 + lean, y - h * 0.85), new Point(x - w * 0.08 + lean, y - h * 0.85));
        // The helmet.
        dc.DrawEllipse(Brush(0xFFF0F0F0), Pen(Ink, Math.Max(1, w * 0.006)), new Point(x + lean, y - h * 0.9), w * 0.07, w * 0.06);
        dc.FillRectangle(Brush(0xFF2A2A60), new Rect(x - w * 0.05 + lean, y - h * 0.94, w * 0.1, w * 0.02));
        // The rear wing on its stays.
        dc.FillRectangle(Brush(Ink), new Rect(x - w * 0.02 + lean, y - h * 1.15, w * 0.04, h * 0.4));
        var wing = new Rect(x - w * 0.36 + lean, y - h * 1.32, w * 0.72, h * 0.2);
        dc.DrawRectangle(Brush(Shade(colour, 0.7)), Pen(Ink, Math.Max(1, w * 0.006)), wing);
        if (w > 60) Text(dc, number.ToString(), wing.Center, Math.Max(8, wing.Height * 0.9), White, center: true);
        // Tail light.
        dc.DrawEllipse(Brush(0xFFFF3A2A), null, new Point(x + lean, y - h * 0.2), w * 0.03, w * 0.02);
    }

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        double x = Field.Width / 2, y = 700, w = 230;
        if (g.Crashed > 0)
        {
            // Wreckage in a ball of fire.
            double t = 1 - g.Crashed / 2.0;
            for (int i = 0; i < 9; i++)
            {
                double a = i * 0.7, r = 30 + t * 90;
                dc.DrawEllipse(Brush(i % 2 == 0 ? 0xC0FF8A2A : 0xC0FFE04A), null, new Point(x + Math.Cos(a) * r * 0.8, y - 70 - Math.Sin(a) * r * 0.5 - t * 40), 30 * (1 - t) + 10, 26 * (1 - t) + 8);
            }
            for (int i = 0; i < 6; i++)
                dc.FillRectangle(Brush(i % 2 == 0 ? 0xFFD02A2A : 0xFF161618), new Rect(x - 120 + i * 45 + Math.Sin(i * 3) * 30 * t, y - 40 - t * (60 + i * 20) + t * t * 200, 22, 12));
            return;
        }
        if (g.Safe > 0 && (int)(_clock * 12) % 2 == 0) return;
        if (g.OffRoad && g.Speed > 30)
            for (int i = 0; i < 4; i++)
                dc.DrawEllipse(Brush(0x70A08A5A), null, new Point(x + (i - 1.5) * 50, y - 10 - (_clock * 80 + i * 20) % 50), 22, 12);
        if (g.Skidding)
            foreach (double side in new[] { -1.0, 1.0 })
                dc.DrawEllipse(Brush(0x60FFFFFF), null, new Point(x + side * 95, y - 6), 30, 10);
        DrawRacer(dc, x, y, w, 0xFFD02A2A, g.Steering, g.Total, 1);
    }

    // ───────────────────────────── words ─────────────────────────────

    private static string Clock(double seconds)
    {
        int m = (int)(seconds / 60);
        double s = seconds - m * 60;
        return $"{m}'{s:00.00}".Replace('.', '"');
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C0C14), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(Red), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "TOP", new Point(14, 16), 22, Red);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString(), new Point(70, 16), 24, White);
        Text(dc, "SCORE", new Point(14, 44), 22, Red);
        Text(dc, g.Score.ToString(), new Point(90, 44), 26, White);
        Text(dc, "TIME", new Point(Field.Width / 2, 14), 22, Yellow, center: true);
        bool low = g.TimeLeft < 10 && g.Phase is Phase.Qualifying or Phase.Racing;
        if (!low || (int)(_clock * 4) % 2 == 0) Text(dc, ((int)Math.Ceiling(g.TimeLeft)).ToString(), new Point(Field.Width / 2, 44), 44, low ? Red : Yellow, center: true);
        Text(dc, "LAP", new Point(Field.Width - 380, 16), 22, Red);
        Text(dc, Clock(g.LapTime), new Point(Field.Width - 330, 16), 24, White);
        Text(dc, "SPEED", new Point(Field.Width - 380, 44), 22, Red);
        Text(dc, $"{(int)g.Speed} km", new Point(Field.Width - 300, 44), 26, White);
        Text(dc, g.Gear == 0 ? "LO" : "HI", new Point(Field.Width - 170, 40), 40, g.Gear == 0 ? Green : Yellow);
        if (g.Phase is Phase.Racing or Phase.Finished)
        {
            Text(dc, $"LAP {Math.Min(GameEngine.RaceLaps, g.Laps + 1)}/{GameEngine.RaceLaps}", new Point(Field.Width - 14, 16), 22, White, right: true);
            Text(dc, $"POS {g.Position}", new Point(Field.Width - 14, 44), 26, Yellow, right: true);
        }
        else Text(dc, g.Track.Name, new Point(Field.Width - 14, 16), 20, Dim, right: true);
    }

    private void DrawLights(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(Field.Width / 2 - 150, 160, 300, 90);
        dc.DrawRectangle(Brush(0xFF1A1A22), Pen(0xFF5A5A66, 3), r, 10, 10);
        int lit = (int)Math.Floor((g.PhaseTime - 0.6) / 1.0) + 1;
        bool go = g.PhaseTime >= 3.6;
        for (int i = 0; i < 4; i++)
        {
            var c = new Point(r.X + 45 + i * 70, r.Center.Y);
            uint col = i < 3 ? (lit > i && !go ? Red : 0xFF4A1A1A) : go ? Green : 0xFF1A4A1A;
            dc.DrawEllipse(Brush(col), Pen(Ink, 2), c, 24, 24);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        switch (g.Phase)
        {
            case Phase.QualifyStart:
                Text(dc, "PREPARE TO QUALIFY", new Point(cx, 120), 52, Yellow, center: true);
                Text(dc, $"{g.Track.Name} - {g.Track.Country}", new Point(cx, 280), 30, White, center: true);
                Text(dc, $"QUALIFY IN UNDER {g.Track.QualifyLimit:0} SECONDS", new Point(cx, 312), 26, Dim, center: true);
                DrawLights(dc);
                return;
            case Phase.QualifyResult:
                Text(dc, $"QUALIFIED {Ordinal(g.Grid)}", new Point(cx, 140), 64, Yellow, center: true);
                Text(dc, $"LAP TIME {Clock(g.QualifyTime)}    BONUS {GameEngine.GridBonus[g.Grid - 1]}", new Point(cx, 200), 32, White, center: true);
                return;
            case Phase.RaceStart:
                Text(dc, "PREPARE TO RACE", new Point(cx, 120), 52, Yellow, center: true);
                Text(dc, $"STARTING {Ordinal(g.Grid)} OF 8 - {GameEngine.RaceLaps} LAPS", new Point(cx, 280), 28, White, center: true);
                DrawLights(dc);
                return;
            case Phase.Finished:
                Text(dc, "GOAL!", new Point(cx, 140), 80, Yellow, center: true);
                Text(dc, $"FINISHED {Ordinal(g.Position)}    TIME BONUS {g.FinishBonus}", new Point(cx, 210), 32, White, center: true);
                return;
        }
        foreach (var e in g.Effects) Text(dc, e.Text, new Point(e.X, e.Y - e.Age / e.Life * 30), 34, e.Colour, center: true);
        if (g.Message is { } msg) Text(dc, msg, new Point(cx, 190), msg.Length > 18 ? 44 : 60, g.Phase == Phase.Over || g.Crashed > 0 ? Red : Yellow, center: true);
    }

    private static string Ordinal(int n) => n switch { 1 => "1ST", 2 => "2ND", 3 => "3RD", _ => $"{n}TH" };

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA POLE POSITION", new Point(cx, 160), 104, Red, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 232), 28, Dim, center: true);
        string[] lines =
        [
            "QUALIFY WITH ONE FAST LAP FOR A PLACE ON THE GRID",
            "THEN RACE FOUR LAPS BEFORE THE CLOCK RUNS OUT",
            "EVERY LAP COMPLETED BUYS MORE TIME",
            "MIND THE OTHER CARS AND THE BILLBOARDS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 310 + i * 38), 30, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 500), 48, Yellow, center: true);
        Text(dc, "LEFT / RIGHT: STEER   UP OR Z: ACCELERATE   DOWN OR X: BRAKE   SPACE: GEAR", new Point(cx, 560), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "FASTEST DRIVERS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   RACE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
