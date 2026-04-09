using Godot;
using System;

namespace HoverTank
{
    public enum WeaponType { MiniGun = 0, Rocket = 1, TankShell = 2 }

    // Standalone   – local input drives firing; projectiles deal damage (default/offline).
    // LocalPrediction – local input drives firing; projectiles are visual-only + fire
    //                   the Fired event so NetworkManager can relay the shot to the server.
    // NetworkGhost – no local input; projectile spawning is driven entirely by network
    //               events (the Fired event is never raised in this mode).
    public enum WeaponFireMode { Standalone, LocalPrediction, NetworkGhost }

    public partial class WeaponManager : Node3D
    {
        // Controls how this weapon manager interacts with the network.
        public WeaponFireMode FireMode { get; set; } = WeaponFireMode.Standalone;

        // Input action prefix. "" for P1 (default), "p2_" for split-screen P2.
        // Affects next_weapon and fire_weapon action names.
        public string InputPrefix { get; set; } = "";

        // Raised once per individual projectile spawn (e.g. twice for a minigun burst).
        // NetworkManager subscribes to this to relay the shot to the server / other clients.
        public event Action<ProjectileKind, Transform3D>? Fired;

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

        // ── Turret reference (set by TurretController on Turret node) ────────
        private TurretController? _turret;

        // World-space point the crosshair aims at. Set by HoverTank each tick.
        // Null when no camera is present (server-side, ghost tanks).
        // Rockets use this as their guided target.
        public Vector3? AimTarget { get; set; }

        // ── AI fire control ──────────────────────────────────────────────────
        // EnemyAI sets this to true each frame it wants to fire.
        // WeaponManager fires (respecting cooldown) then clears the flag.
        public bool AIFireRequested { get; set; }

        // Directly selects a weapon and resets cooldown (used by EnemyAI on spawn).
        public void SelectWeapon(WeaponType weapon)
        {
            CurrentWeapon = weapon;
            _cooldown = 0f;
        }

        public override void _Ready()
        {
            _miniGunLeft  = GetNode<Marker3D>("MiniGunLeft");
            _miniGunRight = GetNode<Marker3D>("MiniGunRight");
            _rocketLeft   = GetNode<Marker3D>("RocketLeft");
            _rocketRight  = GetNode<Marker3D>("RocketRight");
            _cannon       = GetNode<Marker3D>("Cannon");

            _turret = GetParent().GetNodeOrNull<TurretController>("Turret");

            // Cache owner RID so projectiles can exclude the firing tank
            if (GetParent() is RigidBody3D rb)
                _ownerRid = rb.GetRid();
        }

        public override void _Process(double delta)
        {
            // Ghost tanks are driven entirely by network events — ignore local input.
            if (FireMode == WeaponFireMode.NetworkGhost) return;

            _cooldown -= (float)delta;

            if (Input.IsActionJustPressed(InputPrefix + "next_weapon"))
                CycleWeapon(1);

            // Minigun: hold to fire. Rockets & shells: tap to fire.
            bool trigger = CurrentWeapon == WeaponType.MiniGun
                ? Input.IsActionPressed(InputPrefix + "fire_weapon")
                : Input.IsActionJustPressed(InputPrefix + "fire_weapon");

            if (trigger && _cooldown <= 0f)
                Fire();

            // AI fire request — checked after player input so cooldown applies equally.
            if (AIFireRequested)
            {
                if (_cooldown <= 0f) Fire();
                AIFireRequested = false;
            }
        }

        private void CycleWeapon(int dir)
        {
            CurrentWeapon = (WeaponType)(((int)CurrentWeapon + dir + 3) % 3);
            _cooldown = 0f; // allow immediate fire after switching
        }

        private void Fire()
        {
            Vector3? turretFwd = _turret?.GetAimForward();

            switch (CurrentWeapon)
            {
                case WeaponType.MiniGun:
                    // Minigun is fixed to the tank body — no turret involvement.
                    if (MiniGunAmmo <= 0) return;
                    SpawnProjectile(_miniGunLeft,  ProjectileKind.Bullet);
                    SpawnProjectile(_miniGunRight, ProjectileKind.Bullet);
                    MiniGunAmmo = Math.Max(0, MiniGunAmmo - 2);
                    _cooldown = MiniGunInterval;
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Bullet, GlobalPosition);
                    break;

                case WeaponType.Rocket:
                    // Rockets fire along turret aim and curve toward the crosshair target.
                    if (RocketAmmo <= 0) return;
                    var rocketOrigin = _rocketAlternate ? _rocketRight : _rocketLeft;
                    SpawnProjectile(rocketOrigin, ProjectileKind.Rocket,
                                    aimOverride: turretFwd,
                                    guidedTarget: AimTarget);
                    _rocketAlternate = !_rocketAlternate;
                    RocketAmmo = Math.Max(0, RocketAmmo - 1);
                    _cooldown = RocketInterval;
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Rocket, GlobalPosition);
                    break;

                case WeaponType.TankShell:
                    // Cannon shell fires along turret aim direction.
                    if (TankShellAmmo <= 0) return;
                    SpawnProjectile(_cannon, ProjectileKind.Shell, aimOverride: turretFwd);
                    TankShellAmmo = Math.Max(0, TankShellAmmo - 1);
                    _cooldown = ShellInterval;
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Shell, GlobalPosition);
                    break;
            }
        }

        private void SpawnProjectile(Marker3D origin, ProjectileKind kind,
                                      Vector3? aimOverride = null,
                                      Vector3? guidedTarget = null)
        {
            var (speed, damage, lifetime) = Projectile.GetStats(kind);
            var proj = new Projectile
            {
                Speed          = speed,
                Damage         = damage,
                Lifetime       = lifetime,
                Kind           = kind,
                OwnerRid       = _ownerRid,
                // In LocalPrediction mode the server owns damage; spawn visuals only.
                IsVisualOnly   = FireMode == WeaponFireMode.LocalPrediction,
                TargetPosition = guidedTarget,
            };
            // Add to scene root so the projectile is independent of the tank
            GetTree().CurrentScene.AddChild(proj);
            proj.GlobalTransform = origin.GlobalTransform;

            // Override aim direction for turret-aimed weapons (cannon, rockets).
            // Basis.LookingAt(dir) makes the -Z axis (projectile forward) point toward dir.
            if (aimOverride.HasValue && !aimOverride.Value.IsZeroApprox())
                proj.GlobalBasis = Basis.LookingAt(aimOverride.Value.Normalized(), Vector3.Up);

            // Notify NetworkManager so it can relay the shot over the network.
            Fired?.Invoke(kind, proj.GlobalTransform);
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
