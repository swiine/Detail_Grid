using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CadFifa;

internal enum Side { Home = 0, Away = 1 }

internal enum Difficulty { Easy, Normal, Hard }

/// <summary>Solo: P1 vs CPU. Versus: P1 (red) vs P2 (blue). Coop: P1 and P2 together vs CPU.</summary>
internal enum GameMode { Solo, Versus, Coop }

/// <summary>A footballer. Positions are metres from the centre spot.</summary>
internal sealed class Footballer
{
    public Side Side;
    public int Number;
    public bool IsKeeper;
    /// <summary>Formation slot in team-local coordinates (the team always attacks +X locally).</summary>
    public Vector2 Slot;
    public Vector2 Pos;
    public Vector2 Vel;
    public Vector2 Facing = Vector2.UnitX;
    public float KickCooldown;
    public float TackleCooldown;
    public float StunTimer;
    public float HoldTimer;
    public float DecisionTimer;
    /// <summary>Running animation phase in radians; advances with distance covered.</summary>
    public float RunPhase;
    /// <summary>Time left in a slide tackle; while positive the player skids along <see cref="SlideDir"/>.</summary>
    public float SlideTimer;
    public Vector2 SlideDir;
    /// <summary>Whether the current slide has already won (or touched) the ball.</summary>
    public bool SlideConnected;
}

internal sealed class Ball
{
    public Vector2 Pos;
    public Vector2 Vel;
    public Footballer? Owner;
    public Side LastTouch;
    /// <summary>Roll angle in radians, for drawing the panels spinning.</summary>
    public float Spin;
}

/// <summary>One human player and the footballer they currently control.</summary>
internal sealed class Controller
{
    public int Index;   // 0 = P1, 1 = P2
    public Side Side;
    public Footballer Player = null!;
    internal float ChargeTime;
    internal bool PrevShootHeld;
    /// <summary>The shoot key went down to start a tackle, so letting go must not shoot.</summary>
    internal bool PressUsedForTackle;
    /// <summary>0..1 shot power while the shoot key is held.</summary>
    public float Charge => Math.Min(1f, ChargeTime / 0.8f);
}

/// <summary>What one human is pressing this frame.</summary>
internal sealed class PadState
{
    public Vector2 Move;
    public bool Sprint;
    public bool ShootHeld;
    public bool PassPressed;
    public bool SwitchPressed;
}

/// <summary>
/// Pure simulation of an 11-a-side match. Knows nothing about AutoCAD so it can be
/// stepped from any loop. Home attacks +X; humans drive the players their
/// <see cref="Controller"/>s point at and the AI runs everyone else.
/// </summary>
internal sealed class Match
{
    // FIFA-standard pitch, metres.
    public const float HalfLength = 52.5f;
    public const float HalfWidth = 34f;
    /// <summary>Half the goal mouth: twice a regulation goal (7.32 m) for a more open, arcade game.</summary>
    public const float GoalHalfWidth = 7.32f;
    public const float PenaltyDepth = 16.5f;
    public const float PenaltyHalfWidth = 20.16f;

    public const float PlayerRadius = 0.9f;
    public const float BallRadius = 0.45f;

    const float RunSpeed = 6.8f;
    const float SprintSpeed = 9.0f;
    const float DribbleSpeed = 6.2f;
    const float DribbleSprintSpeed = 8.0f;
    const float ControlReach = 1.3f;
    const float SlideSpeed = 11f;
    const float SlideTime = 0.45f;
    const float SlideReach = 1.8f;
    /// <summary>
    /// Players can't overlap (centres stay 2 x PlayerRadius apart), so a slide also connects
    /// when it reaches the carrier's body; otherwise tackles from behind could never get to
    /// the ball, which sits in front of the carrier.
    /// </summary>
    const float SlideBodyReach = PlayerRadius * 2f + 0.5f;
    const float KeeperReach = 1.9f;

