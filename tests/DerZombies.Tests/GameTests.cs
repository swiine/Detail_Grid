using DerZombies.Core;
using Xunit;

namespace DerZombies.Tests;

public class MapTests
{
    [Fact]
    public void Layout_HasAllAreasAndDoors()
    {
        var map = GameMap.CreateDefault();
        Assert.Equal(60, map.Cols);
        Assert.Equal(40, map.Rows);
        Assert.Equal(MapData.AreaNames.Count, map.Areas.Count);
        Assert.Equal(MapData.DoorCosts.Count, map.Doors.Count);
        foreach (var door in map.Doors.Values)
            Assert.True(door.Areas.Count == 2, $"Door '{door.Id}' should join exactly two areas, joins {door.Areas.Count}");
    }

    [Fact]
    public void Features_AreOnFloorCells()
    {
        var map = GameMap.CreateDefault();
        foreach (var f in MapData.Features)
            Assert.True(GameMap.IsAreaChar(map.CharAt(f.Col, f.Row)), $"{f.Kind} {f.Key} at ({f.Col},{f.Row}) is not on a floor cell");
    }

    [Fact]
    public void EveryArea_IsReachable_WhenAllDoorsAreOpen()
    {
        var map = GameMap.CreateDefault();
        foreach (var id in map.Doors.Keys.ToList()) map.OpenDoor(id);
        var start = MapData.Features.Single(f => f.Kind == FeatureKind.PlayerStart);
        map.UpdateFlowField(map.CellCenter(start.Col, start.Row));
        foreach (var area in map.Areas.Values)
        foreach (var (c, r) in area.Cells)
            Assert.NotEqual(GameMap.Unreachable, map.FlowDistance(map.CellCenter(c, r)));
    }

    [Fact]
    public void ClosedDoors_BlockPathing()
    {
        var map = GameMap.CreateDefault();
        var start = MapData.Features.Single(f => f.Kind == FeatureKind.PlayerStart);
        map.UpdateFlowField(map.CellCenter(start.Col, start.Row));
        var lower = map.Areas['L'].Cells[0];
        Assert.Equal(GameMap.Unreachable, map.FlowDistance(map.CellCenter(lower.Col, lower.Row)));

        map.OpenDoor('1');
        map.UpdateFlowField(map.CellCenter(start.Col, start.Row));
        Assert.NotEqual(GameMap.Unreachable, map.FlowDistance(map.CellCenter(lower.Col, lower.Row)));
    }

    [Fact]
    public void WallSegments_AreAxisAligned()
    {
        var segs = GameMap.CreateDefault().WallSegments();
        Assert.NotEmpty(segs);
        Assert.All(segs, s => Assert.True(s.A.X == s.B.X || s.A.Y == s.B.Y));
    }
}

public class GameplayTests
{
    private static void Run(Game g, InputState input, double seconds)
    {
        for (double t = 0; t < seconds; t += 1.0 / 30) g.Update(1.0 / 30, input);
    }

    /// <summary>Presses then releases a button so the game sees one press.</summary>
    private static void Tap(Game g, Func<InputState, InputState> set)
    {
        g.Update(1.0 / 30, set(new InputState()));
        g.Update(1.0 / 30, new InputState());
    }

    private static InputState E(InputState i) { i.Interact = true; return i; }

    private static Vec2 Feature(Game g, FeatureKind kind, string key = "")
    {
        var f = MapData.Features.First(x => x.Kind == kind && (key == "" || x.Key == key));
        return g.Map.CellCenter(f.Col, f.Row);
    }

    [Fact]
    public void NewGame_StartsWithMr6And500Points()
    {
        var g = new Game(seed: 1);
        Assert.Equal(500, g.Player.Points);
        Assert.Equal("MR6", g.Player.Current.Name);
        Run(g, new InputState(), 5);
        Assert.Equal(1, g.Round);
    }

    [Fact]
    public void BuyingADoor_OpensTheArea()
    {
        var g = new Game(seed: 1);
        g.Player.Pos = g.Map.Doors['1'].Center + new Vec2(4, 0);
        g.GivePoints(1000);
        Tap(g, E);
        Assert.True(g.Map.Doors['1'].Open);
        Assert.True(g.Map.Areas['L'].Open);
        Assert.Equal(750, g.Player.Points);
        Assert.Contains(g.DrainEvents(), e => e.Type == GameEventType.DoorOpened && e.Data == "1");
    }

    [Fact]
    public void PerksNeedPower_ExceptQuickRevive()
    {
        var g = new Game(seed: 1);
        g.GivePoints(20000);
        g.Player.Pos = Feature(g, FeatureKind.Perk, nameof(Perk.Juggernog));
        Tap(g, E);
        Assert.DoesNotContain(Perk.Juggernog, g.Player.Perks);

        g.Player.Pos = Feature(g, FeatureKind.Perk, nameof(Perk.QuickRevive));
        Tap(g, E);
        Assert.Contains(Perk.QuickRevive, g.Player.Perks);

        g.Player.Pos = Feature(g, FeatureKind.PowerSwitch);
        Tap(g, E);
        Assert.True(g.PowerOn);

        g.Player.Pos = Feature(g, FeatureKind.Perk, nameof(Perk.Juggernog));
        Tap(g, E);
        Assert.Contains(Perk.Juggernog, g.Player.Perks);
        Assert.Equal(250, g.Player.MaxHealth);
    }

