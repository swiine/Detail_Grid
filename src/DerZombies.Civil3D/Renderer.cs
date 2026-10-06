using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using DerZombies.Core;

namespace DerZombies.Civil3D
{
    /// <summary>
    /// Draws everything that moves as AutoCAD transient graphics: in-memory entities
    /// that are displayed but never written to the drawing, so the game adds nothing
    /// to the DWG or the undo stack while it runs.
    /// </summary>
    internal sealed class Renderer : IDisposable
    {
        private readonly Database _db;
        private readonly Vector3d _origin;
        private readonly GameMap _map;
        private readonly IntegerCollection _viewports = new IntegerCollection();
        private readonly List<Entity> _all = new List<Entity>();
        private readonly Dictionary<object, Entity[]> _tracked = new Dictionary<object, Entity[]>();
        private readonly HashSet<object> _seen = new HashSet<object>();
        private readonly Dictionary<Entity, string> _textCache = new Dictionary<Entity, string>();

        // Player
        private readonly Circle _player;
        private readonly Line _aim;
        // Static-position overlays whose colour shows game state
        private readonly Circle _powerLight;
        private readonly Circle[] _padLights;
        private readonly Circle _papLight;
        private readonly Circle _pedestalLight;
        private readonly List<(Dragon Dragon, Circle Range, DBText Label)> _dragons = new List<(Dragon, Circle, DBText)>();
        // Mystery box
        private readonly Polyline _box;
        private readonly DBText _boxText;
        // HUD
        private readonly DBText _center, _line1, _line2, _line3, _line4, _prompt, _pause;
        private readonly DBText[] _feed = new DBText[5];
        private readonly Solid _healthBar;
        private readonly Polyline _healthFrame;

        private TransientManager Tm => TransientManager.CurrentTransientManager;

        public Renderer(Database db, Game game, Vector3d origin)
        {
            _db = db;
            _origin = origin;
            _map = game.Map;
            double w = _map.Width, h = _map.Height;

            Feature(FeatureKind.PowerSwitch, out var power);
            _powerLight = Add(new Circle(P(power), Vector3d.ZAxis, 3.0), 1);
            _padLights = game.Pads.Select(p => Add(new Circle(P(p.Pos), Vector3d.ZAxis, 2.2), 1)).ToArray();
            Feature(FeatureKind.PackAPunch, out var pap);
            _papLight = Add(new Circle(P(pap), Vector3d.ZAxis, 3.6), 8);
            Feature(FeatureKind.BowPedestal, out var ped);
            _pedestalLight = Add(new Circle(P(ped), Vector3d.ZAxis, 2.6), 8);
            foreach (var d in game.Dragons)
                _dragons.Add((d, Add(new Circle(P(d.Pos), Vector3d.ZAxis, Dragon.Range), 30), Text(d.Pos + new Vec2(-4, -5), 1.6, 30)));

            _box = new Polyline();
            for (int i = 0; i < 4; i++) _box.AddVertexAt(i, new Point2d(i, i % 2), 0, 0, 0);
            _box.Closed = true;
            _box.ConstantWidth = 0.4;
            Add(_box, 5);
            _boxText = Text(Vec2.Zero, 1.8, 5);

            _player = Add(new Circle(P(game.Player.Pos), Vector3d.ZAxis, Player.Radius), 2);
            _aim = Add(new Line(P(game.Player.Pos), P(game.Player.Pos)), 2);

            _center = Text(new Vec2(0, h + 45), 7, 2);
            _line1 = Text(new Vec2(0, h + 33), 5, 7);
            _line2 = Text(new Vec2(0, h + 23), 4, 7);
            _line3 = Text(new Vec2(60, h + 14), 3.5, 7);
            _line4 = Text(new Vec2(0, h + 5), 3.2, 8);
            _prompt = Text(new Vec2(0, -10), 4.5, 2);
            _pause = Text(new Vec2(w / 2 - 40, h / 2), 10, 1);
            for (int i = 0; i < _feed.Length; i++) _feed[i] = Text(new Vec2(w + 6, h - 8 - i * 6), 3, 7);

            var controls = new[]
            {
                "CONTROLS",
                "W A S D   move",
                "Mouse     aim   (or Left/Right)",
                "L-Click / Space   fire",
                "R  reload    Q  swap weapon",
                "E  buy / use",
                "V / R-Click  knife   G  grenade",
                "P  pause     Esc  quit",
                "Enter  restart after game over",
            };
            for (int i = 0; i < controls.Length; i++)
                SetText(Text(new Vec2(w + 6, h - 50 - i * 5), i == 0 ? 3.2 : 2.6, i == 0 ? (short)2 : (short)8), controls[i]);

            _healthFrame = Add(Rect(new Vec2(0, h + 13), new Vec2(50, h + 18)), 8);
            _healthBar = Add(new Solid(P(new Vec2(0, h + 13)), P(new Vec2(1, h + 13)), P(new Vec2(0, h + 18)), P(new Vec2(1, h + 18))), 3);

            void Feature(FeatureKind kind, out Vec2 pos)
            {
                var f = MapData.Features.First(x => x.Kind == kind);
                pos = _map.CellCenter(f.Col, f.Row);
            }
        }

