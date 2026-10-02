using System;
using System.Numerics;

namespace CadFifa;

/// <summary>
/// How the footballers look and move, shared by both displays (the game window and the
/// in-drawing renderer) so they always match. Colours are plain RGB; sizes are metres.
/// </summary>
internal static class Look
{
    /// <summary>Figures are drawn a bit larger than their physics size so they read at a distance.</summary>
    public const double S = 1.7;
    public const double BallR = 0.65;

    public const double ShadowR = 0.75 * S, TorsoR = 0.55 * S, HairR = 0.27 * S, HeadR = 0.22 * S;
    public const double LegW = 0.3 * S, ArmW = 0.24 * S, ShoulderW = 0.6 * S;

    public static readonly (byte R, byte G, byte B) Shadow = (28, 82, 34);
    public static readonly (byte R, byte G, byte B) BallWhite = (250, 250, 250);
    public static readonly (byte R, byte G, byte B) BallPanel = (25, 25, 25);

    static readonly (byte R, byte G, byte B)[] Skins =
        { (255, 219, 172), (241, 194, 125), (224, 172, 105), (198, 134, 66), (141, 85, 36), (96, 60, 32) };
    static readonly (byte R, byte G, byte B)[] Hairs =
        { (30, 20, 15), (85, 55, 30), (215, 175, 90), (160, 70, 30), (12, 12, 12), (120, 90, 60) };

    static int Seed(Footballer p) => p.Number * 7 + (int)p.Side * 3;

    public static (byte R, byte G, byte B) Skin(Footballer p) => Skins[Seed(p) % Skins.Length];
    public static (byte R, byte G, byte B) Hair(Footballer p) => Hairs[(Seed(p) / 2) % Hairs.Length];

    public static (byte R, byte G, byte B) Shirt(Footballer p) => p.Side == Side.Home
        ? (p.IsKeeper ? ((byte)255, (byte)165, (byte)25) : ((byte)210, (byte)35, (byte)45))
        : (p.IsKeeper ? ((byte)150, (byte)60, (byte)190) : ((byte)35, (byte)90, (byte)210));

    public static (byte R, byte G, byte B) Socks(Footballer p) => p.IsKeeper ? Shirt(p)
        : p.Side == Side.Home ? ((byte)245, (byte)245, (byte)245) : ((byte)20, (byte)30, (byte)85);

    /// <summary>Keepers wear long sleeves; outfield players show bare arms.</summary>
    public static (byte R, byte G, byte B) Arms(Footballer p) => p.IsKeeper ? Shirt(p) : Skin(p);

    /// <summary>Where every body part is this frame, in pitch coordinates.</summary>
    public struct Body
    {
        public Vector2 Shadow, Torso, ShoulderL, ShoulderR, Hair, Head;
        public Vector2 LegL0, LegL1, LegR0, LegR1, ArmL0, ArmL1, ArmR0, ArmR1;
        public bool ShowLegs;
    }

    public static Body Pose(Footballer p)
    {
        var fwd = p.Facing;
        var side = new Vector2(-fwd.Y, fwd.X);
        var c = p.Pos;
        // Stride grows with speed; legs and arms swing in opposite phase like a real run.
        double stride = Math.Min(1.0, p.Vel.Length() / 7.0);
        double swing = Math.Sin(p.RunPhase) * stride;
        bool sliding = p.SlideTimer > 0f;
        bool down = p.StunTimer > 0f && !sliding;

        var b = new Body
        {
            Shadow = c + new Vector2((float)(0.2 * S), (float)(-0.2 * S)),
            Torso = c,
            ShoulderL = c + side * (float)(0.75 * S),
            ShoulderR = c - side * (float)(0.75 * S),
            Hair = c - fwd * (float)(0.06 * S),
            Head = c + fwd * (float)(0.06 * S),
            // A slide goes in feet first; a player who has been tackled lies flat with legs trailing.
            ShowLegs = sliding || down || stride > 0.05,
        };
        Limb(c, side, fwd, 0.22, sliding ? 1.2 : down ? -1.1 : 0.75 * swing, 0, out b.LegL0, out b.LegL1);
        Limb(c, side, fwd, -0.22, sliding ? 0.9 : down ? -1.1 : -0.75 * swing, 0, out b.LegR0, out b.LegR1);
        // Arms swing opposite to the legs, and are thrown back for balance in a slide.
        double spread = sliding ? 0.4 : 0.12;
        Limb(c, side, fwd, 0.68, sliding ? -0.8 : down ? 0.9 : -0.5 * swing, spread, out b.ArmL0, out b.ArmL1);
        Limb(c, side, fwd, -0.68, sliding ? -0.8 : down ? 0.9 : 0.5 * swing, spread, out b.ArmR0, out b.ArmR1);
        return b;
    }

    /// <summary>A limb rooted <paramref name="offset"/> to the side, reaching <paramref name="reach"/> forward or back.</summary>
    static void Limb(Vector2 c, Vector2 side, Vector2 fwd, double offset, double reach, double spread,
                     out Vector2 root, out Vector2 end)
    {
        if (Math.Abs(reach) < 0.05) reach = 0.05;
        root = c + side * (float)(offset * S);
        end = root + fwd * (float)(reach * S) + side * (float)(Math.Sign(offset) * spread * S);
    }

    /// <summary>The three dark panels on the ball, which orbit its centre as it rolls.</summary>
    public static (Vector2 Pos, bool Visible)[] BallPanels(Ball ball)
    {
        var roll = ball.Vel.LengthSquared() > 0.01f ? Vector2.Normalize(ball.Vel) : Vector2.UnitX;
        var side = new Vector2(-roll.Y, roll.X);
        var panels = new (Vector2, bool)[3];
        for (int i = 0; i < 3; i++)
        {
            double a = ball.Spin + i * 2.0 * Math.PI / 3.0;
            float along = (float)(Math.Sin(a) * 0.35), across = (i - 1) * 0.26f;
            panels[i] = (ball.Pos + roll * along + side * across, Math.Cos(a) > -0.3); // hide panels on the far side
        }
        return panels;
    }
}
