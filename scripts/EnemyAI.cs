using Godot;
using HoverTank.Network;

namespace HoverTank
{
    /// <summary>
    /// AI controller for enemy tanks. Attach as a child of a HoverTank node
    /// that has IsEnemy=true. Each physics tick it generates a TankInput and
    /// directly sets turret aim on the sibling TurretController.
    ///
    /// Difficulty is configured per-wave by WaveManager before the tank
    /// enters the scene tree.
    /// </summary>
    public partial class EnemyAI : Node
    {
        // ── Difficulty parameters (set by WaveManager) ───────────────────────
        // Random yaw noise added to aim direction. 0=perfect, 1=very inaccurate.
        [Export] public float AimAccuracy   = 0.40f;
        // Distance (m) at which the enemy stops advancing and holds position.
        [Export] public float EngageRange   = 50f;
        // Weapon assigned to this enemy.
        [Export] public WeaponType PreferredWeapon = WeaponType.MiniGun;
        // When true, enemy leads the target to compensate for bullet travel time.
        [Export] public bool LeadTarget     = false;
        // Maximum angle error (radians) allowed before firing.
        [Export] public float FireAngleThreshold = 0.30f;

        // ── Minigun burst pacing ─────────────────────────────────────────────
        // Number of trigger-pulls per burst. WeaponManager converts each pull
        // into 2 bullets for the minigun.
        [Export] public int   BurstLength       = 6;
        // Pause between bursts (seconds). A small random jitter is added.
        [Export] public float BurstRestSeconds  = 1.1f;

        // ── Internal refs ────────────────────────────────────────────────────
        private HoverTank         _tank    = null!;
        private TurretController  _turret  = null!;
        private WeaponManager     _weapons = null!;

        // Smoothed noise offset so aim drifts rather than jitters.
        private float _noiseYaw;
        private float _noisePitch;
        private float _noiseDriftTimer;

        // Burst state — only used for the minigun.
        private int   _burstShotsLeft;
        private float _burstRestTimer;

        public override void _Ready()
        {
            _tank    = GetParent<HoverTank>();
            _turret  = GetParent().GetNode<TurretController>("Turret");
            _weapons = GetParent().GetNode<WeaponManager>("WeaponManager");

            _weapons.SelectWeapon(PreferredWeapon);
            // Enemies have unlimited ammo — they're not a resource-management challenge.
            _weapons.MiniGunAmmo   = 9999;
            _weapons.RocketAmmo    = 9999;
            _weapons.TankShellAmmo = 9999;
        }

        public override void _PhysicsProcess(double delta)
        {
            HoverTank? player = FindPlayer();
            if (player == null || _tank.Health <= 0f) return;

            Vector3 toPlayer    = player.GlobalPosition - _tank.GlobalPosition;
            float   dist        = toPlayer.Length();

            UpdateAimNoise((float)delta);
            UpdateTurretAim(toPlayer, player, dist);

            TankInput input = BuildMovementInput(toPlayer, dist);
            _tank.SetInput(input);

            TryFire(player, dist);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private HoverTank? FindPlayer()
        {
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is HoverTank tank && !tank.IsEnemy && !tank.IsFriendlyAI && tank.Health > 0f)
                    return tank;
            }
            return null;
        }

        // Drift the aim noise slowly so the enemy's accuracy feels organic.
        private void UpdateAimNoise(float delta)
        {
            _noiseDriftTimer -= delta;
            if (_noiseDriftTimer <= 0f)
            {
                float spread = AimAccuracy * Mathf.Pi * 0.5f; // max half-PI at worst
                _noiseYaw    = (GD.Randf() * 2f - 1f) * spread;
                _noisePitch  = (GD.Randf() * 2f - 1f) * spread * 0.4f;
                _noiseDriftTimer = 0.25f + GD.Randf() * 0.4f; // re-sample every ~0.25–0.65 s
            }
        }

        private void UpdateTurretAim(Vector3 toPlayer, HoverTank player, float dist)
        {
            Vector3 aimAt = player.GlobalPosition;

            // Lead targeting: predict where player will be when projectile arrives.
            if (LeadTarget && dist > 0.1f)
            {
                var kind = PreferredWeapon == WeaponType.MiniGun  ? ProjectileKind.Bullet :
                           PreferredWeapon == WeaponType.Rocket    ? ProjectileKind.Rocket :
                                                                     ProjectileKind.Shell;
                var (speed, _, _) = Projectile.GetStats(kind);
                float tof = dist / speed;
                aimAt += player.LinearVelocity * tof;
            }

            Vector3 aimDir = (aimAt - _tank.GlobalPosition).Normalized();

            float targetYaw   = Mathf.Atan2(-aimDir.X, -aimDir.Z) + _noiseYaw;
            float targetPitch = Mathf.Asin(Mathf.Clamp(aimDir.Y, -1f, 1f)) + _noisePitch;

            _turret.TargetAimYaw   = targetYaw;
            _turret.TargetAimPitch = targetPitch;
        }

        private TankInput BuildMovementInput(Vector3 toPlayer, float dist)
        {
            // Point the tank body toward the player via auto-steer (AimYaw).
            float yawToPlayer = Mathf.Atan2(-toPlayer.X, -toPlayer.Z);

            float throttle = 0f;
            if (dist > EngageRange + 3f)
                throttle = 1.0f;   // advance
            else if (dist < EngageRange - 8f)
                throttle = -0.5f;  // slight reverse to maintain range

            return new TankInput
            {
                Throttle = throttle,
                Steer    = 0f,
                AimYaw   = yawToPlayer,
            };
        }

        private void TryFire(HoverTank player, float dist)
        {
            // Minigun: fire in bursts so the projectile count stays bounded and
            // enemies don't sound like a continuous buzzsaw. Rockets and shells
            // are naturally paced by WeaponManager's per-shot cooldowns.
            if (PreferredWeapon == WeaponType.MiniGun)
            {
                TickMinigunBurst((float)GetPhysicsProcessDeltaTime());
                if (_burstRestTimer > 0f || _burstShotsLeft <= 0) return;
            }

            if (dist > EngageRange) return;

            // Compare turret forward against the direction to the player.
            Vector3 turretFwd   = _turret.GetAimForward();
            Vector3 toPlayerDir = (player.GlobalPosition - _tank.GlobalPosition).Normalized();
            float   angleError  = turretFwd.AngleTo(toPlayerDir);

            if (angleError > FireAngleThreshold) return;

            _weapons.AIFireRequested = true;

            // Only consume a burst slot when the weapon is actually going to
            // fire this tick (off-cooldown). Otherwise every physics tick in
            // the spray would burn a slot and bursts would last one shot.
            if (PreferredWeapon == WeaponType.MiniGun && _weapons.ReadyToFire)
            {
                _burstShotsLeft--;
                if (_burstShotsLeft <= 0)
                    _burstRestTimer = BurstRestSeconds + GD.Randf() * 0.4f;
            }
        }

        // Reload the burst magazine once the rest timer runs out.
        private void TickMinigunBurst(float delta)
        {
            if (_burstShotsLeft > 0) return;
            if (_burstRestTimer > 0f) { _burstRestTimer -= delta; return; }
            _burstShotsLeft = BurstLength;
        }
    }
}