        // ============================================================== per frame

        public void Draw(Game game, bool paused)
        {
            var p = game.Player;

            // Player
            _player.Center = P(p.Pos);
            _player.ColorIndex = (short)(p.HurtFlash > 0 ? 1 : p.Invulnerable > 0 ? 4 : 2);
            _aim.StartPoint = P(p.Pos);
            _aim.EndPoint = P(p.Pos + Vec2.FromAngle(p.AimAngle) * 4.5);
            _aim.ColorIndex = (short)(p.Reloading ? 8 : 2);
            Update(_player);
            Update(_aim);

            // State lights
            Recolor(_powerLight, game.PowerOn ? (short)3 : (short)1);
            for (int i = 0; i < _padLights.Length; i++) Recolor(_padLights[i], game.PadsActive[i] ? (short)3 : game.PowerOn ? (short)30 : (short)1);
            Recolor(_papLight, game.PackAPunchReady ? (short)6 : (short)8);
            Recolor(_pedestalLight, game.BowTaken ? (short)8 : game.BowReady ? (short)30 : (short)250);
            foreach (var (d, range, label) in _dragons)
            {
                Recolor(range, d.Fed ? (short)8 : (short)30);
                SetText(label, d.Fed ? "FED" : $"SOULS {d.Souls}/{Dragon.SoulsNeeded}");
            }

            // Mystery box
            var b = game.BoxPos;
            for (int i = 0; i < 4; i++)
            {
                double sx = i == 0 || i == 3 ? -3 : 3, sy = i < 2 ? -1.5 : 1.5;
                _box.SetPointAt(i, new Point2d(b.X + sx + _origin.X, b.Y + sy + _origin.Y));
            }
            _box.ColorIndex = (short)(game.FireSale > 0 ? 1 : game.BoxState == BoxState.Leaving ? 8 : 5);
            Update(_box);
            _boxText.Position = P(b + new Vec2(-3, 2.5));
            Update(_boxText);
            SetText(_boxText, game.BoxState == BoxState.Idle ? "? BOX ?" : game.BoxDisplay);

            // Moving things
            _seen.Clear();
            foreach (var z in game.Zombies) Sync(z, CreateZombie, UpdateZombie);
            foreach (var pr in game.Projectiles) Sync(pr, x => new Entity[] { Add(new Circle(P(x.Pos), Vector3d.ZAxis, x.Radius), x.Color) }, UpdateProjectile);
            foreach (var pu in game.PowerUps) Sync(pu, CreatePowerUp, UpdatePowerUp);
            foreach (var e in game.Effects) Sync(e, CreateEffect, UpdateEffect);
            foreach (var key in _tracked.Keys.Where(k => !_seen.Contains(k)).ToList())
            {
                foreach (var ent in _tracked[key]) Remove(ent);
                _tracked.Remove(key);
            }

            DrawHud(game, paused);
        }