    static readonly Vector2[] Formation442 =
    {
        new(-50f,   0f),                                                    // GK
        new(-36f, -21f), new(-38f,  -7f), new(-38f,   7f), new(-36f,  21f), // back four
        new(-20f, -22f), new(-22f,  -7f), new(-22f,   7f), new(-20f,  22f), // midfield
        new( -5f,  -9f), new( -5f,   9f),                                   // strikers
    };

    readonly Random _rng = new();
    readonly float _aiSkill;
    readonly float _matchSeconds;
    readonly HashSet<Footballer> _human = new();
    float _freeze;

    public List<Footballer> Players { get; } = new();
    public Ball Ball { get; } = new();
    public GameMode Mode { get; }
    public List<Controller> Controllers { get; } = new();
    public int[] Score { get; } = new int[2];
    public float Elapsed { get; private set; }
    public bool FullTime { get; private set; }
    public string Message { get; private set; } = "";
    public float MessageTimer { get; private set; }

    public Match(GameMode mode, Difficulty difficulty, float matchMinutes = 4f)
    {
        Mode = mode;
        Controllers.Add(new Controller { Index = 0, Side = Side.Home });
        if (mode != GameMode.Solo)
            Controllers.Add(new Controller { Index = 1, Side = mode == GameMode.Versus ? Side.Away : Side.Home });

        _aiSkill = difficulty switch { Difficulty.Easy => 0.8f, Difficulty.Hard => 1.08f, _ => 0.94f };
        _matchSeconds = matchMinutes * 60f;

        foreach (Side side in new[] { Side.Home, Side.Away })
        {
            for (int i = 0; i < Formation442.Length; i++)
            {
                Players.Add(new Footballer
                {
                    Side = side,
                    Number = i + 1,
                    IsKeeper = i == 0,
                    Slot = Formation442[i],
                });
            }
        }
        Restart();
    }

    /// <summary>Displayed match minute, 0–90.</summary>
    public int MatchMinute => (int)Math.Min(90f, Elapsed / _matchSeconds * 90f);

    static float Dir(Side s) => s == Side.Home ? 1f : -1f;
    static Side Other(Side s) => s == Side.Home ? Side.Away : Side.Home;
    static Vector2 GoalOf(Side attacking) => new(Dir(attacking) * HalfLength, 0f);
    static Vector2 World(Footballer p, Vector2 local) => local * Dir(p.Side);

    IEnumerable<Footballer> Team(Side s) => Players.Where(p => p.Side == s);

    bool HasHuman(Side s) => Controllers.Any(c => c.Side == s);

    /// <summary>Nearest outfield player to a point that no other controller is using.</summary>
    Footballer NearestFree(Controller c, Vector2 to, Footballer? except = null) =>
        Team(c.Side)
            .Where(p => !p.IsKeeper && p != except && !Controllers.Any(o => o != c && o.Player == p))
            .OrderBy(p => Vector2.DistanceSquared(p.Pos, to))
            .First();

    void Say(string text, float seconds)
    {
        Message = text;
        MessageTimer = seconds;
    }

    void KickOff(Side kicking)
    {
        foreach (var p in Players)
        {
            p.Pos = World(p, p.Slot);
            p.Vel = Vector2.Zero;
            p.Facing = new Vector2(Dir(p.Side), 0f);
            p.StunTimer = p.KickCooldown = p.TackleCooldown = p.HoldTimer = p.SlideTimer = 0f;
        }
        var taker = Team(kicking).First(p => p.Number == 11);
        taker.Pos = new Vector2(-Dir(kicking) * (PlayerRadius + BallRadius), 0f);
        Ball.Pos = Vector2.Zero;
        Ball.Vel = Vector2.Zero;
        Ball.Owner = taker;
        Ball.LastTouch = kicking;
        foreach (var side in new[] { Side.Home, Side.Away })
        {
            int n = 11;
            foreach (var c in Controllers.Where(c => c.Side == side))
                c.Player = Team(side).First(p => p.Number == n--);
        }
        _freeze = 1.5f;
    }

