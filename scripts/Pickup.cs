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
    ///
    /// IMPORTANT: set <see cref="BasePosition"/> before calling AddChild so
    /// _Ready can capture the correct base Y for the bob animation.
    /// </summary>
    public partial class Pickup : Area3D
    {
        public PickupType Type        { get; set; }
        // World-space spawn position. Must be assigned before AddChild.
        public Vector3    BasePosition { get; set; }

        // Restore amounts per type
        private const float HealthRestore    = 40f;
        private const int   MiniGunRestore   = 200;
        private const int   RocketRestore    = 8;
        private const int   TankShellRestore = 5;

        // Bob animation
        private const float BobSpeed  = 1.8f;
        private const float BobHeight = 0.25f;
        private const float SpinSpeed = 1.4f;   // radians per second

        // Pickup expires after this many seconds if never collected.
        private const float Lifetime = 60f;

        // Tanks live on physics layer 1 (matches HoverTank RigidBody3D default).
        private const uint TankCollisionLayer = 1;

        private float _age;
        private float _bobPhase;
        private bool  _collected;

        public override void _Ready()
        {
            // Occupy no physics layer; detect RigidBody3D on TankCollisionLayer.
            CollisionLayer = 0;
            CollisionMask  = TankCollisionLayer;
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
            _age += (float)delta;

            if (_age >= Lifetime)
            {
                QueueFree();
                return;
            }

            // Bob vertically around base Y (captured from BasePosition in spawner).
            var pos = GlobalPosition;
            pos.Y = BasePosition.Y + BobHeight * Mathf.Sin(BobSpeed * _age + _bobPhase);
            GlobalPosition = pos;

            // Spin around world-up
            RotateY(SpinSpeed * (float)delta);
        }

        private void OnBodyEntered(Node3D body)
        {
            if (_collected) return;
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

            _collected = true;
            QueueFree();
        }

        // ── Visuals ───────────────────────────────────────────────────────────

        // Single source of truth for per-type color used by both mesh and light.
        private Color GetTypeColor() => Type switch
        {
            PickupType.Health      => new Color(0.15f, 1.00f, 0.30f),   // bright green
            PickupType.MiniGunAmmo => new Color(1.00f, 0.90f, 0.20f),   // yellow
            PickupType.RocketAmmo  => new Color(1.00f, 0.45f, 0.10f),   // orange
            _                      => new Color(0.90f, 0.65f, 0.10f),   // gold (TankShell)
        };

        private MeshInstance3D BuildVisual()
        {
            Mesh mesh = Type switch
            {
                PickupType.Health      => new SphereMesh { Radius = 0.45f, Height = 0.9f },
                PickupType.MiniGunAmmo => new BoxMesh    { Size = new Vector3(0.55f, 0.35f, 0.8f) },
                PickupType.RocketAmmo  => new CapsuleMesh { Radius = 0.25f, Height = 0.9f },
                _                      => new SphereMesh { Radius = 0.50f, Height = 1.0f },
            };

            Color color = GetTypeColor();
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
            return new OmniLight3D
            {
                LightColor    = GetTypeColor(),
                LightEnergy   = 1.6f,
                OmniRange     = 5.0f,
                ShadowEnabled = false,
            };
        }
    }
}
