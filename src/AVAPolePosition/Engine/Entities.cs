namespace AVAPolePosition.Engine;

/// <summary>Steering, the accelerator, the brake, and the gear lever (a press changes gear).</summary>
public readonly record struct Controls(int Steer, bool Gas = false, bool Brake = false, bool Shift = false);

public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    /// <summary>Where the road meets the sky.</summary>
    public const double Horizon = 330;
}

public enum SpriteKind { Billboard, Tree, Chevron, Gantry, Bush, Stand, Light }

/// <summary>Something by the roadside: how far out (in road half-widths) and what it says.</summary>
public sealed record Sprite(SpriteKind Kind, double Offset, string Text = "", uint Colour = 0xFFFFFFFF);

public sealed class Segment
{
    public int Index;
    public double Curve;
    public List<Sprite> Sprites = [];
    /// <summary>Alternate bands of colour, as the road rushes past.</summary>
    public bool Light => Index / 3 % 2 == 0;
}

public sealed class Car
{
    public int Id;
    /// <summary>Distance from the start of the lap, and in all (for the standings).</summary>
    public double Z, Total;
    /// <summary>Across the road: -1 and 1 are its edges.</summary>
    public double X, TargetX;
    public double Speed, TopSpeed;
    public uint Colour;
    public int Number;
}

public enum EffectKind { Score, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