    public void Restart()
    {
        Score[0] = Score[1] = 0;
        Elapsed = 0f;
        FullTime = false;
        KickOff(Side.Home);
        Say("KICK OFF", 1.5f);
    }

    /// <param name="pads">Input for each controller, indexed by <see cref="Controller.Index"/>.</param>
    public void Step(float dt, IReadOnlyList<PadState> pads)
    {
        if (MessageTimer > 0f) MessageTimer -= dt;
        if (FullTime) return;
        if (_freeze > 0f)
        {
            _freeze -= dt;
            foreach (var c in Controllers)
            {
                c.PrevShootHeld = pads[c.Index].ShootHeld;
                c.ChargeTime = 0f;
            }
            return;
        }

        Elapsed += dt;
        if (Elapsed >= _matchSeconds)
        {
            FullTime = true;
            string result = Mode == GameMode.Versus
                ? (Score[0] > Score[1] ? "P1 WINS!" : Score[0] < Score[1] ? "P2 WINS!" : "DRAW")
                : (Score[0] > Score[1] ? "YOU WIN!" : Score[0] < Score[1] ? "YOU LOSE" : "DRAW");
            Say($"FULL TIME  {Score[0]} - {Score[1]}  {result}  (R = rematch)", float.MaxValue);
            return;
        }

        foreach (var p in Players)
        {
            p.KickCooldown = Math.Max(0f, p.KickCooldown - dt);
            p.TackleCooldown = Math.Max(0f, p.TackleCooldown - dt);
            p.StunTimer = Math.Max(0f, p.StunTimer - dt);
            p.DecisionTimer = Math.Max(0f, p.DecisionTimer - dt);
        }

        foreach (var c in Controllers)
            HandleHuman(c, dt, pads[c.Index]);
        _human.Clear();
        foreach (var c in Controllers) _human.Add(c.Player);
        foreach (var p in Players)
            if (!_human.Contains(p)) Think(p, dt);
        foreach (var p in Players)
            if (p.SlideTimer > 0f) Slide(p, dt);

        foreach (var p in Players)
        {
            p.Pos += p.Vel * dt;
            p.RunPhase = (p.RunPhase + p.Vel.Length() * dt * 1.7f) % (2f * (float)Math.PI);
            p.Pos = new Vector2(
                Clamp(p.Pos.X, -HalfLength + 1f, HalfLength - 1f),
                Clamp(p.Pos.Y, -HalfWidth + 1f, HalfWidth - 1f));
            if (p.Vel.LengthSquared() > 0.09f) p.Facing = TurnToward(p.Facing, Vector2.Normalize(p.Vel), dt);
        }
        Separate();
        StepBall(dt);
    }

    // ---------------------------------------------------------------- human

    void HandleHuman(Controller c, float dt, PadState pad)
    {
        if (pad.SwitchPressed && Ball.Owner != c.Player)
            c.Player = NearestFree(c, Ball.Pos, except: c.Player);
        var me = c.Player;

        bool hasBall = Ball.Owner == me;
        bool pressed = pad.ShootHeld && !c.PrevShootHeld;
        if (pressed && !hasBall)
        {
            // Without the ball, the shoot key is an instant slide tackle.
            StartSlide(me, pad.Move);
            c.PressUsedForTackle = true;
        }
        float speed = hasBall
            ? (pad.Sprint ? DribbleSprintSpeed : DribbleSpeed)
            : (pad.Sprint ? SprintSpeed : RunSpeed);
        var move = pad.Move.LengthSquared() > 0f ? Vector2.Normalize(pad.Move) : Vector2.Zero;
        if (me.StunTimer > 0f) move = Vector2.Zero;
        if (me.SlideTimer <= 0f) me.Vel = Approach(me.Vel, move * speed, 40f * dt);

        if (pad.ShootHeld && !c.PressUsedForTackle) c.ChargeTime += dt;
        bool released = c.PrevShootHeld && !pad.ShootHeld;
        c.PrevShootHeld = pad.ShootHeld;

        if (released)
        {
            // Charge is still valid here because it is only reset below. A press that
            // started a tackle never turns into a shot, even if the slide won the ball.
            if (hasBall && !c.PressUsedForTackle) Shoot(me, c.Charge, pad.Move.Y * GoalHalfWidth * 0.8f);
            c.PressUsedForTackle = false;
        }
        if (!pad.ShootHeld) c.ChargeTime = 0f;

        if (pad.PassPressed && Ball.Owner == me)
        {
            var target = PickPassTarget(me, me.Facing);
            if (target != null) Pass(me, target);
        }
    }

