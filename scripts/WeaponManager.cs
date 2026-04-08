using Godot;
using System;

namespace HoverTank
{
    public enum WeaponType { MiniGun = 0, Rocket = 1, TankShell = 2 }

    public partial class WeaponManager : Node3D
    {
        // ── Ammo ────────────────────────────────────────────────────────────
        public int MiniGunAmmo  = 500;
        public int RocketAmmo   = 20;
        public int TankShellAmmo = 15;

        public const int MaxMiniGunAmmo   = 500;
        public const int MaxRocketAmmo    = 20;
        public const int MaxTankShellAmmo = 15;

        // ── Fire intervals (seconds between shots) ──────────────────────────
        private const float MiniGunInterval = 0.05f;  // 20 rounds/sec
        private const float RocketInterval  = 1.5f;
        private const float ShellInterval   = 3.0f;

        // ── State ────────────────────────────────────────────────────────────
        public WeaponType CurrentWeapon { get; private set; } = WeaponType.MiniGun;
        private bool  _rocketAlternate;
        private float _cooldown;
        private Rid   _ownerRid;

        // ── Fire points (Marker3D children set in scene) ─────────────────────
        private Marker3D _miniGunLeft  = null!;
        private Marker3D _miniGunRight = null!;
        private Marker3D _rocketLeft   = null!;
        private Marker3D _rocketRight  = null!;
        private Marker3D _cannon       = null!;

        public override void _Ready()
        {
            _miniGunLeft  = GetNode<Marker3D>("MiniGunLeft");
            _miniGunRight = GetNode<Marker3D>("MiniGunRight");
            _rocketLeft   = GetNode<Marker3D>("RocketLeft");
            _rocketRight  = GetNode<Marker3D>("RocketRight");
            _cannon       = GetNode<Marker3D>("Cannon");

            // Cache owner RID so projectiles can exclude the firing tank
            if (GetParent() is RigidBody3D rb)
                _ownerRid = rb.GetRid();
        }

        public override void _Process(double delta)
        {
            _cooldown -= (float)delta;

            if (Input.IsActionJustPressed("next_weapon"))
                CycleWeapon(1);

            // Minigun: hold to fire. Rockets & shells: tap to fire.
            bool trigger = CurrentWeapon == WeaponType.MiniGun
                ? Input.IsActionPressed("fire_weapon")
                : Input.IsActionJustPressed("fire_weapon");

            if (trigger && _cooldown <= 0f)
                Fire();
        }

        private void CycleWeapon(int dir)
        {
            CurrentWeapon = (WeaponType)(((int)CurrentWeapon + dir + 3) % 3);
            _cooldown = 0f; // allow immediate fire after switching
        }

        private void Fire()
        {
            switch (CurrentWeapon)
            {
                case WeaponType.MiniGun:
                    if (MiniGunAmmo <= 0) return;
                    SpawnProjectile(_miniGunLeft,  speed: 90f,  damage: 5f,   lifetime: 2.5f, kind: ProjectileKind.Bullet);
                    SpawnProjectile(_miniGunRight, speed: 90f,  damage: 5f,   lifetime: 2.5f, kind: ProjectileKind.Bullet);
                    MiniGunAmmo = Math.Max(0, MiniGunAmmo - 2);
                    _cooldown = MiniGunInterval;
                    break;

                case WeaponType.Rocket:
                    if (RocketAmmo <= 0) return;
                    SpawnProjectile(
                        _rocketAlternate ? _rocketRight : _rocketLeft,
                        speed: 28f, damage: 50f, lifetime: 6.0f, kind: ProjectileKind.Rocket);
                    _rocketAlternate = !_rocketAlternate;
                    RocketAmmo = Math.Max(0, RocketAmmo - 1);
                    _cooldown = RocketInterval;
                    break;

                case WeaponType.TankShell:
                    if (TankShellAmmo <= 0) return;
                    SpawnProjectile(_cannon, speed: 45f, damage: 100f, lifetime: 6.0f, kind: ProjectileKind.Shell);
                    TankShellAmmo = Math.Max(0, TankShellAmmo - 1);
                    _cooldown = ShellInterval;
                    break;
            }
        }

        private void SpawnProjectile(Marker3D origin, float speed, float damage,
                                      float lifetime, ProjectileKind kind)
        {
            var proj = new Projectile
            {
                Speed    = speed,
                Damage   = damage,
                Lifetime = lifetime,
                Kind     = kind,
                OwnerRid = _ownerRid,
            };
            // Add to scene root so the projectile is independent of the tank
            GetTree().CurrentScene.AddChild(proj);
            proj.GlobalTransform = origin.GlobalTransform;
        }

        // Returns (current, max) ammo for a given weapon type.
        public (int current, int max) GetAmmo(WeaponType weapon) => weapon switch
        {
            WeaponType.MiniGun   => (MiniGunAmmo,    MaxMiniGunAmmo),
            WeaponType.Rocket    => (RocketAmmo,     MaxRocketAmmo),
            WeaponType.TankShell => (TankShellAmmo,  MaxTankShellAmmo),
            _                    => (0, 0),
        };
    }
}
