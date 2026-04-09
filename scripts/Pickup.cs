using Godot;
using System;

namespace HoverTank
{
    public enum PickupType { Health, MiniGunAmmo, RocketAmmo, TankShellAmmo }

    /// <summary>
    /// A collectible power-up that floats above the terrain.
    ///
    /// Implemented as an Area3D so the tank's RigidBody3D drives through it
    /// without a physics collision — only an overlap callback fires.
    /// Enemy and ally tanks are ignored; only the player collects pickups.
    /// </summary>
    public partial class Pickup : Area3D
    {
        public PickupType Type { get; set; }

        // Restore amounts per type
        private const float HealthRestore    = 40f;
        private const int   MiniGunRestore   = 200;
        private const int   RocketRestore    = 8;
        private const int   TankShellRestore = 5;

        // Bob animation
        private const float BobSpeed  = 1.8f;
        private const float BobHeight = 0.25f;
        private const float SpinSpeed = 1.4f;   // radians per second

        private bool  _baseYCaptured;
        private float _baseY;
        private float _bobPhase;
        private float _age;

        public override void _Ready()
        {
            // Occupy no physics layer; detect RigidBody3D on layer 1 (tanks).
            CollisionLayer = 0;
            CollisionMask  = 1;
            Monitoring     = true;
            Monitorable    = false;

            var colShape = new CollisionShape3D
            {
                Shape = new SphereShape3D { Radius = 1.8f }
            };
            AddChild(colShape);
            AddChild(BuildVisual());
            AddChild(BuildLight());

            // Stagger bob phase so nearby pickups don't move in sync.
            _bobPhase = GD.Randf() * Mathf.Tau;

            BodyEntered += OnBodyEntered;
            AddToGroup("pickups");
        }

        public override void _Process(double delta)
        {
            // Capture base Y on the first tick after position has been assigned.
            if (!_baseYCaptured)
            {
                _baseY = GlobalPosition.Y;
                _baseYCaptured = true;
            }

            _age += (float)delta;

            // Bob vertically
            var pos = GlobalPosition;
            pos.Y = _baseY + BobHeight * Mathf.Sin(BobSpeed * _age + _bobPhase);
            GlobalPosition = pos;

            // Spin around world-up
            RotateY(SpinSpeed * (float)delta);
        }

        private void OnBodyEntered(Node3D body)
        {
            if (body is not HoverTank tank) return;
            if (tank.IsEnemy || tank.IsFriendlyAI) return;
            if (tank.Health <= 0f) return;

            switch (Type)
            {
                case PickupType.Health:
                    tank.Health = Mathf.Min(tank.MaxHealth, tank.Health + HealthRestore);
                    break;

                case PickupType.MiniGunAmmo:
                    if (tank.Weapons != null)
                        tank.Weapons.MiniGunAmmo = Math.Min(
                            WeaponManager.MaxMiniGunAmmo,
                            tank.Weapons.MiniGunAmmo + MiniGunRestore);
                    break;

                case PickupType.RocketAmmo:
                    if (tank.Weapons != null)
                        tank.Weapons.RocketAmmo = Math.Min(
                            WeaponManager.MaxRocketAmmo,
                            tank.Weapons.RocketAmmo + RocketRestore);
                    break;

                case PickupType.TankShellAmmo:
                    if (tank.Weapons != null)
                        tank.Weapons.TankShellAmmo = Math.Min(
                            WeaponManager.MaxTankShellAmmo,
                            tank.Weapons.TankShellAmmo + TankShellRestore);
                    break;
            }

            QueueFree();
        }

        // ── Visuals ───────────────────────────────────────────────────────────

        private MeshInstance3D BuildVisual()
        {
            (Mesh mesh, Color color) = Type switch
            {
                PickupType.Health      => ((Mesh)new SphereMesh { Radius = 0.45f, Height = 0.9f },
                                           new Color(0.15f, 1.00f, 0.30f)),   // bright green
                PickupType.MiniGunAmmo => (new BoxMesh { Size = new Vector3(0.55f, 0.35f, 0.8f) },
                                           new Color(1.00f, 0.90f, 0.20f)),   // yellow
                PickupType.RocketAmmo  => (new CapsuleMesh { Radius = 0.25f, Height = 0.9f },
                                           new Color(1.00f, 0.45f, 0.10f)),   // orange
                _                      => (new SphereMesh { Radius = 0.50f, Height = 1.0f },
                                           new Color(0.90f, 0.65f, 0.10f)),   // gold (TankShell)
            };

            var mat = new StandardMaterial3D
            {
                AlbedoColor     = color,
                EmissionEnabled = true,
                Emission        = color * 0.55f,
                Roughness       = 0.3f,
                Metallic        = 0.5f,
            };

            var mi = new MeshInstance3D { Mesh = mesh };
            mi.SetSurfaceOverrideMaterial(0, mat);
            return mi;
        }

        private OmniLight3D BuildLight()
        {
            Color lightColor = Type switch
            {
                PickupType.Health      => new Color(0.20f, 1.00f, 0.30f),
                PickupType.MiniGunAmmo => new Color(1.00f, 0.90f, 0.20f),
                PickupType.RocketAmmo  => new Color(1.00f, 0.45f, 0.10f),
                _                      => new Color(0.90f, 0.65f, 0.10f),
            };

            return new OmniLight3D
            {
                LightColor    = lightColor,
                LightEnergy   = 1.6f,
                OmniRange     = 5.0f,
                ShadowEnabled = false,
            };
        }
    }
}
