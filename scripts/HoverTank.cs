using Godot;
using HoverTank.Network;

namespace HoverTank
{
    public partial class HoverTank : RigidBody3D
    {
        // ── Team flags ───────────────────────────────────────────────────────
        // Set to true before AddChild to configure this tank as an AI enemy:
        // disables the player camera and suppresses camera-driven turret aim.
        [Export] public bool IsEnemy = false;
        // Set to true before AddChild for a player-allied AI tank (controlled
        // by AllyAI + UnitCommander). Like IsEnemy, disables the player camera.
        [Export] public bool IsFriendlyAI = false;

        // Emitted once when Health first reaches zero.
        [Signal] public delegate void DiedEventHandler();

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

        // Speed cap (m/s) in the thrust direction — prevents endless acceleration.
        [Export] public float MaxSpeed = 12f;

        // ── Angular drag ────────────────────────────────────────────────
        // Roll/pitch counter-torque only — kills tumbling from terrain jolts.
        // Yaw damping is owned entirely by AutoSteerDamp in the PD controller.
        [Export] public float TiltDrag = 30f;

        // ── Jump jets ───────────────────────────────────────────────────────
        // Instantaneous upward impulse (kg·m/s) on the first frame E is pressed.
        [Export] public float JumpImpulse = 3f;

        // Sustained upward force (N) at normal power.
        // First second: 2× this value.  Depleted: 0.1× (sputter only).
        [Export] public float JumpSustainForce = 20f;

        // Seconds of continuous use before fuel hits zero (1 s max + 4 s normal).
        [Export] public float JumpFuelDuration = 5f;

        // Seconds to recharge from empty to full when jets are idle.
        [Export] public float JumpRechargeTime = 4f;

        // ── Health ──────────────────────────────────────────────────────────
        public float MaxHealth = 100f;
        public float Health    = 100f;

        private bool _died = false;

        public void TakeDamage(float amount)
        {
            if (_died) return;
            Health = Mathf.Max(0f, Health - amount);

            // Shake the player camera proportionally to incoming damage.
            // Bullet (5 dmg) → ~0.012 m, Rocket (50) → 0.125 m, Shell (100) → 0.25 m.
            AimCamera?.AddShake(Mathf.Clamp(amount * 0.0025f, 0.01f, 0.25f));

            if (Health == 0f)
            {
                _died = true;
                AudioManager.Instance?.PlayExplosion(GlobalPosition);
                SpawnDestructionEffect();
                EmitSignal(SignalName.Died);
            }
        }

        private void SpawnDestructionEffect()
        {
            var scene = GetTree().CurrentScene;

            // ── Large one-shot debris burst ───────────────────────────────────
            var burst = new GpuParticles3D();
            var bmat = new ParticleProcessMaterial
            {
                Direction          = Vector3.Up,
                Spread             = 180f,
                InitialVelocityMin = 6f,
                InitialVelocityMax = 25f,
                Gravity            = new Vector3(0f, -6f, 0f),
                ScaleMin           = 0.15f,
                ScaleMax           = 0.60f,
            };
            var bgrad = new Gradient();
            bgrad.SetColor(0, new Color(1f, 0.50f, 0.05f, 1.0f));
            bgrad.SetColor(1, new Color(0.1f, 0.10f, 0.10f, 0.0f));
            bmat.ColorRamp      = new GradientTexture1D { Gradient = bgrad };
            burst.ProcessMaterial = bmat;
            burst.Amount          = 80;
            burst.Lifetime        = 1.5;
            burst.OneShot         = true;
            burst.Explosiveness   = 0.9f;
            burst.LocalCoords     = false;
            burst.Emitting        = true;
            scene.AddChild(burst);
            burst.GlobalPosition  = GlobalPosition;

            // ── Lingering smoke column ────────────────────────────────────────
            var smoke = new GpuParticles3D();
            var smat = new ParticleProcessMaterial
            {
                Direction          = Vector3.Up,
                Spread             = 30f,
                InitialVelocityMin = 1f,
                InitialVelocityMax = 4f,
                Gravity            = new Vector3(0f, 0.8f, 0f),
                ScaleMin           = 0.30f,
                ScaleMax           = 0.90f,
            };
            var sgrad = new Gradient();
            sgrad.SetColor(0, new Color(0.20f, 0.20f, 0.20f, 0.70f));
            sgrad.SetColor(1, new Color(0.40f, 0.40f, 0.40f, 0.00f));
            smat.ColorRamp       = new GradientTexture1D { Gradient = sgrad };
            smoke.ProcessMaterial  = smat;
            smoke.Amount           = 25;
            smoke.Lifetime         = 3.0;
            smoke.LocalCoords      = false;
            smoke.Emitting         = true;
            scene.AddChild(smoke);
            smoke.GlobalPosition   = GlobalPosition + Vector3.Up * 0.5f;

            // ── Bright flash light ────────────────────────────────────────────
            var flash = new OmniLight3D
            {
                LightColor    = new Color(1f, 0.65f, 0.20f),
                LightEnergy   = 20f,
                OmniRange     = 18f,
                ShadowEnabled = false,
                LightBakeMode = Light3D.BakeMode.Disabled,
            };
            scene.AddChild(flash);
            flash.GlobalPosition = GlobalPosition;

            // Cleanup timers
            var flashTimer = GetTree().CreateTimer(0.15);
            flashTimer.Timeout += flash.QueueFree;
            var burstTimer = GetTree().CreateTimer(3.5);
            burstTimer.Timeout += burst.QueueFree;
            // Stop smoke after 6 s, let particles finish their own lifetime
            var smokeStop  = GetTree().CreateTimer(6.0);
            smokeStop.Timeout  += () => smoke.Emitting = false;
            var smokeClean = GetTree().CreateTimer(10.0);
            smokeClean.Timeout += smoke.QueueFree;

            // ── Physics: throw the tank body upward and spin it ───────────────
            ApplyCentralImpulse(Vector3.Up * 12f + new Vector3(
                (float)GD.RandRange(-2.0, 2.0), 0f, (float)GD.RandRange(-2.0, 2.0)));
            ApplyTorqueImpulse(new Vector3(
                (float)GD.RandRange(-15.0, 15.0),
                (float)GD.RandRange(-15.0, 15.0),
                (float)GD.RandRange(-15.0, 15.0)));
        }