    // ---------------------------------------------------------------- AI

    void Think(Footballer p, float dt)
    {
        if (p.StunTimer > 0f)
        {
            p.Vel = Approach(p.Vel, Vector2.Zero, 30f * dt);
            return;
        }
        // AI teammates of a human are a touch weaker than a CPU-only side.
        float skill = HasHuman(p.Side) ? 0.88f : _aiSkill;
        if (p.IsKeeper) { ThinkKeeper(p, dt, skill); return; }

        if (Ball.Owner == p) { ThinkOnBall(p, dt, skill); return; }

        bool teamHasBall = Ball.Owner?.Side == p.Side;
        var chaser = Team(p.Side)
            .Where(q => !q.IsKeeper && !_human.Contains(q))
            .OrderBy(q => Vector2.DistanceSquared(q.Pos, Ball.Pos))
            .First();

        if (!teamHasBall && p == chaser)
        {
            var intercept = Ball.Pos + Ball.Vel * 0.35f;
            SteerTo(p, intercept, RunSpeed * 1.1f * skill, dt);
            if (Ball.Owner != null)
                TryTackle(p, reach: ControlReach + 0.2f, chance: 0.4f * skill);
            return;
        }

        // Hold shape, sliding with the ball and pushing up when in possession.
        float dir = Dir(p.Side);
        var home = World(p, p.Slot);
        float push = teamHasBall ? 9f : -2f;
        var target = new Vector2(
            home.X * 0.55f + Ball.Pos.X * 0.5f + dir * push,
            home.Y * 0.85f + Ball.Pos.Y * 0.25f);
        if (teamHasBall && p.Number >= 10)
        {
            // Strikers lead the line on the edge of the box, drifting with the ball.
            target = new Vector2(dir * (HalfLength - 15f), home.Y * 0.8f + Ball.Pos.Y * 0.3f);
        }
        target.X = Clamp(target.X, -HalfLength + 3f, HalfLength - 3f);
        SteerTo(p, target, RunSpeed * 0.85f * skill, dt);
    }

