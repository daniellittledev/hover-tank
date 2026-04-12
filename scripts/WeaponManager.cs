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
        private const float MiniGunInterval = 0.10f;  // 10 pairs/sec (20 bullets/sec)
        private const float RocketInterval  = 1.5f;
        private const float ShellInterval   = 3.0f;

        // ── AI minigun burst pacing ──────────────────────────────────────────
        // Applied only to AI fire requests (player hold-trigger is unaffected).
        // Length + rest must be tuned together: at MiniGunInterval 0.10 s,
        // a 6-shot burst lasts ~0.6 s; a 1.0 s rest gives ~37 % duty cycle,
        // which keeps projectile traffic bounded and reads as "bursts" aurally.
        [Export] public int   AIMinigunBurstLength  = 6;
        [Export] public float AIMinigunBurstRest    = 1.0f;
        [Export] public float AIMinigunBurstJitter  = 0.4f;

        // ── State ────────────────────────────────────────────────────────────
        public WeaponType CurrentWeapon { get; private set; } = WeaponType.MiniGun;
        private bool         _rocketAlternate;
        private float        _cooldown;
        private Rid          _ownerRid;
        private RigidBody3D? _tankBody;
        private BurstPacer   _aiMinigunBurst;

        // ── Fire points (Marker3D children set in scene) ─────────────────────
        private Marker3D _miniGunLeft  = null!;
        private Marker3D _miniGunRight = null!;
        private Marker3D _rocketLeft   = null!;
        private Marker3D _rocketRight  = null!;
        private Marker3D _cannon       = null!;

        // ── Muzzle flash lights (created in _Ready, attached to fire points) ─
        private OmniLight3D _cannonFlashLight   = null!;
        private OmniLight3D _rocketFlashLLight  = null!;
        private OmniLight3D _rocketFlashRLight  = null!;
        private OmniLight3D _miniFlashLLight    = null!;
        private OmniLight3D _miniFlashRLight    = null!;

        // Countdown timers for each flash group (seconds remaining at peak energy).
        private float _cannonFlashTimer;
        private float _rocketFlashLTimer;
        private float _rocketFlashRTimer;
        private float _miniFlashTimer;

        // Duration a flash stays visible.  Energy decays linearly to zero.
        private const float FlashDuration     = 0.09f;
        private const float CannonFlashPeak   = 12f;
        private const float RocketFlashPeak   = 5f;
        private const float MiniFlashPeak     = 2.5f;

        // ── Turret reference (set by TurretController on Turret node) ────────
        private TurretController? _turret;

        // World-space point the crosshair aims at. Set by HoverTank each tick.
        // Null when no camera is present (server-side, ghost tanks).
        // Rockets use this as their guided target.
        public Vector3? AimTarget { get; set; }

        // ── AI fire control ──────────────────────────────────────────────────
        // EnemyAI / AllyAI set this to true each frame they want to fire.
        // WeaponManager fires (respecting cooldown) then clears the flag.
        public bool AIFireRequested { get; set; }

        // UnitCommander sets this to true when consuming a selection click so
        // the same LMB event does not also trigger fire_weapon this frame.
        public bool SuppressFireThisFrame { get; set; }

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

            // Cache parent body so projectiles can exclude it and recoil can be applied.
            if (GetParent() is RigidBody3D rb)
            {
                _ownerRid = rb.GetRid();
                _tankBody = rb;
            }

            _aiMinigunBurst = new BurstPacer(
                AIMinigunBurstLength, AIMinigunBurstRest, AIMinigunBurstJitter);

            // Create muzzle flash lights parented to each fire point so they
            // move with the tank automatically.
            _cannonFlashLight  = AttachFlashLight(_cannon,      range: 8f);
            _rocketFlashLLight = AttachFlashLight(_rocketLeft,  range: 5f);
            _rocketFlashRLight = AttachFlashLight(_rocketRight, range: 5f);
            _miniFlashLLight   = AttachFlashLight(_miniGunLeft, range: 3f);
            _miniFlashRLight   = AttachFlashLight(_miniGunRight,range: 3f);
        }

        private static OmniLight3D AttachFlashLight(Marker3D parent, float range)
        {
            var light = new OmniLight3D
            {
                LightColor    = new Color(1f, 0.80f, 0.35f),
                LightEnergy   = 0f,
                OmniRange     = range,
                ShadowEnabled = false,
                LightBakeMode = Light3D.BakeMode.Disabled,
            };
            parent.AddChild(light);
            return light;
        }

        // Decays a flash timer and updates the light's energy.  Returns the
        // current energy so a single timer can drive two lights (mini guns).
        private static float TickFlash(ref float timer, OmniLight3D light, float peak, float dt)
        {
            if (timer <= 0f)
            {
                light.LightEnergy = 0f;
                return 0f;
            }
            float energy = peak * (timer / FlashDuration);
            light.LightEnergy = energy;
            timer = Mathf.Max(0f, timer - dt);
            return energy;
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // Tick muzzle flash lights for all fire modes (ghost tanks get flashes too).
            TickFlash(ref _cannonFlashTimer,  _cannonFlashLight,  CannonFlashPeak,  dt);
            TickFlash(ref _rocketFlashLTimer, _rocketFlashLLight, RocketFlashPeak,  dt);
            TickFlash(ref _rocketFlashRTimer, _rocketFlashRLight, RocketFlashPeak,  dt);
            float miniEnergy = TickFlash(ref _miniFlashTimer, _miniFlashLLight, MiniFlashPeak, dt);
            _miniFlashRLight.LightEnergy = miniEnergy;

            // Ghost tanks are driven entirely by network events — ignore local input.
            if (FireMode == WeaponFireMode.NetworkGhost) return;

            _cooldown -= dt;

            if (Input.IsActionJustPressed(InputPrefix + "next_weapon"))
                CycleWeapon(1);

            // UnitCommander consumed this click for unit selection — skip fire.
            if (SuppressFireThisFrame)
            {
                SuppressFireThisFrame = false;
            }
            else
            {
                // Minigun: hold to fire. Rockets & shells: tap to fire.
                bool trigger = CurrentWeapon == WeaponType.MiniGun
                    ? Input.IsActionPressed(InputPrefix + "fire_weapon")
                    : Input.IsActionJustPressed(InputPrefix + "fire_weapon");

                if (trigger && _cooldown <= 0f)
                    Fire();
            }

            // AI fire request — checked after player input so cooldown applies equally.
            // Minigun is stuttered into bursts via _aiMinigunBurst; rockets and
            // shells are already naturally paced by their long cooldowns.
            if (AIFireRequested)
            {
                bool burstGate = CurrentWeapon != WeaponType.MiniGun || _aiMinigunBurst.Ready;
                if (burstGate && _cooldown <= 0f && Fire() && CurrentWeapon == WeaponType.MiniGun)
                    _aiMinigunBurst.ConsumeShot(GD.Randf());
                AIFireRequested = false;
            }

            if (CurrentWeapon == WeaponType.MiniGun)
                _aiMinigunBurst.Tick(dt);
        }

        private void CycleWeapon(int dir)
        {
            CurrentWeapon = (WeaponType)(((int)CurrentWeapon + dir + 3) % 3);
            _cooldown = 0f; // allow immediate fire after switching
        }

        // Returns true when a shot was actually spawned. Used by the AI burst
        // pacer so it only consumes a burst slot when a bullet truly flew.
        private bool Fire()
        {
            Vector3? turretFwd = _turret?.GetAimForward();

            switch (CurrentWeapon)
            {
                case WeaponType.MiniGun:
                    if (MiniGunAmmo <= 0) return false;
                    // Minigun tracks the turret aim direction by up to 5% so bullets
                    // subtly follow the player's aim without being fully turret-aimed.
                    Vector3? miniAim = null;
                    if (turretFwd.HasValue)
                    {
                        Vector3 tankFwd = -GlobalBasis.Z;
                        miniAim = tankFwd.Lerp(turretFwd.Value, 0.05f).Normalized();
                    }
                    SpawnProjectile(_miniGunLeft,  ProjectileKind.Bullet, aimOverride: miniAim);
                    SpawnProjectile(_miniGunRight, ProjectileKind.Bullet, aimOverride: miniAim);
                    MiniGunAmmo = Math.Max(0, MiniGunAmmo - 2);
                    _cooldown = MiniGunInterval;
                    _miniFlashTimer = FlashDuration;
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Bullet, GlobalPosition);
                    break;

                case WeaponType.Rocket:
                    // Rockets fire along turret aim and curve toward the crosshair target.
                    if (RocketAmmo <= 0) return false;
                    var rocketOrigin = _rocketAlternate ? _rocketRight : _rocketLeft;
                    SpawnProjectile(rocketOrigin, ProjectileKind.Rocket,
                                    aimOverride: turretFwd,
                                    guidedTarget: AimTarget);
                    if (_rocketAlternate)
                        _rocketFlashRTimer = FlashDuration;
                    else
                        _rocketFlashLTimer = FlashDuration;
                    _rocketAlternate = !_rocketAlternate;
                    RocketAmmo = Math.Max(0, RocketAmmo - 1);
                    _cooldown = RocketInterval;
                    _tankBody?.ApplyCentralImpulse(_tankBody.GlobalBasis.Z * 3f);
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Rocket, GlobalPosition);
                    break;

                case WeaponType.TankShell:
                    // Cannon shell fires along turret aim direction.
                    if (TankShellAmmo <= 0) return false;
                    SpawnProjectile(_cannon, ProjectileKind.Shell, aimOverride: turretFwd);
                    TankShellAmmo = Math.Max(0, TankShellAmmo - 1);
                    _cooldown = ShellInterval;
                    _cannonFlashTimer = FlashDuration;
                    // Spring-lag camera turns this backward shove into a visible kick.
                    _tankBody?.ApplyCentralImpulse(_tankBody.GlobalBasis.Z * 4f);
                    AudioManager.Instance?.PlayWeaponFire(ProjectileKind.Shell, GlobalPosition);
                    break;
            }
            return true;
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
