using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using DerZombies.Core;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DerZombies.Civil3D
{
    internal interface IGameRenderer : IDisposable
    {
        void Draw(Game game, bool paused);
    }

    /// <summary>
    /// First-person view. Every frame the AutoCAD view is turned into a perspective
    /// camera at the player's eye. Zombies, pickups and effects are transient 3D
    /// solids, labels are billboards that turn to face you, and the HUD is drawn on
    /// a plane fixed just in front of the camera.
    /// </summary>
    internal sealed class Renderer3D : IGameRenderer
    {
        public const double EyeHeight = 5.4;
        private const double GunHeight = 4.4;
        private const double HudDistance = 3.0;
        private const double LensLength = 20.0;
        private const double LabelRange = 70.0;

        /// <summary>Look up/down in radians, set by the session from the mouse.</summary>
        public double Pitch { get; set; }

        private readonly Database _db;
        private readonly Editor _ed;
        private readonly Vector3d _origin;
        private readonly GameMap _map;
        private readonly IntegerCollection _vps = new IntegerCollection();
        private readonly List<Entity> _all = new List<Entity>();
        private readonly Dictionary<object, Model> _tracked = new Dictionary<object, Model>();
        private readonly HashSet<object> _seen = new HashSet<object>();

        // Camera basis for this frame
        private Point3d _eye;
        private Vector3d _fwd, _right, _up, _fwdFlat;
        private (Point3d Eye, Vector3d Fwd)? _lastCamera;

        // World overlays
        private readonly Solid3d _powerLight, _papLight, _pedestalLight;
        private readonly Solid3d[] _padLights;
        private readonly Model _box;
        private readonly List<(Label Label, Func<Game, string> Text)> _worldLabels = new List<(Label, Func<Game, string>)>();

        // HUD
        private readonly List<HudItem> _hud = new List<HudItem>();
        private readonly HudText _center, _round, _points, _weapon, _perks, _objectives, _area, _prompt, _pause, _timers;
        private readonly HudText[] _feed = new HudText[5];
        private readonly HudItem _hurt;

        private TransientManager Tm => TransientManager.CurrentTransientManager;

        public Renderer3D(Document doc, Game game, Vector3d origin)
        {
            _db = doc.Database;
            _ed = doc.Editor;
            _origin = origin;
            _map = game.Map;
            SetCamera(game.Player);

            // ---- state lights on the quest machines
            Vec2 F(FeatureKind k) { var f = MapData.Features.First(x => x.Kind == k); return _map.CellCenter(f.Col, f.Row); }
            _powerLight = World(Solids.Sphere(_db, W(F(FeatureKind.PowerSwitch), 6.4), 0.8), 1);
            _papLight = World(Solids.Sphere(_db, W(F(FeatureKind.PackAPunch), 7.6), 1.0), 8);
            _pedestalLight = World(Solids.Sphere(_db, W(F(FeatureKind.BowPedestal), 3.8), 0.8), 250);
            _padLights = game.Pads.Select(p => World(Solids.Cylinder(_db, W(p.Pos, 0.35), 2.4, 0.3), 1)).ToArray();

            var boxModel = new Model(W(game.BoxPos, 0));
            boxModel.Parts.Add(World(Solids.Box(_db, W(game.BoxPos - new Vec2(3, 1.4), 0), W(game.BoxPos + new Vec2(3, 1.4), 2.8)), 5));
            _box = boxModel;
            _worldLabels.Add((NewLabel(0.9, 5), g => g.BoxState == BoxState.Idle ? "? MYSTERY BOX ?" : g.BoxDisplay));

            // ---- floating labels over everything you can buy or use
            foreach (var door in _map.Doors.Values)
            {
                var d = door;
                AddWorldLabel(d.Center, MapBuilder3D.DoorHeight + 1.5, 1.1, 2, g => d.Open ? "" : $"{d.Cost}");
            }
            foreach (var f in MapData.Features)
            {
                var c = _map.CellCenter(f.Col, f.Row);
                switch (f.Kind)
                {
                    case FeatureKind.Perk:
                    {
                        var perk = Enum.Parse<Perk>(f.Key);
                        AddWorldLabel(c, 9, 0.8, Perks.Color(perk), g => $"{Perks.DisplayName(perk)}  {Perks.Cost(perk)}");
                        break;
                    }
                    case FeatureKind.WallBuy:
                    {
                        var def = WeaponCatalog.Get(f.Key);
                        AddWorldLabel(c, 6.5, 0.7, 51, g => $"{def.Name}  {def.WallCost}");
                        break;
                    }
                    case FeatureKind.PowerSwitch: AddWorldLabel(c, 8, 0.8, 1, g => g.PowerOn ? "POWER ON" : "POWER"); break;
                    case FeatureKind.LandingPad: AddWorldLabel(c, 3, 0.7, 4, g => "LANDING PAD"); break;
                    case FeatureKind.PackAPunch: AddWorldLabel(c, 9.5, 0.9, 6, g => "PACK-A-PUNCH"); break;
                    case FeatureKind.Gondola: AddWorldLabel(c, 9, 0.8, 4, g => "GONDOLA"); break;
                    case FeatureKind.BowPedestal: AddWorldLabel(c, 5.5, 0.7, 30, g => g.BowTaken ? "" : "BOW PEDESTAL"); break;
                    case FeatureKind.BowAltar: AddWorldLabel(c, 4.5, 0.7, 6, g => f.Key.ToUpperInvariant() + " ALTAR"); break;
                }
            }
            foreach (var dr in game.Dragons)
            {
                var d = dr;
                AddWorldLabel(d.Pos, 16.5, 1.0, 30, g => d.Fed ? $"{d.Name.ToUpperInvariant()} - FED" : $"{d.Name.ToUpperInvariant()}  SOULS {d.Souls}/{Dragon.SoulsNeeded}");
            }

            // ---- HUD (units are drawing units on a plane HudDistance in front of the eye)
            _center = HudLabel(0, 0.62, 0.12, 2, centred: true);
            _prompt = HudLabel(0, -0.38, 0.07, 2, centred: true);
            _pause = HudLabel(0, 0.2, 0.2, 1, centred: true);
            _round = HudLabel(-2.1, -1.2, 0.26, 1);
            _points = HudLabel(-2.1, -0.82, 0.09, 7);
            _perks = HudLabel(-2.1, -0.66, 0.055, 7);
            _timers = HudLabel(-2.1, -0.54, 0.055, 3);
            _weapon = HudLabel(0.75, -1.16, 0.075, 7);
            _objectives = HudLabel(-2.1, 1.12, 0.05, 8);
            _area = HudLabel(-2.1, 1.0, 0.07, 7);
            for (int i = 0; i < _feed.Length; i++) _feed[i] = HudLabel(0.75, 1.12 - i * 0.08, 0.05, 7);

            // Crosshair
            foreach (var (a, b) in new[] { ((-0.09, 0.0), (-0.03, 0.0)), ((0.03, 0.0), (0.09, 0.0)), ((0.0, -0.09), (0.0, -0.03)), ((0.0, 0.03), (0.0, 0.09)) })
                Hud(new Line(new Point3d(a.Item1, a.Item2, 0), new Point3d(b.Item1, b.Item2, 0)), 0, 0, 7);

            // Gun: a simple blocky silhouette in the lower right.
            Hud(new Solid(new Point3d(0.0, 0.0, 0), new Point3d(0.55, 0.12, 0), new Point3d(0.0, 0.16, 0), new Point3d(0.55, 0.26, 0)), 1.05, -0.95, 250);
            Hud(new Solid(new Point3d(0.1, -0.25, 0), new Point3d(0.22, -0.25, 0), new Point3d(0.1, 0.02, 0), new Point3d(0.22, 0.04, 0)), 1.05, -0.95, 8);
            Hud(new Solid(new Point3d(-0.25, 0.1, 0), new Point3d(0.0, 0.08, 0), new Point3d(-0.25, 0.14, 0), new Point3d(0.0, 0.14, 0)), 1.05, -0.95, 251);

            // Red border when you're hurt.
            var border = new Polyline();
            border.AddVertexAt(0, new Point2d(-2.25, -1.3), 0, 0, 0);
            border.AddVertexAt(1, new Point2d(2.25, -1.3), 0, 0, 0);
            border.AddVertexAt(2, new Point2d(2.25, 1.3), 0, 0, 0);
            border.AddVertexAt(3, new Point2d(-2.25, 1.3), 0, 0, 0);
            border.Closed = true;
            border.ConstantWidth = 0.08;
            _hurt = Hud(border, 0, 0, 1);
            _hurt.Entity.Visible = false;
        }

        // ================================================================== per frame

        public void Draw(Game game, bool paused)
        {
            var p = game.Player;
            SetCamera(p);
            ApplyCamera();

            // State lights
            Recolor(_powerLight, game.PowerOn ? (short)3 : (short)1);
            for (int i = 0; i < _padLights.Length; i++)
                Recolor(_padLights[i], game.PadsActive[i] ? (short)3 : game.PowerOn ? (short)30 : (short)1);
            Recolor(_papLight, game.PackAPunchReady ? (short)6 : (short)8);
            Recolor(_pedestalLight, game.BowTaken ? (short)250 : game.BowReady ? (short)30 : (short)250);

            // Mystery box follows its current location.
            MoveTo(_box, W(game.BoxPos, 0));
            foreach (var part in _box.Parts) Recolor(part, game.FireSale > 0 ? (short)1 : game.BoxState == BoxState.Leaving ? (short)8 : (short)5);
            _worldLabels[0].Label.Anchor = W(game.BoxPos, 4.2);

            // Moving things
            _seen.Clear();
            foreach (var z in game.Zombies) Sync(z, CreateZombie, UpdateZombie);
            foreach (var pr in game.Projectiles)
                Sync(pr, x => Single(World(Solids.Sphere(_db, W(x.Pos, ProjectileZ(x)), Math.Max(0.3, x.Radius * 0.7)), x.Color), W(x.Pos, ProjectileZ(x))),
                    (x, m) => MoveTo(m, W(x.Pos, ProjectileZ(x))));
            foreach (var pu in game.PowerUps) Sync(pu, CreatePowerUp, UpdatePowerUp);
            foreach (var fx in game.Effects) Sync(fx, CreateEffect, UpdateEffect);
            foreach (var key in _tracked.Keys.Where(k => !_seen.Contains(k)).ToList())
            {
                _tracked[key].Dispose(this);
                _tracked.Remove(key);
            }

            foreach (var (label, text) in _worldLabels) PlaceBillboard(label, text(game));
            DrawHud(game, paused);
        }

        private static double ProjectileZ(Projectile p) => p.Kind == ProjectileKind.Grenade ? 1.0 : GunHeight;

        private void DrawHud(Game game, bool paused)
        {
            var p = game.Player;
            var w = p.Current;

            _center.Set(game.CenterMessage);
            _round.Set(game.Round == 0 ? "" : game.Round.ToString());
            _points.Set($"{p.Points:N0}");

            string ammo = w.Def.Kind == WeaponKind.Bow ? $"{w.Mag + w.Reserve} arrows" : $"{w.Mag} / {w.Reserve}";
            string reload = p.Reloading ? "  RELOADING" : w.Mag == 0 && w.Reserve == 0 ? "  NO AMMO" : "";
            _weapon.Set($"{w.Name}   {ammo}{reload}   G:{p.Grenades}");

            string perks = string.Join(" ", p.Perks.Select(Perks.Short));
            _perks.Set($"HP {Math.Ceiling(p.Health)}/{p.MaxHealth}   {perks}");

            var timers = new List<string>();
            if (game.InstaKill > 0) timers.Add($"INSTA-KILL {Math.Ceiling(game.InstaKill)}");
            if (game.DoublePoints > 0) timers.Add($"DOUBLE POINTS {Math.Ceiling(game.DoublePoints)}");
            if (game.FireSale > 0) timers.Add($"FIRE SALE {Math.Ceiling(game.FireSale)}");
            _timers.Set(string.Join("   ", timers));

            string bow = game.BowTaken ? $"bow kills {p.BowKills}" : game.BowReady ? "bow ready (Clock Tower)" : $"dragons fed {game.Dragons.Count(d => d.Fed)}/{game.Dragons.Count}";
            string left = game.Intermission > 0 && game.Round > 0 ? $"next round in {Math.Ceiling(game.Intermission)}" : $"zombies left {game.ZombiesRemaining}";
            _objectives.Set($"power {(game.PowerOn ? "ON" : "OFF")} | pads {game.PadsActive.Count(a => a)}/3 | {bow} | {left}");

            char area = _map.AreaAt(p.Pos);
            _area.Set(area != '\0' && _map.Areas.TryGetValue(area, out var a) ? a.Name.ToUpperInvariant() : "");
            _prompt.Set(game.Prompt);
            _pause.Set(paused ? "PAUSED" : "");

            var feed = game.Feed;
            for (int i = 0; i < _feed.Length; i++) _feed[i].Set(i < feed.Count ? feed[feed.Count - 1 - i] : "");

            bool hurt = p.HurtFlash > 0 || p.Health < p.MaxHealth * 0.35;
            if (_hurt.Entity.Visible != hurt) _hurt.Entity.Visible = hurt;

            foreach (var item in _hud) PlaceHud(item);
        }

        // ================================================================== camera

        private void SetCamera(Player p)
        {
            double yaw = p.AimAngle;
            double pitch = Math.Clamp(Pitch, -0.7, 0.7);
            _eye = W(p.Pos, EyeHeight);
            _fwdFlat = new Vector3d(Math.Cos(yaw), Math.Sin(yaw), 0);
            _fwd = new Vector3d(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch));
            _right = new Vector3d(Math.Sin(yaw), -Math.Cos(yaw), 0);
            _up = _right.CrossProduct(_fwd).GetNormal();
        }

        private void ApplyCamera()
        {
            if (_lastCamera is { } last && last.Eye.DistanceTo(_eye) < 1e-4 && last.Fwd.IsEqualTo(_fwd)) return;
            _lastCamera = (_eye, _fwd);

            using var view = _ed.GetCurrentView();
            view.Target = _eye + _fwd * 10;
            view.ViewDirection = -_fwd * 10;   // target -> camera
            view.PerspectiveEnabled = true;
            view.LensLength = LensLength;
            view.CenterPoint = Point2d.Origin;
            view.ViewTwist = 0;
            view.Height = 10;
            view.Width = 18;
            _ed.SetCurrentView(view);
        }

        // ================================================================== models

        /// <summary>A group of transient entities moved together by displacement.</summary>
        private sealed class Model
        {
            public readonly List<Entity> Parts = new List<Entity>();
            public Point3d At;
            public Label? Label;
            public Model(Point3d at) { At = at; }

            public void Dispose(Renderer3D r)
            {
                foreach (var e in Parts) r.Remove(e);
                if (Label != null) r.Remove(Label.Text);
            }
        }

        private Model Single(Entity e, Point3d at)
        {
            var m = new Model(at);
            m.Parts.Add(e);
            return m;
        }

        private void MoveTo(Model m, Point3d p)
        {
            var d = p - m.At;
            if (d.Length < 1e-6) return;
            var disp = Matrix3d.Displacement(d);
            foreach (var e in m.Parts)
            {
                e.TransformBy(disp);
                Update(e);
            }
            m.At = p;
        }

        private Model CreateZombie(Zombie z)
        {
            bool panzer = z.Type == ZombieType.Panzer;
            double bodyR = panzer ? 2.0 : z.Radius * 0.75;
            double bodyH = panzer ? 6.5 : 4.3;
            double headR = panzer ? 1.1 : 0.65;
            var m = new Model(W(z.Pos, 0));
            m.Parts.Add(World(Solids.Cylinder(_db, W(z.Pos, 0), bodyR, bodyH), ZombieColor(z)));
            m.Parts.Add(World(Solids.Sphere(_db, W(z.Pos, bodyH + headR * 0.9), headR), panzer ? (short)8 : (short)32));
            if (panzer) m.Label = NewLabel(0.9, 1);
            return m;
        }

        private void UpdateZombie(Zombie z, Model m)
        {
            MoveTo(m, W(z.Pos, 0));
            Recolor(m.Parts[0], ZombieColor(z));
            if (m.Label != null)
            {
                int bars = (int)Math.Round(10 * Math.Clamp(z.Health / z.MaxHealth, 0, 1));
                m.Label.Anchor = W(z.Pos, 10);
                PlaceBillboard(m.Label, "PANZER [" + new string('#', bars) + new string('-', 10 - bars) + "]");
            }
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

        private Model CreatePowerUp(PowerUp pu)
        {
            var m = Single(World(Solids.Sphere(_db, W(pu.Pos, 2.4), 1.0), 3), W(pu.Pos, 0));
            m.Label = NewLabel(0.7, 3);
            m.Label.Anchor = W(pu.Pos, 4);
            return m;
        }

        private void UpdatePowerUp(PowerUp pu, Model m)
        {
            bool v = pu.Visible;
            foreach (var e in m.Parts)
                if (e.Visible != v) { e.Visible = v; Update(e); }
            PlaceBillboard(m.Label!, v ? pu.Label : "");
        }

        private Model CreateEffect(Effect fx)
        {
            if (fx.Shape == EffectShape.Line)
            {
                // Tracers, lightning, flames and souls all run at gun height, nudged to the gun side.
                var a = W(fx.A, GunHeight) + _right * 0.5;
                return Single(World(new Line(a, W(fx.B, GunHeight)), fx.Color), W(fx.A, 0));
            }
            var c = new Circle(W(fx.A, 0.15), Vector3d.ZAxis, Math.Max(0.1, fx.CurrentRadius)) { Thickness = fx.Grow ? 3.0 : 0.6 };
            return Single(World(c, fx.Color), W(fx.A, 0));
        }

        private void UpdateEffect(Effect fx, Model m)
        {
            if (fx.Grow && m.Parts[0] is Circle c)
            {
                c.Radius = Math.Max(0.1, fx.CurrentRadius);
                Update(c);
            }
        }

        private void Sync<T>(T item, Func<T, Model> create, Action<T, Model> update) where T : class
        {
            _seen.Add(item);
            if (_tracked.TryGetValue(item, out var m)) update(item, m);
            else _tracked[item] = create(item);
        }

        // ================================================================== labels & HUD

        /// <summary>Text that is re-oriented every frame (billboard or HUD).</summary>
        private sealed class Label
        {
            public DBText Text = null!;
            public Matrix3d Current = Matrix3d.Identity;
            public double Height;
            public string Value = "";
            public Point3d Anchor;
        }

        private class HudItem
        {
            public Entity Entity = null!;
            public Matrix3d Current = Matrix3d.Identity;
            public double X, Y;
        }

        private sealed class HudText : HudItem
        {
            public bool Centred;
            public double BaseX;
            public double Height;
            public string Value = "";

            public void Set(string s)
            {
                if (s == Value) return;
                Value = s;
                var t = (DBText)Entity;
                t.TextString = string.IsNullOrEmpty(s) ? " " : s;
                if (Centred) X = BaseX - s.Length * Height * 0.42;
            }
        }

        private Label NewLabel(double height, short color)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(_db);
            t.Height = height;
            t.TextString = " ";
            var label = new Label { Text = t, Height = height };
            World(t, color, defaults: false);
            return label;
        }

        private void AddWorldLabel(Vec2 at, double z, double height, short color, Func<Game, string> text)
        {
            var label = NewLabel(height, color);
            label.Anchor = W(at, z);
            _worldLabels.Add((label, text));
        }

        /// <summary>Stands a label upright at its anchor, centred and turned to face the camera.</summary>
        private void PlaceBillboard(Label l, string text)
        {
            bool visible = !string.IsNullOrEmpty(text) && l.Anchor.DistanceTo(_eye) < LabelRange;
            if (l.Text.Visible != visible) { l.Text.Visible = visible; Update(l.Text); }
            if (!visible) return;
            if (text != l.Value)
            {
                l.Value = text;
                l.Text.TextString = text;
            }
            var origin = l.Anchor + _right * (-text.Length * l.Height * 0.42);
            var m = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                origin, _right, Vector3d.ZAxis, -_fwdFlat);
            l.Text.TransformBy(m * l.Current.Inverse());
            l.Current = m;
            Update(l.Text);
        }

        private HudText HudLabel(double x, double y, double height, short color, bool centred = false)
        {
            var t = new DBText();
            t.SetDatabaseDefaults(_db);
            t.Height = height;
            t.TextString = " ";
            var item = new HudText { Entity = t, X = x, Y = y, BaseX = x, Height = height, Centred = centred };
            Overlay(t, color, defaults: false);
            _hud.Add(item);
            return item;
        }

        private HudItem Hud(Entity e, double x, double y, short color)
        {
            var item = new HudItem { Entity = e, X = x, Y = y };
            Overlay(e, color);
            _hud.Add(item);
            return item;
        }

        /// <summary>Pins a HUD item to the plane in front of the camera.</summary>
        private void PlaceHud(HudItem h)
        {
            var at = _eye + _fwd * HudDistance + _right * h.X + _up * h.Y;
            var m = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                at, _right, _up, -_fwd);
            h.Entity.TransformBy(m * h.Current.Inverse());
            h.Current = m;
            Update(h.Entity);
        }

        // ================================================================== plumbing

        private Point3d W(Vec2 v, double z) => new Point3d(v.X, v.Y, z) + _origin;

        /// <summary>A 3D object in the scene (hidden by walls).</summary>
        private T World<T>(T e, short color, bool defaults = true) where T : Entity =>
            Register(e, color, defaults, TransientDrawingMode.Main);

        /// <summary>Drawn on top of everything (HUD).</summary>
        private T Overlay<T>(T e, short color, bool defaults = true) where T : Entity =>
            Register(e, color, defaults, TransientDrawingMode.DirectTopmost);

        private T Register<T>(T e, short color, bool defaults, TransientDrawingMode mode) where T : Entity
        {
            if (defaults) e.SetDatabaseDefaults(_db);
            e.Layer = MapBuilder.Game;
            e.ColorIndex = color;
            Tm.AddTransient(e, mode, 128, _vps);
            _all.Add(e);
            return e;
        }

        private void Recolor(Entity e, short color)
        {
            if (e.ColorIndex == color) return;
            e.ColorIndex = color;
            Update(e);
        }

        private void Update(Entity e) => Tm.UpdateTransient(e, _vps);

        private void Remove(Entity e)
        {
            Tm.EraseTransient(e, _vps);
            _all.Remove(e);
            e.Dispose();
        }

        public void Dispose()
        {
            foreach (var e in _all.ToList()) Remove(e);
            _tracked.Clear();
        }
    }
}