    void ThinkOnBall(Footballer p, float dt, float skill)
    {
        var goal = GoalOf(p.Side);
        float distGoal = Vector2.Distance(p.Pos, goal);
        var nearestOpp = Team(Other(p.Side)).OrderBy(q => Vector2.DistanceSquared(q.Pos, p.Pos)).First();
        float pressure = Vector2.Distance(nearestOpp.Pos, p.Pos);

        if (p.DecisionTimer <= 0f)
        {
            p.DecisionTimer = 0.35f;
            if (distGoal < 27f && _rng.NextDouble() < (distGoal < 18f ? 0.8 : 0.4))
            {
                Shoot(p, 0.75f + (float)_rng.NextDouble() * 0.25f, ((float)_rng.NextDouble() * 2f - 1f) * (GoalHalfWidth - 1f));
                return;
            }
            // Pass when pressed, and now and then just to move the ball forward.
            double passChance = pressure < 5f ? 0.7 : 0.2;
            if (_rng.NextDouble() < passChance)
            {
                var mate = PickPassTarget(p, Vector2.Normalize(goal - p.Pos));
                bool forward = mate != null && Vector2.Distance(mate.Pos, goal) < distGoal - 3f;
                if (mate != null && LaneClear(p.Side, p.Pos, mate.Pos) && (pressure < 5f || (forward && Openness(mate) > 5f)))
                {
                    Pass(p, mate);
                    return;
                }
            }
        }

        // Dribble at goal, veering away from the nearest defender.
        var heading = Vector2.Normalize(goal - p.Pos);
        var away = p.Pos - nearestOpp.Pos;
        if (pressure < 6f && away.LengthSquared() > 0.01f)
        {
            var side = new Vector2(-heading.Y, heading.X);
            float sign = Vector2.Dot(side, away) >= 0f ? 1f : -1f;
            heading = Vector2.Normalize(heading + side * sign * (6f - pressure) / 4f);
        }
        float speed = pressure > 5f ? DribbleSprintSpeed : DribbleSpeed; // run into space
        p.Vel = Approach(p.Vel, heading * speed * skill, 30f * dt);
    }

    void ThinkKeeper(Footballer p, float dt, float skill)
    {
        float dir = Dir(p.Side);
        if (Ball.Owner == p)
        {
            p.Vel = Vector2.Zero;
            p.HoldTimer += dt;
            if (p.HoldTimer > 1.0f)
            {
                p.HoldTimer = 0f;
                var mate = Team(p.Side).Where(q => !q.IsKeeper)
                    .OrderByDescending(q => Openness(q) + (float)_rng.NextDouble() * 4f)
                    .First();
                Pass(p, mate);
            }
            return;
        }

        float goalX = -dir * HalfLength;
        bool danger = Ball.Owner?.Side != p.Side
                      && Math.Abs(Ball.Pos.X - goalX) < PenaltyDepth - 2f
                      && Math.Abs(Ball.Pos.Y) < PenaltyHalfWidth - 4f;
        Vector2 target = danger && Ball.Owner == null
            ? Ball.Pos + Ball.Vel * 0.15f
            : new Vector2(goalX + dir * 1.5f, Clamp(Ball.Pos.Y * 0.35f, -GoalHalfWidth + 0.6f, GoalHalfWidth - 0.6f));
        SteerTo(p, target, RunSpeed * skill, dt);
        if (danger && Ball.Owner != null)
            TryTackle(p, reach: KeeperReach, chance: 0.5f);
    }

    // ---------------------------------------------------------------- actions

    void Pass(Footballer from, Footballer to)
    {
        var lead = to.Pos + to.Vel * 0.5f;
        var delta = lead - Ball.Pos;
        float dist = delta.Length();
        if (dist < 0.01f) return;
        float speed = Clamp(9f + dist * 0.75f, 11f, 30f);
        Release(from, Vector2.Normalize(delta) * speed);
        // Pass and follow: the passer's controller moves on to the receiver.
        var passer = Controllers.FirstOrDefault(c => c.Player == from);
        if (passer != null && !Controllers.Any(c => c.Player == to)) passer.Player = to;
    }

    void Shoot(Footballer p, float power, float aimY)
    {
        var goal = GoalOf(p.Side);
        float dist = Vector2.Distance(p.Pos, goal);
        float spread = 0.6f + dist * 0.05f + power * 1.2f;
        var target = new Vector2(goal.X,
            Clamp(aimY, -GoalHalfWidth - 1f, GoalHalfWidth + 1f)
            + ((float)_rng.NextDouble() * 2f - 1f) * spread);
        float speed = 15f + power * 17f;
        Release(p, Vector2.Normalize(target - Ball.Pos) * speed);
    }

    void Release(Footballer p, Vector2 velocity)
    {
        Ball.Owner = null;
        Ball.Vel = velocity;
        Ball.LastTouch = p.Side;
        p.KickCooldown = 0.35f;
    }

