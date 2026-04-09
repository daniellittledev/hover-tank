using Godot;
using HoverTank.Network;

namespace HoverTank
{
    public partial class HoverTank : RigidBody3D
    {
        // ── Hover spring ────────────────────────────────────────────────────
        // Target height the bottom of the tank floats above the ground (metres).
        [Export] public float HoverHeight = 1.0f;

        // Proportional term: force per metre of displacement from equilibrium.
        // At rest each ray carries mass*gravity/4 ≈ 12.3 N.
        // SpringStrength=300 gives a natural frequency of ~2.5 Hz — tank-like bob.
        [Export] public float SpringStrength = 300f;

        // Derivative term: opposes vertical velocity to damp oscillations.
        // Critical damping ≈ 2*sqrt(k*m) = 2*sqrt(300*5) ≈ 77.
        // 50 gives ~65% of critical — slight bounce that settles in ~2 cycles.
        [Export] public float SpringDamping = 50f;

        // ── Movement ────────────────────────────────────────────────────────
        // Forward/back thrust force (Newtons) applied in the tank's local frame.
        [Export] public float ThrustForce = 200f;

        // Yaw torque (N·m) applied around the world-up axis for turning.
        [Export] public float TurnTorque = 80f;

        // Speed cap (m/s) in the thrust direction — prevents endless acceleration.
        [Export] public float MaxSpeed = 12f;

        // ── Jump jets ───────────────────────────────────────────────────────
        // Instantaneous upward impulse (kg·m/s) on the first frame E is pressed.
        [Export] public float JumpImpulse = 8f;

        // Sustained upward force (N) each physics frame while E is held.
        [Export] public float JumpSustainForce = 120f;

        // ── Health ──────────────────────────────────────────────────────────
        public float MaxHealth = 100f;
        public float Health    = 100f;

        public void TakeDamage(float amount) =>
            Health = Mathf.Max(0f, Health - amount);

        // ── Auto-steer (Halo-style) ──────────────────────────────────────────
        // Proportional gain: torque per radian of yaw error.
        [Export] public float AutoSteerGain = 120f;
        // Derivative gain: damps yaw oscillation.
        [Export] public float AutoSteerDamp = 20f;

        // ── Weapons ──────────────────────────────────────────────────────────
        public WeaponManager? Weapons { get; private set; }

        // ── Camera + turret ──────────────────────────────────────────────────
        public FollowCamera?    AimCamera { get; private set; }
        private TurretController? _turret;

        // ── Internal ────────────────────────────────────────────────────────
        private RayCast3D[] _hoverRays = null!;
        private TankInput _currentInput;

        public override void _Ready()
        {
            // 3×3 impulse grid: front/middle/back rows × left/centre/right columns.
            _hoverRays = new[]
            {
                GetNode<RayCast3D>("HoverRayFL"),
                GetNode<RayCast3D>("HoverRayFC"),
                GetNode<RayCast3D>("HoverRayFR"),
                GetNode<RayCast3D>("HoverRayML"),
                GetNode<RayCast3D>("HoverRayMC"),
                GetNode<RayCast3D>("HoverRayMR"),
                GetNode<RayCast3D>("HoverRayBL"),
                GetNode<RayCast3D>("HoverRayBC"),
                GetNode<RayCast3D>("HoverRayBR"),
            };

            Weapons   = GetNodeOrNull<WeaponManager>("WeaponManager");
            AimCamera = GetNodeOrNull<FollowCamera>("CameraMount/Camera");
            _turret   = GetNodeOrNull<TurretController>("Turret");

            // Register so the HUD can find this tank by group
            AddToGroup("hover_tanks");
        }

        // Called by ClientSimulation or ServerSimulation before each physics tick.
        public void SetInput(TankInput input) => _currentInput = input;

        public override void _PhysicsProcess(double delta)
        {
            ProcessHoverForces();
            ProcessMovement(_currentInput);
            ProcessJumpJets(_currentInput);

            // Drive turret toward camera aim direction.
            if (_turret != null && AimCamera != null)
            {
                _turret.TargetAimYaw   = AimCamera.CurrentYaw;
                _turret.TargetAimPitch = AimCamera.CurrentPitch;
            }

            // Feed aim target to weapons so rockets know where to curve.
            // Null when no camera exists (server-side tanks, bots).
            if (Weapons != null)
                Weapons.AimTarget = AimCamera?.AimTarget;
        }