        private void DrawHud(Game game, bool paused)
        {
            var p = game.Player;
            var w = p.Current;

            SetText(_center, game.CenterMessage);
            _center.Position = P(new Vec2(_map.Width / 2 - game.CenterMessage.Length * 7 * 0.45, _map.Height + 45));
            Update(_center);

            string round = game.Round == 0 ? "-" : game.Round.ToString();
            string next = game.Intermission > 0 && game.Round > 0 ? $"   NEXT ROUND IN {Math.Ceiling(game.Intermission)}" : $"   ZOMBIES LEFT {game.ZombiesRemaining}";
            SetText(_line1, $"ROUND {round}     POINTS {p.Points:N0}     KILLS {p.Kills}  (HEADSHOTS {p.Headshots}){next}");

            string ammo = w.Def.Kind == WeaponKind.Bow ? $"{w.Mag + w.Reserve} ARROWS" : $"{w.Mag} / {w.Reserve}";
            string others = string.Join("  ", p.Weapons.Where(x => x != w).Select(x => $"[{x.Name}]"));
            string reload = p.Reloading ? "  RELOADING..." : w.Mag == 0 && w.Reserve == 0 ? "  NO AMMO" : "";
            SetText(_line2, $"{w.Name}   {ammo}{reload}     {others}     GRENADES {p.Grenades}");

            string perks = p.Perks.Count == 0 ? "no perks" : string.Join(" ", p.Perks.Select(Perks.Short));
            var timers = new List<string>();
            if (game.InstaKill > 0) timers.Add($"INSTA-KILL {Math.Ceiling(game.InstaKill)}");
            if (game.DoublePoints > 0) timers.Add($"DOUBLE POINTS {Math.Ceiling(game.DoublePoints)}");
            if (game.FireSale > 0) timers.Add($"FIRE SALE {Math.Ceiling(game.FireSale)}");
            SetText(_line3, $"HP {Math.Ceiling(p.Health)}/{p.MaxHealth}    PERKS: {perks}    {string.Join("   ", timers)}");

            string bow = game.BowTaken ? $"BOW: kills {p.BowKills}" : game.BowReady ? "BOW: ready at the Clock Tower" : $"DRAGONS FED {game.Dragons.Count(d => d.Fed)}/{game.Dragons.Count}";
            SetText(_line4, $"POWER {(game.PowerOn ? "ON" : "OFF")}   |   LANDING PADS {game.PadsActive.Count(a => a)}/3   |   PACK-A-PUNCH {(game.PackAPunchReady ? "READY" : "LOCKED")}   |   {bow}");

            SetText(_prompt, game.Prompt);
            SetText(_pause, paused ? "PAUSED  (P)" : "");

            var feed = game.Feed;
            for (int i = 0; i < _feed.Length; i++)
                SetText(_feed[i], i < feed.Count ? feed[feed.Count - 1 - i] : "");

            double frac = Math.Clamp(p.Health / p.MaxHealth, 0, 1);
            double h = _map.Height;
            double x = Math.Max(0.01, 50 * frac);
            _healthBar.SetPointAt(1, P(new Vec2(x, h + 13)));
            _healthBar.SetPointAt(3, P(new Vec2(x, h + 18)));
            _healthBar.ColorIndex = (short)(frac > 0.6 ? 3 : frac > 0.3 ? 2 : 1);
            Update(_healthBar);
        }

        // ============================================================== entity factories

        private Entity[] CreateZombie(Zombie z)
        {
            var body = Add(new Circle(P(z.Pos), Vector3d.ZAxis, z.Radius), ZombieColor(z));
            if (z.Type != ZombieType.Panzer) return new Entity[] { body };
            var core = Add(new Circle(P(z.Pos), Vector3d.ZAxis, z.Radius * 0.45), 5);
            var bar = Add(new Line(P(z.Pos), P(z.Pos)), 1);
            return new Entity[] { body, core, bar };
        }

        private void UpdateZombie(Zombie z, Entity[] e)
        {
            var body = (Circle)e[0];
            body.Center = P(z.Pos);
            body.ColorIndex = ZombieColor(z);
            Update(body);
            if (e.Length < 3) return;
            var core = (Circle)e[1];
            core.Center = P(z.Pos);
            Update(core);
            var bar = (Line)e[2];
            double frac = Math.Clamp(z.Health / z.MaxHealth, 0, 1);
            var left = z.Pos + new Vec2(-3, z.Radius + 1.5);
            bar.StartPoint = P(left);
            bar.EndPoint = P(left + new Vec2(6 * frac + 0.01, 0));
            Update(bar);
        }