    void StartSlide(Footballer p, Vector2 aim)
    {
        if (p.SlideTimer > 0f || p.StunTimer > 0f || p.TackleCooldown > 0f) return;
        // Slide where the stick points; with no direction held, go at where the ball is heading.
        var dir = aim.LengthSquared() > 0f ? aim : Ball.Pos + Ball.Vel * 0.25f - p.Pos;
        if (dir.LengthSquared() < 1e-4f) dir = p.Facing;
        p.SlideDir = Vector2.Normalize(dir);
        p.Facing = p.SlideDir;
        p.SlideTimer = SlideTime;
        p.SlideConnected = false;
        p.TackleCooldown = SlideTime + 0.4f;
    }

    void Slide(Footballer p, float dt)
    {
        // Skid fast at first, slowing as the slide runs out.
        p.Vel = p.SlideDir * SlideSpeed * (0.35f + 0.65f * p.SlideTimer / SlideTime);
        p.Facing = p.SlideDir;
        p.SlideTimer -= dt;

        var owner = Ball.Owner;
        bool touchesBall = Vector2.Distance(p.Pos, Ball.Pos) < SlideReach;
        bool touchesCarrier = owner != null && Vector2.Distance(p.Pos, owner.Pos) < SlideBodyReach;
        if (!p.SlideConnected && (touchesBall || touchesCarrier))
        {
            if (owner == null)
            {
                p.SlideConnected = true;
                TakeBall(p);
            }
            else if (owner.Side != p.Side)
            {
                p.SlideConnected = true;
                // Clean tackles from in front or the side nearly always work; from behind is riskier.
                var toTackler = Vector2.Normalize(p.Pos - owner.Pos + new Vector2(1e-3f, 0f));
                bool fromBehind = Vector2.Dot(toTackler, owner.Facing) < -0.5f;
                if (_rng.NextDouble() < (fromBehind ? 0.55 : 0.85))
                {
                    owner.StunTimer = 0.9f;
                    owner.KickCooldown = 0.6f;
                    TakeBall(p);
                    Say("TACKLE!", 0.8f);
                }
            }
        }

        if (p.SlideTimer <= 0f)
        {
            p.SlideTimer = 0f;
            // Winning the ball means springing straight up; a miss leaves you on the floor.
            if (Ball.Owner != p) p.StunTimer = 0.5f;
            p.Vel *= 0.3f;
        }
    }

    void TryTackle(Footballer p, float reach, float chance)
    {
        var owner = Ball.Owner;
        if (owner == null || owner.Side == p.Side || p.TackleCooldown > 0f) return;
        if (Vector2.Distance(p.Pos, Ball.Pos) > reach) return;
        p.TackleCooldown = 0.6f;
        if (_rng.NextDouble() < chance)
        {
            owner.StunTimer = 0.7f;
            owner.KickCooldown = 0.5f;
            TakeBall(p);
        }
    }

    void TakeBall(Footballer p)
    {
        Ball.Owner = p;
        Ball.LastTouch = p.Side;
        p.HoldTimer = 0f;
        p.DecisionTimer = 0.4f;

        // The attacking side's nearest controller takes over the new ball carrier.
        if (!p.IsKeeper && !Controllers.Any(c => c.Player == p))
        {
            var taker = Controllers.Where(c => c.Side == p.Side)
                .OrderBy(c => Vector2.DistanceSquared(c.Player.Pos, p.Pos)).FirstOrDefault();
            if (taker != null) taker.Player = p;
        }
        // Defending: the opposing controller nearest the ball jumps to the closest defender.
        var defender = Controllers.Where(c => c.Side != p.Side)
            .OrderBy(c => Vector2.DistanceSquared(c.Player.Pos, Ball.Pos)).FirstOrDefault();
        if (defender != null) defender.Player = NearestFree(defender, Ball.Pos);
    }