        // ────────────────────────────────────────────────────────────────────
        // Hover: independent spring-damper at each point of a 3×3 ray grid.
        //
        // Each RayCast3D casts 2.5 m downward in its local space. The "resting"
        // compression when hovering at HoverHeight is:
        //   equilibriumCompression = rayLength - HoverHeight
        // Displacement is how far from that equilibrium we currently are.
        // Force = SpringStrength * displacement  (spring, P term)
        //       - SpringDamping  * vertVelocity  (damper, D term)
        //
        // Force is clamped to ≥ 0 so it only pushes upward — gravity provides
        // the downward pull when the tank is above hover height.
        //
        // Each ray carries 1/9 of the total equilibrium load (mass*g/9 ≈ 5.4 N).
        // Applying force at the ray's world offset from CoM produces realistic
        // roll/pitch response — the 3×3 grid gives finer torque resolution over
        // uneven or crater-edged terrain compared to a 4-corner layout.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessHoverForces()
        {
            foreach (var ray in _hoverRays)
            {
                if (!ray.IsColliding()) continue;

                float rayLength = -ray.TargetPosition.Y; // 2.5
                float distToGround = ray.GlobalPosition.DistanceTo(ray.GetCollisionPoint());

                float compression = rayLength - distToGround;
                float equilibriumCompression = rayLength - HoverHeight;
                float displacement = compression - equilibriumCompression;

                // Point velocity at the ray origin (rigid body kinematics)
                Vector3 r = ray.GlobalPosition - GlobalPosition;
                float vertVelocity = (LinearVelocity + AngularVelocity.Cross(r)).Dot(Vector3.Up);

                float force = SpringStrength * displacement - SpringDamping * vertVelocity;
                if (force < 0f) force = 0f;

                // Each ray takes 1/9 of the total load at equilibrium
                ApplyForce(Vector3.Up * (force / 9f), r);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Movement: thrust along local -Z (Godot's forward), yaw torque for turns.
        // Also called directly during client reconciliation re-simulation.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessMovement(TankInput input)
        {
            if (input.Throttle != 0f)
            {
                // Forward is -Z in Godot; Throttle > 0 means forward.
                Vector3 thrustDir = -Basis.Z * Mathf.Sign(input.Throttle);
                float speedInDir = LinearVelocity.Dot(thrustDir);
                if (speedInDir < MaxSpeed)
                    ApplyCentralForce(thrustDir * ThrustForce * Mathf.Abs(input.Throttle));
            }

            // ── Halo-style auto-steer: PD controller toward camera yaw ───────
            // tankYaw uses Atan2(Basis.Z.X, Basis.Z.Z) — the angle that places the
            // camera directly behind this tank — matching FollowCamera's yaw=0
            // convention. This ensures zero error (and zero torque) when the camera
            // is centred behind the tank.
            float tankYaw  = Mathf.Atan2(Basis.Z.X, Basis.Z.Z);
            float yawError = MathUtils.AngleDiff(input.AimYaw, tankYaw);
            float yawRate  = AngularVelocity.Y;

            float autoTorque   = AutoSteerGain * yawError - AutoSteerDamp * yawRate;
            // A/D keys add a steering bias on top of auto-steer (fine-grained swerve).
            float manualTorque = TurnTorque * input.Steer;
            ApplyTorque(Vector3.Up * (autoTorque + manualTorque));
        }

        // ────────────────────────────────────────────────────────────────────
        // Jump jets: initial burst impulse on keydown + sustained force while held.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessJumpJets(TankInput input)
        {
            if (input.JumpJustPressed)
                ApplyCentralImpulse(Vector3.Up * JumpImpulse);

            if (input.JumpJet)
                ApplyCentralForce(Vector3.Up * JumpSustainForce);
        }

        // Called during client reconciliation: applies input forces without hover
        // (hover forces are environment-driven and don't need re-simulation).
        public void ApplyInputForces(TankInput input)
        {
            ProcessMovement(input);
            ProcessJumpJets(input);
        }
    }
}