        // ── Auto-steer (Halo-style) ──────────────────────────────────────────
        // Proportional gain: torque per radian of yaw error.
        [Export] public float AutoSteerGain = 120f;
        // Derivative gain: sole source of yaw damping (no separate angular drag on Y).
        [Export] public float AutoSteerDamp = 65f;

        // ── Weapons ──────────────────────────────────────────────────────────
        public WeaponManager? Weapons { get; private set; }

        // ── Camera + turret ──────────────────────────────────────────────────
        public FollowCamera?    AimCamera { get; private set; }
        private TurretController? _turret;

        // ── Internal ────────────────────────────────────────────────────────
        private RayCast3D[] _hoverRays = null!;
        private TankInput _currentInput;
        private AudioStreamPlayer3D? _enginePlayer;
        private float _jetFuel = 1f; // 0 = empty, 1 = full

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

            // AI-driven tanks (enemy or friendly) don't need the player camera.
            if (IsEnemy || IsFriendlyAI)
            {
                var camMount = GetNodeOrNull("CameraMount");
                camMount?.QueueFree();
                AimCamera = null;
            }

            // Register so the HUD and AI can find tanks by group
            AddToGroup("hover_tanks");
            if (IsFriendlyAI)
                AddToGroup("ally_tanks");

            // Attach a looping engine-hum player. Works for both player and enemy
            // tanks — 3D attenuation handles distance falloff for enemy units.
            if (AudioManager.Instance != null)
            {
                _enginePlayer = AudioManager.Instance.CreateEnginePlayer();
                AddChild(_enginePlayer);
            }
        }

        // Called by ClientSimulation or ServerSimulation before each physics tick.
        public void SetInput(TankInput input) => _currentInput = input;

        public override void _PhysicsProcess(double delta)
        {
            ProcessHoverForces();
            ProcessMovement(_currentInput);
            ProcessJumpJets(_currentInput);
            var av = AngularVelocity;
            ApplyTorque(new Vector3(-av.X * TiltDrag, 0f, -av.Z * TiltDrag));

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

            UpdateEngineAudio();
        }

        private void UpdateEngineAudio()
        {
            if (_enginePlayer == null) return;
            AudioManager.Instance!.UpdateEngineThrottle(
                _enginePlayer, Mathf.Abs(_currentInput.Throttle), _currentInput.JumpJet);
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

                // Point velocity at the ray origin (rigid body kinematics).
                // Only the Y component is needed (dotting with Vector3.Up), so
                // compute it directly instead of constructing intermediate vectors.
                Vector3 r = ray.GlobalPosition - GlobalPosition;
                float vertVelocity = LinearVelocity.Y + AngularVelocity.Z * r.X - AngularVelocity.X * r.Z;

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

            ApplyTorque(Vector3.Up * (AutoSteerGain * yawError - AutoSteerDamp * yawRate));

            if (input.Steer != 0f)
            {
                // Positive Steer = A (left), negative = D (right).
                Vector3 strafeDir = -Basis.X * Mathf.Sign(input.Steer);
                float speedInDir = LinearVelocity.Dot(strafeDir);
                if (speedInDir < MaxSpeed)
                    ApplyCentralForce(strafeDir * ThrustForce * Mathf.Abs(input.Steer));
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Jump jets: fuel-based power curve.
        //
        // Fuel drains linearly while E is held; recharges when released.
        //   fuel > 0.8  →  first ~1 s of use  →  2× force  (max power)
        //   fuel > 0    →  next ~4 s of use   →  1× force  (normal)
        //   fuel == 0   →  depleted            →  0.1× force (sputter)
        //
        // At JumpFuelDuration=5 s drain rate = 0.2/s, so full→0.8 takes
        // exactly 1 s. Recharge at JumpRechargeTime=4 s is faster than drain.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessJumpJets(TankInput input)
        {
            float dt = (float)GetPhysicsProcessDeltaTime();

            if (input.JumpJet)
            {
                float fuelBefore = _jetFuel;
                _jetFuel = Mathf.Max(0f, _jetFuel - dt / JumpFuelDuration);

                if (input.JumpJustPressed && fuelBefore > 0f)
                    ApplyCentralImpulse(Vector3.Up * JumpImpulse);

                float power = _jetFuel > 0.8f ? 2.0f
                            : _jetFuel > 0f   ? 1.0f
                            :                   0.1f;
                ApplyCentralForce(Vector3.Up * JumpSustainForce * power);
            }
            else
            {
                _jetFuel = Mathf.Min(1f, _jetFuel + dt / JumpRechargeTime);
            }
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
