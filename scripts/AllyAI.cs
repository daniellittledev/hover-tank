using Godot;
using HoverTank.Network;

namespace HoverTank
{
    /// <summary>
    /// AI controller for player-allied tanks. Attach as a child named "AllyAI"
    /// of a HoverTank node that has IsFriendlyAI=true.
    ///
    /// Orders are set externally by UnitCommander. Each physics tick the AI
    /// generates a TankInput and drives the turret, depending on the active order.
    ///
    /// Allies opportunistically shoot any enemy in range regardless of order,
    /// except when explicitly ordered to AttackTarget (priority fire).
    /// </summary>
    public partial class AllyAI : Node
    {
        public enum AllyOrder { Idle, Follow, Hold, MoveToWaypoint, AttackTarget }

        // ── Orders (set by UnitCommander) ────────────────────────────────────
        public AllyOrder  CurrentOrder     { get; set; } = AllyOrder.Idle;
        // World-space destination for MoveToWaypoint.
        public Vector3    WaypointPosition { get; set; }
        // Explicit attack target (null clears on death).
        public HoverTank? AttackTarget     { get; set; }
        // World-space formation slot, updated every frame by UnitCommander while Following.
        public Vector3    FormationSlot    { get; set; }

        // ── Selection glow ───────────────────────────────────────────────────
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                ApplySelectionGlow(value);
            }
        }

        // ── Tuning ───────────────────────────────────────────────────────────
        // Distance (m) at which the ally engages targets opportunistically.
        [Export] public float EngageRange   = 45f;
        // Stop within this radius of a waypoint / formation slot.
        [Export] public float ArrivalRadius = 3f;
        // Aim noise: 0 = perfect, 1 = terrible. Allies are trained but not perfect.
        [Export] public float AimAccuracy   = 0.12f;

        // ── Internal refs ────────────────────────────────────────────────────
        private HoverTank        _tank    = null!;
        private TurretController _turret  = null!;
        private WeaponManager    _weapons = null!;

        // Read by UnitCommander to poll health for the unit-card HUD.
        public HoverTank Tank => _tank;

        // Smoothed aim noise so the gun drifts rather than jitters.
        private float _noiseYaw;
        private float _noisePitch;
        private float _noiseDriftTimer;

        public override void _Ready()
        {
            _tank    = GetParent<HoverTank>();
            _turret  = GetParent().GetNode<TurretController>("Turret");
            _weapons = GetParent().GetNode<WeaponManager>("WeaponManager");

            _weapons.SelectWeapon(WeaponType.MiniGun);
            _weapons.MiniGunAmmo   = 9999;
            _weapons.RocketAmmo    = 9999;
            _weapons.TankShellAmmo = 9999;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_tank.Health <= 0f) return;

            switch (CurrentOrder)
            {
                case AllyOrder.Idle:           ProcessIdle();               break;
                case AllyOrder.Follow:         ProcessFollow();             break;
                case AllyOrder.Hold:           ProcessHold();               break;
                case AllyOrder.MoveToWaypoint: ProcessMoveToWaypoint();     break;
                case AllyOrder.AttackTarget:   ProcessAttackTarget();       break;
            }
        }

        // ── Order handlers ────────────────────────────────────────────────────

        private void ProcessIdle()
        {
            _tank.SetInput(TankInput.Empty);
            TryFireAtNearestEnemy();
        }

        private void ProcessFollow()
        {
            MoveTowardPosition(FormationSlot);
            TryFireAtNearestEnemy();
        }

        private void ProcessHold()
        {
            _tank.SetInput(TankInput.Empty);
            TryFireAtNearestEnemy();
        }

        private void ProcessMoveToWaypoint()
        {
            float dist = _tank.GlobalPosition.DistanceTo(WaypointPosition);
            if (dist <= ArrivalRadius)
            {
                CurrentOrder = AllyOrder.Hold;
                _tank.SetInput(TankInput.Empty);
            }
            else
            {
                MoveTowardPosition(WaypointPosition);
            }
            TryFireAtNearestEnemy();
        }

        private void ProcessAttackTarget()
        {
            if (AttackTarget == null || AttackTarget.Health <= 0f)
            {
                CurrentOrder = AllyOrder.Hold;
                return;
            }

            Vector3 toTarget = AttackTarget.GlobalPosition - _tank.GlobalPosition;
            float dist = toTarget.Length();

            AimTurretAt(AttackTarget.GlobalPosition);

            if (dist > EngageRange)
                MoveTowardPosition(AttackTarget.GlobalPosition);
            else
                _tank.SetInput(new TankInput { AimYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z) });

            TryFireAt(AttackTarget, dist);
        }

        // ── Movement ─────────────────────────────────────────────────────────

        private void MoveTowardPosition(Vector3 target)
        {
            Vector3 toTarget = target - _tank.GlobalPosition;
            float dist = toTarget.Length();

            if (dist < 0.5f)
            {
                _tank.SetInput(TankInput.Empty);
                return;
            }

            float yaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);

            // Ease off throttle when nearly at destination to avoid overshooting.
            float throttle = dist < ArrivalRadius * 3f
                ? Mathf.Clamp(dist / (ArrivalRadius * 3f), 0.25f, 1f)
                : 1f;

            _tank.SetInput(new TankInput { Throttle = throttle, Steer = 0f, AimYaw = yaw });
        }

        // ── Shooting ─────────────────────────────────────────────────────────

        private void TryFireAtNearestEnemy()
        {
            HoverTank? nearest = FindNearestEnemy();
            if (nearest == null) return;
            float dist = _tank.GlobalPosition.DistanceTo(nearest.GlobalPosition);
            if (dist > EngageRange) return;
            AimTurretAt(nearest.GlobalPosition);
            TryFireAt(nearest, dist);
        }

        // Burst pacing lives in WeaponManager; this just signals "I want to fire".
        private void TryFireAt(HoverTank target, float dist)
        {
            if (dist > EngageRange) return;
            Vector3 turretFwd   = _turret.GetAimForward();
            Vector3 toTargetDir = (target.GlobalPosition - _tank.GlobalPosition).Normalized();
            if (turretFwd.AngleTo(toTargetDir) < 0.25f)
                _weapons.AIFireRequested = true;
        }

        private void AimTurretAt(Vector3 worldPos)
        {
            RefreshAimNoise();
            Vector3 dir = (worldPos - _tank.GlobalPosition).Normalized();
            _turret.TargetAimYaw   = Mathf.Atan2(-dir.X, -dir.Z) + _noiseYaw;
            _turret.TargetAimPitch = Mathf.Asin(Mathf.Clamp(dir.Y, -1f, 1f)) + _noisePitch;
        }

        private HoverTank? FindNearestEnemy()
        {
            HoverTank? best   = null;
            float      bestD  = float.MaxValue;
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is HoverTank t && t.IsEnemy && t.Health > 0f)
                {
                    float d = _tank.GlobalPosition.DistanceTo(t.GlobalPosition);
                    if (d < bestD) { bestD = d; best = t; }
                }
            }
            return best;
        }

        // ── Aim noise ─────────────────────────────────────────────────────────

        private void RefreshAimNoise()
        {
            _noiseDriftTimer -= (float)GetPhysicsProcessDeltaTime();
            if (_noiseDriftTimer > 0f) return;

            float spread   = AimAccuracy * Mathf.Pi * 0.25f;
            _noiseYaw      = (GD.Randf() * 2f - 1f) * spread;
            _noisePitch    = (GD.Randf() * 2f - 1f) * spread * 0.4f;
            _noiseDriftTimer = 0.3f + GD.Randf() * 0.4f;
        }

        // ── Selection glow ────────────────────────────────────────────────────

        private void ApplySelectionGlow(bool selected)
        {
            var body = _tank.GetNodeOrNull<MeshInstance3D>("Body");
            if (body == null) return;

            // Clone the current override material (or create a fresh one) so we
            // don't mutate any shared resource.
            var src = body.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
            var mat = src != null
                ? (StandardMaterial3D)src.Duplicate()
                : new StandardMaterial3D
                  {
                      AlbedoColor = new Color(0.15f, 0.75f, 0.25f),
                      Roughness   = 0.55f,
                      Metallic    = 0.55f,
                  };

            mat.EmissionEnabled          = selected;
            mat.Emission                 = selected ? new Color(0.1f, 1.0f, 0.35f) : Colors.Black;
            mat.EmissionEnergyMultiplier = 1.5f;
            body.SetSurfaceOverrideMaterial(0, mat);
        }
    }
}