    [Fact]
    public void LandingPads_UnlockPackAPunch()
    {
        var g = new Game(seed: 1);
        g.GivePoints(10000);
        g.Player.Pos = Feature(g, FeatureKind.PowerSwitch);
        Tap(g, E);
        foreach (var pad in g.Pads)
        {
            g.Player.Pos = pad.Pos;
            Tap(g, E);
        }
        Assert.True(g.PackAPunchReady);

        g.Player.Pos = Feature(g, FeatureKind.PackAPunch);
        Tap(g, E);
        Assert.True(g.Player.Current.Packed);
        Assert.Equal(150, g.Player.Current.Damage);
    }

    [Fact]
    public void WallBuy_AddsSecondWeapon_ThenReplacesCurrent()
    {
        var g = new Game(seed: 1);
        g.GivePoints(5000);
        g.Player.Pos = Feature(g, FeatureKind.WallBuy, "RK5");
        Tap(g, E);
        Assert.Equal(2, g.Player.Weapons.Count);
        Assert.Equal("RK5", g.Player.Current.Name);

        g.Player.Pos = Feature(g, FeatureKind.WallBuy, "Sheiva");
        Tap(g, E);
        Assert.Equal(2, g.Player.Weapons.Count);
        Assert.Equal("Sheiva", g.Player.Current.Name);
        Assert.Equal("MR6", g.Player.Weapons[0].Name);
    }

    [Fact]
    public void MysteryBox_OffersAWeapon()
    {
        var g = new Game(seed: 3);
        g.GivePoints(5000);
        g.Player.Pos = g.BoxPos;
        Tap(g, E);
        Run(g, new InputState(), 3);
        Assert.Equal(BoxState.Offering, g.BoxState);
        Tap(g, E);
        Assert.Equal(2, g.Player.Weapons.Count);
        Assert.True(g.Player.Current.Def.InBox);
    }

    [Fact]
    public void FeedingDragons_UnlocksTheBow_AndAltarsUpgradeIt()
    {
        var g = new Game(seed: 1);
        foreach (var d in g.Dragons) d.Souls = Dragon.SoulsNeeded;
        Assert.True(g.BowReady);

        g.Player.Pos = Feature(g, FeatureKind.BowPedestal);
        Tap(g, E);
        Assert.Equal(WeaponKind.Bow, g.Player.Current.Def.Kind);

        g.Player.Pos = Feature(g, FeatureKind.BowAltar, nameof(BowVariant.Storm));
        Tap(g, E);
        Assert.Equal(BowVariant.Base, g.Player.Current.Bow); // needs 10 bow kills first

        g.Player.BowKills = 10;
        Tap(g, E);
        Assert.Equal(BowVariant.Storm, g.Player.Current.Bow);
    }

    [Fact]
    public void Zombies_KillAnIdlePlayer()
    {
        var g = new Game(seed: 2);
        Run(g, new InputState(), 120);
        Assert.True(g.IsGameOver);
        Assert.Contains(g.DrainEvents(), e => e.Type == GameEventType.GameOver);
    }

    [Fact]
    public void ShootingBot_SurvivesEarlyRounds()
    {
        var g = new Game(seed: 7);
        var home = g.Player.Pos;
        var sheiva = Feature(g, FeatureKind.WallBuy, "Sheiva");
        bool toggle = false;
        for (int frame = 0; frame < 30 * 240 && !g.IsGameOver; frame++)
        {
            // Like a player would: buy the Sheiva off the wall, then keep its ammo topped up.
            var w = g.Player.Current;
            bool needGun = w.Def.Name != "Sheiva" && g.Player.Points >= 900;
            bool needAmmo = w.Def.Name == "Sheiva" && w.Reserve < 20 && g.Player.Points >= 450;
            if (needGun || needAmmo)
            {
                g.Player.Pos = sheiva;
                Tap(g, E);
                g.Player.Pos = home;
            }

            var target = g.Zombies.Where(z => z.Alive && g.Map.HasLineOfSight(g.Player.Pos, z.Pos))
                .OrderBy(z => Vec2.Distance(z.Pos, g.Player.Pos)).FirstOrDefault();
            toggle = !toggle;
            bool close = target != null && Vec2.Distance(target.Pos, g.Player.Pos) < 4;
            var input = new InputState { AimPoint = target?.Pos, Fire = target != null && !close && toggle, Knife = close && toggle };
            g.Update(1.0 / 30, input);
        }
        Assert.True(g.Round >= 3, $"Bot only reached round {g.Round} (game over: {g.IsGameOver}, kills {g.Player.Kills})");
        Assert.True(g.Player.Kills >= 20, $"Bot only got {g.Player.Kills} kills");
    }

    [Fact]
    public void AimAngle_OverridesAimPoint()
    {
        var g = new Game(seed: 1);
        g.Update(1.0 / 30, new InputState { AimAngle = 1.0, AimPoint = g.Player.Pos + new Vec2(-10, 0) });
        Assert.Equal(1.0, g.Player.AimAngle, 6);
    }

    [Fact]
    public void RoundScaling_IsMonotonic()
    {
        for (int r = 1; r < 50; r++)
        {
            Assert.True(Game.ZombiesForRound(r + 1) >= Game.ZombiesForRound(r));
            Assert.True(Game.HealthForRound(r + 1) > Game.HealthForRound(r));
        }
    }
}