        private static short ZombieColor(Zombie z)
        {
            if (z.HitFlash > 0) return 7;
            if (z.Webbed > 0) return 6;
            return z.Type switch
            {
                ZombieType.Walker => 92,
                ZombieType.Runner => 40,
                ZombieType.Sprinter => 1,
                ZombieType.Panzer => 150,
                _ => 7,
            };
        }

        private void UpdateProjectile(Projectile p, Entity[] e)
        {
            var c = (Circle)e[0];
            c.Center = P(p.Pos);
            Update(c);
        }

        private Entity[] CreatePowerUp(PowerUp pu)
        {
            var ring = Add(new Circle(P(pu.Pos), Vector3d.ZAxis, 1.6), 3);
            var label = Text(pu.Pos + new Vec2(-pu.Label.Length * 0.8, 2.2), 1.8, 3);
            SetText(label, pu.Label);
            return new Entity[] { ring, label };
        }

        private void UpdatePowerUp(PowerUp pu, Entity[] e)
        {
            foreach (var ent in e)
            {
                if (ent.Visible == pu.Visible) continue;
                ent.Visible = pu.Visible;
                Update(ent);
            }
        }

        private Entity[] CreateEffect(Effect fx) => fx.Shape == EffectShape.Line
            ? new Entity[] { Add(new Line(P(fx.A), P(fx.B)), fx.Color) }
            : new Entity[] { Add(new Circle(P(fx.A), Vector3d.ZAxis, Math.Max(0.1, fx.CurrentRadius)), fx.Color) };

        private void UpdateEffect(Effect fx, Entity[] e)
        {
            if (e[0] is Circle c && fx.Grow)
            {
                c.Radius = Math.Max(0.1, fx.CurrentRadius);
                Update(c);
            }
        }

        // ============================================================== plumbing

        private void Sync<T>(T item, Func<T, Entity[]> create, Action<T, Entity[]> update) where T : class
        {
            _seen.Add(item);
            if (_tracked.TryGetValue(item, out var ents)) update(item, ents);
            else _tracked[item] = create(item);
        }

        private Point3d P(Vec2 v) => new Point3d(v.X, v.Y, 0) + _origin;

        private T Add<T>(T ent, short color, bool defaults = true) where T : Entity
        {
            if (defaults) ent.SetDatabaseDefaults(_db);
            ent.Layer = MapBuilder.Game; // a layer we know is on and thawed
            ent.ColorIndex = color;
            Tm.AddTransient(ent, TransientDrawingMode.DirectShortTerm, 128, _viewports);
            _all.Add(ent);
            return ent;
        }

        private DBText Text(Vec2 at, double height, short color)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(_db);
            t.Position = P(at);
            t.Height = height;
            t.TextString = " ";
            _textCache[t] = " ";
            return Add(t, color, defaults: false);
        }

        private void SetText(DBText t, string s)
        {
            if (string.IsNullOrEmpty(s)) s = " ";
            if (_textCache.TryGetValue(t, out var old) && old == s) return;
            _textCache[t] = s;
            t.TextString = s;
            Update(t);
        }

        private Polyline Rect(Vec2 min, Vec2 max)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(min.X + _origin.X, min.Y + _origin.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(max.X + _origin.X, min.Y + _origin.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(max.X + _origin.X, max.Y + _origin.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(min.X + _origin.X, max.Y + _origin.Y), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private void Recolor(Entity e, short color)
        {
            if (e.ColorIndex == color) return;
            e.ColorIndex = color;
            Update(e);
        }

        private void Update(Entity e) => Tm.UpdateTransient(e, _viewports);

        private void Remove(Entity e)
        {
            Tm.EraseTransient(e, _viewports);
            _all.Remove(e);
            _textCache.Remove(e);
            e.Dispose();
        }

        public void Dispose()
        {
            foreach (var e in _all.ToList()) Remove(e);
            _tracked.Clear();
        }
    }
}