    Footballer? PickPassTarget(Footballer from, Vector2 aim)
    {
        Footballer? best = null;
        float bestScore = float.MinValue;
        foreach (var q in Team(from.Side))
        {
            if (q == from || q.IsKeeper) continue;
            var d = q.Pos - from.Pos;
            float dist = d.Length();
            if (dist < 4f || dist > 45f) continue;
            float align = Vector2.Dot(Vector2.Normalize(d), aim);
            float score = align * 3f - dist / 25f + Math.Min(Openness(q), 8f) / 4f;
            if (!LaneClear(from.Side, from.Pos, q.Pos)) score -= 3f;
            if (score > bestScore) { bestScore = score; best = q; }
        }
        return best;
    }

    /// <summary>True when no opponent stands close to the straight line between two points.</summary>
    bool LaneClear(Side side, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        foreach (var o in Team(Other(side)))
        {
            float t = Clamp(Vector2.Dot(o.Pos - a, ab) / lenSq, 0f, 1f);
            if (Vector2.Distance(a + ab * t, o.Pos) < 2.2f) return false;
        }
        return true;
    }

    /// <summary>Distance from a player to the closest opponent.</summary>
    float Openness(Footballer q) =>
        Team(Other(q.Side)).Min(o => Vector2.Distance(o.Pos, q.Pos));

    // ---------------------------------------------------------------- physics

    void StepBall(float dt)
    {
        if (Ball.Owner is { } owner)
        {
            Ball.Pos = owner.Pos + owner.Facing * (PlayerRadius + BallRadius);
            Ball.Vel = owner.Vel;
            Ball.Spin += owner.Vel.Length() * dt / BallRadius * 0.25f;
            return;
        }

        Ball.Pos += Ball.Vel * dt;
        Ball.Spin += Ball.Vel.Length() * dt / BallRadius * 0.25f;
        Ball.Vel *= (float)Math.Exp(-0.8f * dt);
        if (Ball.Vel.LengthSquared() < 0.04f) Ball.Vel = Vector2.Zero;

        if (Math.Abs(Ball.Pos.X) > HalfLength || Math.Abs(Ball.Pos.Y) > HalfWidth)
        {
            OutOfPlay();
            return;
        }

        float speed = Ball.Vel.Length();
        foreach (var p in Players.OrderBy(p => Vector2.DistanceSquared(p.Pos, Ball.Pos)))
        {
            if (p.KickCooldown > 0f || p.StunTimer > 0f) continue;
            float d = Vector2.Distance(p.Pos, Ball.Pos);
            if (p.IsKeeper && d < KeeperReach && InOwnBox(p))
            {
                float saveChance = speed < 12f ? 1f : 0.8f - (speed - 12f) * 0.03f;
                if (_rng.NextDouble() < saveChance)
                {
                    TakeBall(p);
                    Ball.Vel = Vector2.Zero;
                }
                else
                {
                    // Parried away.
                    Ball.Vel = new Vector2(-Ball.Vel.X * 0.35f,
                        Ball.Vel.Y * 0.35f + ((float)_rng.NextDouble() - 0.5f) * 12f);
                    p.KickCooldown = 0.6f;
                    Say("SAVE!", 1f);
                }
                return;
            }
            if (d < ControlReach && speed < 24f)
            {
                TakeBall(p);
                return;
            }
        }
    }

    bool InOwnBox(Footballer p)
    {
        float goalX = -Dir(p.Side) * HalfLength;
        return Math.Abs(Ball.Pos.X - goalX) < PenaltyDepth && Math.Abs(Ball.Pos.Y) < PenaltyHalfWidth;
    }

    void OutOfPlay()
    {
        var pos = Ball.Pos;
        if (Math.Abs(pos.X) > HalfLength && Math.Abs(pos.Y) < GoalHalfWidth)
        {
            var scorer = pos.X > 0f ? Side.Home : Side.Away;
            Score[(int)scorer]++;
            Say(Mode == GameMode.Versus
                    ? (scorer == Side.Home ? "P1 SCORES!" : "P2 SCORES!")
                    : (scorer == Side.Home ? "GOOOAL!" : "GOAL - AWAY"), 2.5f);
            KickOff(Other(scorer));
            _freeze = 2.5f;
            return;
        }

        var restartSide = Other(Ball.LastTouch);
        Vector2 spot;
        if (Math.Abs(pos.X) > HalfLength)
        {
            float sx = Math.Sign(pos.X);
            // Home defends the -X goal line.
            var defending = sx < 0f ? Side.Home : Side.Away;
            if (restartSide == defending)
            {
                spot = new Vector2(sx * (HalfLength - 5.5f), 0f);
                Say("GOAL KICK", 1f);
            }
            else
            {
                spot = new Vector2(sx * (HalfLength - 0.5f), Math.Sign(pos.Y) * (HalfWidth - 0.5f));
                Say("CORNER", 1f);
            }
        }
        else
        {
            spot = new Vector2(Clamp(pos.X, -HalfLength + 1f, HalfLength - 1f),
                Math.Sign(pos.Y) * (HalfWidth - 0.5f));
            Say("THROW-IN", 1f);
        }

        var taker = Team(restartSide).Where(p => !p.IsKeeper)
            .OrderBy(p => Vector2.DistanceSquared(p.Pos, spot)).First();
        // Stand behind the ball, facing into the pitch.
        var inward = Vector2.Normalize(new Vector2(-spot.X, -spot.Y + 0.001f));
        taker.Pos = spot - inward * (PlayerRadius + BallRadius);
        taker.Vel = Vector2.Zero;
        taker.Facing = inward;
        foreach (var p in Team(Other(restartSide)))
        {
            var off = p.Pos - spot;
            if (off.Length() < 9f)
                p.Pos = spot + Vector2.Normalize(off + new Vector2(0.01f, 0.01f)) * 9f;
        }
        Ball.Pos = spot;
        Ball.Vel = Vector2.Zero;
        TakeBall(taker);
        _freeze = 1f;
    }

    void Separate()
    {
        const float min = PlayerRadius * 2f;
        for (int i = 0; i < Players.Count; i++)
        for (int j = i + 1; j < Players.Count; j++)
        {
            var a = Players[i];
            var b = Players[j];
            var d = b.Pos - a.Pos;
            float len = d.Length();
            if (len >= min || len < 1e-4f) continue;
            var push = d / len * (min - len) * 0.5f;
            a.Pos -= push;
            b.Pos += push;
        }
    }

    // ---------------------------------------------------------------- helpers

    static void SteerTo(Footballer p, Vector2 target, float maxSpeed, float dt)
    {
        var delta = target - p.Pos;
        float dist = delta.Length();
        var desired = dist < 0.5f ? Vector2.Zero : delta / dist * Math.Min(maxSpeed, dist * 2f);
        p.Vel = Approach(p.Vel, desired, 25f * dt);
    }

    // Math.Clamp is not available on .NET Framework 4.8 (AutoCAD 2024 and older).
    static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

    /// <summary>
    /// Rotate a facing direction smoothly toward a target, so players turn rather than snap
    /// (snapping made figures and the carried ball jump around on screen).
    /// </summary>
    static Vector2 TurnToward(Vector2 facing, Vector2 target, float dt)
    {
        var blended = facing + (target - facing) * Math.Min(1f, dt * 14f);
        return blended.LengthSquared() < 1e-4f ? target : Vector2.Normalize(blended);
    }

    static Vector2 Approach(Vector2 current, Vector2 target, float maxDelta)
    {
        var d = target - current;
        float len = d.Length();
        return len <= maxDelta || len < 1e-5f ? target : current + d / len * maxDelta;
    }
}
