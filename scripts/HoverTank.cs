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
        // Yaw damping is owned entirely by AutoSteerDamp in the auto-steer controller.
        [Export] public float TiltDrag = 30f;

        // ── Self-righting (bottom-heavy behaviour) ──────────────────────
        // Restoring torque that rotates the tank's local +Y toward world +Y.
        // Gentle enough that a jump-jet flip or terrain jolt still lets the
        // tank leave vertical briefly, but strong enough that any upside-down
        // state recovers in a second or two. Computed as (localUp × worldUp),
        // whose magnitude is sin(tilt) — so the torque grows smoothly with
        // tilt, peaks at 90°, and *reverses sign past 180°*. To still self-
        // right from fully inverted, we add a small constant nudge when the
        // tank is within a few degrees of straight upside-down (sin→0 there).
        [Export] public float UprightGain = 25f;
        // When dot(localUp, worldUp) < this threshold (≈ inverted), add a
        // small nudge torque about the tank's local roll axis so the cross
        // product has something to grab onto.
        private const float InvertedThreshold = -0.9f;

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
        // Cap on commanded yaw rate (rad/s). The tank turns at this rate for any
        // meaningful heading error and only ramps down inside the settle band, so
        // steering does not bleed off as the hull closes on the target heading.
        [Export] public float AutoSteerMaxRate = 3.0f;
        // Heading error (rad) below which the commanded rate ramps down linearly to
        // zero. Above it, the full AutoSteerMaxRate is commanded.
        [Export] public float AutoSteerSettleBand = 0.25f;
        // Stiffness of the torque that drives actual yaw rate to the commanded rate.
        [Export] public float AutoSteerDamp = 65f;

        // ── Aim-driven body pitch ────────────────────────────────────────────
        // The hull tilts toward where the player looks so the gun (hull -Z)
        // elevates with the aim — previously only the camera pitched and the hull
        // stayed level. Two mechanisms cooperate:
        //
        //   • Grounded: bias each hover ray's *target height* so the spring grid
        //     settles the hull at an angle. Fighting the strong springs with a
        //     torque would need a violent (unstable) force, so we move the
        //     equilibrium instead. Front rays lift, rear rays sink for nose-up;
        //     for nose-down the rear lifts while the front is pinned at
        //     HoverHeight so it never dips below ~1 m.
        //   • Airborne (jets): the springs aren't touching ground, so a direct
        //     PD torque about the hull's pitch axis tilts the nose — strong
        //     enough to pitch up and over while the jets hold the tank aloft.
        //
        // FollowCamera.CurrentPitch convention: negative = look up, positive =
        // look down. We map |look| / LookPitchRef to a signed hull pitch
        // (+ve = nose up) clamped asymmetrically.
        [Export] public float LookPitchRef     = 0.6f;  // look angle mapped to a full clamp
        [Export] public float MaxPitchDown     = 0.18f; // nose-down clamp (front stays ~1 m)
        [Export] public float MaxPitchUpGround = 0.62f; // nose-up clamp, no jets (front ~2 m)
        [Export] public float MaxPitchUpAir    = 2.4f;  // nose-up clamp with jets (up and over)
        [Export] public float PitchAirGain     = 80f;   // P term for airborne pitch torque
        [Export] public float PitchAirDamp     = 10f;   // D term for airborne pitch torque

        // Min colliding hover rays to count as "grounded": at/above this the
        // hover bias owns pitch and the airborne torque is suppressed (it would
        // only fight the springs). Below it the torque takes over.
        private const int GroundedRayCount = 3;

        // Per-ray local Z (forward = -Z), cached so the hover bias can compute a
        // height offset per ray without re-reading the node transform each tick.
        private float[] _rayLocalZ = null!;
        // Hull pitch target for the grounded hover bias this tick (always within
        // ±90° so Tan stays well-defined). Set in _PhysicsProcess.
        private float _pitchBiasTarget;

        // ── Weapons ──────────────────────────────────────────────────────────
        public WeaponManager? Weapons { get; private set; }

        // ── Camera + turret ──────────────────────────────────────────────────
        public FollowCamera?    AimCamera { get; private set; }
        private TurretController? _turret;

        // ── TestDrive "feel" profile ─────────────────────────────────────────
        // The reference video's movement: fast, floaty skimming with the craft
        // banking into turns and throwing sparks as it carves the surface. This
        // is enabled ONLY for the player tank in the TestDrive sandbox (detected
        // from GameState in _Ready, mirroring TerrainGenerator's _infiniteMode) —
        // combat/multiplayer tanks keep the tuned default handling untouched.
        private bool _feelMode;
        // Peak cosmetic roll (radians) when carving a hard turn / strafe.
        [Export] public float MaxBank = 0.5f;
        // How strongly lateral motion + yaw rate map into bank angle.
        [Export] public float BankStrength = 0.09f;
        // Rate (per second) the visual roll eases toward its target — higher
        // snaps faster, lower floats more.
        [Export] public float BankResponse = 6f;
        // Current eased visual roll, applied to the interpolated Visual each frame.
        private float _bankAngle;
        // Ember/spark trail kicked up while skimming the ground at speed.
        private GpuParticles3D? _sparkTrail;
        private bool _grounded;

        // FOV widens with speed for a sense of acceleration; this is the extra
        // degrees added at full MaxSpeed on top of the base TestDrive FOV.
        [Export] public float MaxFovKick = 12f;
        private float _baseFov;
        // Eased 0..1 speed fraction driving FOV kick, speed lines and glow pulse.
        private float _speedIntensity;
        // Under-craft hover glow (boosted + speed-pulsed in feel mode).
        private OmniLight3D? _hoverGlowLight;
        private float _glowBase;
        // One-shot ember burst fired on a hard landing.
        private GpuParticles3D? _landingBurst;
        // Full-screen radial speed-line overlay material (intensity driven by speed).
        private ShaderMaterial? _speedLines;
        // Landing detection: previous grounded state + last tick's vertical velocity.
        private bool _wasGrounded;
        private float _prevVertVel;

        // ── Internal ────────────────────────────────────────────────────────
        private RayCast3D[] _hoverRays = null!;
        private TankInput _currentInput;
        private AudioStreamPlayer3D? _enginePlayer;
        private float _jetFuel = 1f; // 0 = empty, 1 = full

        // ── Visual interpolation ─────────────────────────────────────────────
        // The RigidBody's transform only updates at the 60 Hz physics rate, so
        // rendering it directly judders whenever the render frame and physics
        // tick drift out of phase. We render the meshes from a separate "Visual"
        // node whose transform we lerp between the last two physics transforms
        // each frame, using the engine's physics-tick fraction. This is the
        // standard fixed-timestep interpolation: ~1 tick (16 ms) of latency in
        // exchange for perfectly smooth motion at any refresh rate.
        //
        // Remote ghosts (Freeze == true) are positioned by RemoteEntityInterpolator
        // instead, so they skip this path and the Visual node simply tracks the body.
        private Node3D _visual = null!;
        private Transform3D _prevVisualXform;
        private Transform3D _curVisualXform;

        /// <summary>
        /// The interpolated, render-smooth transform of the tank's visuals.
        /// Camera and any view-following code should read this instead of
        /// <see cref="Node3D.GlobalTransform"/>, which steps at the physics rate.
        /// </summary>
        public Transform3D VisualTransform => _visual.GlobalTransform;

        /// <summary>
        /// Collapses the interpolation history to the current physics transform.
        /// Call after any teleport (reconcile snap, respawn, death impulse) so the
        /// Visual node doesn't smear across the discontinuity.
        /// </summary>
        public void ResetVisualInterpolation()
        {
            _prevVisualXform = _curVisualXform = GlobalTransform;
            if (_visual != null)
                _visual.GlobalTransform = GlobalTransform;
        }

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

            // Cache each ray's local Z (forward = -Z) for the aim-driven pitch
            // bias. Rays are direct children of the body, so Position is local.
            _rayLocalZ = new float[_hoverRays.Length];
            for (int i = 0; i < _hoverRays.Length; i++)
                _rayLocalZ[i] = _hoverRays[i].Position.Z;

            Weapons   = GetNodeOrNull<WeaponManager>("WeaponManager");
            AimCamera = GetNodeOrNull<FollowCamera>("CameraMount/Camera");
            _turret   = GetNodeOrNull<TurretController>("Visual/Turret");

            // Visual interpolation root (holds Body, Turret, Thruster, glow).
            _visual = GetNode<Node3D>("Visual");
            _curVisualXform = _prevVisualXform = GlobalTransform;

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

            // TestDrive sandbox: the human-driven tank gets the reference-video
            // movement feel. Mirrors TerrainGenerator's _infiniteMode gate.
            var gs = GameState.Instance;
            _feelMode = gs != null
                && gs.Mode == GameMode.SinglePlayer
                && gs.SinglePlayerMode == SinglePlayerMode.TestDrive
                && !IsEnemy && !IsFriendlyAI;
            if (_feelMode)
                ApplyTestDriveFeel();
        }

        // Reconfigures handling, camera framing and effects for the TestDrive
        // sandbox to match the reference video — fast floaty skimming, a lower
        // punchier chase camera, and an ember trail. Pure tuning + an added
        // particle node; no change to the physics model itself.
        private void ApplyTestDriveFeel()
        {
            // Quicker, faster, floatier than the combat default.
            ThrustForce      = 440f;
            MaxSpeed         = 28f;
            JumpImpulse      = 4.5f;
            JumpSustainForce = 26f;

            // Lower, closer, wider chase view for a sense of speed.
            if (AimCamera != null)
            {
                AimCamera.OrbitRadius       = 6.5f;
                AimCamera.OrbitCenterHeight = 1.1f;
                AimCamera.Fov               = 82f;
                _baseFov                    = 82f;
            }

            _sparkTrail = CreateSparkTrail();
            AddChild(_sparkTrail);
            _sparkTrail.Position = new Vector3(0f, -0.1f, 1.3f); // rear underside

            // One-shot ember burst, fired on hard landings (see UpdateFeelEffects).
            _landingBurst = CreateLandingBurst();
            AddChild(_landingBurst);

            // Boost the existing under-craft glow to a brighter teal so the hull
            // reads like the glowing craft in the reference; pulsed with speed.
            _hoverGlowLight = GetNodeOrNull<OmniLight3D>("Visual/HoverGlow/GlowLight");
            if (_hoverGlowLight != null)
            {
                _hoverGlowLight.LightColor  = new Color(0.40f, 0.85f, 1.0f);
                _glowBase                   = 4.0f;
                _hoverGlowLight.LightEnergy = _glowBase;
                _hoverGlowLight.OmniRange   = 6.0f;
            }

            _speedLines = CreateSpeedLineOverlay();
        }

        // Builds a full-screen radial speed-line overlay on its own CanvasLayer
        // (parented to the tank, so it lives and dies with the player) and returns
        // its ShaderMaterial. The tank pushes a 0..1 `intensity` into it each
        // frame; the lines are faint, edge-biased streaks that only show at speed.
        private ShaderMaterial CreateSpeedLineOverlay()
        {
            var shader = new Shader
            {
                Code = @"
shader_type canvas_item;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec3  line_color : source_color = vec3(0.85, 0.93, 1.0);

float hash(float n) { return fract(sin(n) * 43758.5453123); }

void fragment() {
    vec2  p   = UV - 0.5;
    float r   = length(p);
    float ang = atan(p.y, p.x) / 6.2831853 + 0.5;  // 0..1 around the circle
    float seg = floor(ang * 90.0);                  // 90 angular buckets
    float present = step(0.80, hash(seg));          // ~20% carry a streak
    float edge = smoothstep(0.16, 0.5, r);          // fade out toward centre
    float a = present * edge * intensity * 0.45;    // faint
    COLOR = vec4(line_color, a);
}
",
            };
            var mat = new ShaderMaterial { Shader = shader };

            var rect = new ColorRect { Material = mat, MouseFilter = Control.MouseFilterEnum.Ignore };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            var layer = new CanvasLayer { Layer = 5, Name = "SpeedLines" };
            layer.AddChild(rect);
            AddChild(layer);
            return mat;
        }

        // Orange→yellow ember burst that trails the craft. LocalCoords = false so
        // particles stay put in the world and streak out behind as the tank moves,
        // matching the carved-surface sparks in the reference. Toggled in
        // _PhysicsProcess by ground contact + horizontal speed.
        private static GpuParticles3D CreateSparkTrail()
        {
            var mat = new ParticleProcessMaterial
            {
                Direction          = new Vector3(0f, 0.5f, 1f), // up and aft
                Spread             = 22f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 9f,
                Gravity            = new Vector3(0f, -14f, 0f),  // embers arc back down
                ScaleMin           = 0.04f,
                ScaleMax           = 0.12f,
            };
            var ramp = new Gradient();
            ramp.SetColor(0, new Color(1.0f, 0.85f, 0.35f, 1.0f)); // hot yellow
            ramp.SetColor(1, new Color(1.0f, 0.35f, 0.04f, 0.0f)); // fading orange
            mat.ColorRamp = new GradientTexture1D { Gradient = ramp };

            return new GpuParticles3D
            {
                ProcessMaterial = mat,
                DrawPass1       = CreateEmberQuad(),
                Amount          = 48,
                Lifetime        = 0.7,
                LocalCoords     = false,
                Emitting        = false,
            };
        }

        // One-shot ember fan thrown outward + up on a hard landing. Restarted
        // (and repositioned to the ground contact) from UpdateFeelEffects.
        private static GpuParticles3D CreateLandingBurst()
        {
            var mat = new ParticleProcessMaterial
            {
                EmissionShape       = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.4f,
                Direction           = new Vector3(0f, 1f, 0f),
                Spread              = 78f,
                InitialVelocityMin  = 4f,
                InitialVelocityMax  = 12f,
                Gravity             = new Vector3(0f, -16f, 0f),
                ScaleMin            = 0.05f,
                ScaleMax            = 0.18f,
            };
            var ramp = new Gradient();
            ramp.SetColor(0, new Color(1.0f, 0.88f, 0.45f, 1.0f));
            ramp.SetColor(1, new Color(1.0f, 0.30f, 0.03f, 0.0f));
            mat.ColorRamp = new GradientTexture1D { Gradient = ramp };

            return new GpuParticles3D
            {
                ProcessMaterial = mat,
                DrawPass1       = CreateEmberQuad(),
                Amount          = 36,
                Lifetime        = 0.6,
                OneShot         = true,
                Explosiveness   = 0.95f,
                LocalCoords     = false,
                Emitting        = false,
            };
        }

        // Small additive billboard quad used by both ember effects: unshaded so
        // it ignores scene lighting, additive so it glows, vertex-colour driven
        // so each particle takes its colour + alpha fade from the process ramp.
        private static QuadMesh CreateEmberQuad()
        {
            var quad = new QuadMesh { Size = new Vector2(0.12f, 0.12f) };
            quad.Material = new StandardMaterial3D
            {
                ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode              = BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,
                BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            };
            return quad;
        }

        // Called by ClientSimulation or ServerSimulation before each physics tick.
        public void SetInput(TankInput input) => _currentInput = input;

        public override void _PhysicsProcess(double delta)
        {
            // Aim-driven pitch targets (computed once; consumed by the hover bias
            // and the airborne torque). lookFrac: +1 = full look-up, -1 = full down.
            float lookFrac = AimCamera != null
                ? Mathf.Clamp(-AimCamera.CurrentPitch / LookPitchRef, -1f, 1f)
                : 0f;
            // Grounded bias target — always uses the ground clamp so it stays
            // within ±90° (Tan-safe) even while the jets are held.
            _pitchBiasTarget = lookFrac >= 0f
                ? lookFrac * MaxPitchUpGround
                : lookFrac * MaxPitchDown;
            // Airborne torque target — the jets unlock the full look-up-and-over.
            float airPitchTarget = lookFrac >= 0f
                ? lookFrac * (_currentInput.JumpJet ? MaxPitchUpAir : MaxPitchUpGround)
                : lookFrac * MaxPitchDown;

            ProcessHoverForces();
            ProcessMovement(_currentInput);
            ProcessJumpJets(_currentInput);
            var av = AngularVelocity;
            ApplyTorque(new Vector3(-av.X * TiltDrag, 0f, -av.Z * TiltDrag));
            ProcessBodyPitchAir(airPitchTarget);
            ProcessSelfRighting();

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

            if (_feelMode)
                UpdateFeelEffects();

            // Record this tick's settled transform for render interpolation.
            _prevVisualXform = _curVisualXform;
            _curVisualXform  = GlobalTransform;
        }

        // TestDrive only: spray the ember trail while skimming the ground fast.
        // Ground contact is read from the hover rays (physics-accurate), so this
        // lives in _PhysicsProcess; the cosmetic bank is applied in _Process.
        private void UpdateFeelEffects()
        {
            int contacts = 0;
            foreach (var ray in _hoverRays)
                if (ray.IsColliding()) contacts++;
            _grounded = contacts >= GroundedRayCount;

            if (_sparkTrail != null)
            {
                float hSpeed = new Vector2(LinearVelocity.X, LinearVelocity.Z).Length();
                _sparkTrail.Emitting = _grounded && hSpeed > 6f;
            }

            // Hard-landing ember burst: fire the moment we regain ground contact
            // if we were dropping fast. _prevVertVel holds last tick's Y velocity,
            // captured before the springs arrest the fall, so it reads the true
            // impact speed. A 4 m/s gate keeps gentle skim-bounces from popping.
            if (_landingBurst != null && _grounded && !_wasGrounded && -_prevVertVel > 4f)
            {
                var mc = _hoverRays[4]; // HoverRayMC (centre)
                _landingBurst.GlobalPosition = mc.IsColliding()
                    ? mc.GetCollisionPoint()
                    : GlobalPosition;
                _landingBurst.Restart();
            }

            _wasGrounded = _grounded;
            _prevVertVel = LinearVelocity.Y;
        }

        // Render-frame interpolation: blend the visuals between the last two
        // physics transforms by the fraction of the way through the current
        // physics tick. Skipped for frozen bodies (remote ghosts / dead tanks),
        // which are positioned externally and whose Visual node tracks the body.
        public override void _Process(double delta)
        {
            if (Freeze) return;
            float fraction = (float)Engine.GetPhysicsInterpolationFraction();
            var xform = _prevVisualXform.InterpolateWith(_curVisualXform, fraction);

            // Cosmetic bank (TestDrive feel): roll the craft into turns. Driven by
            // sideways velocity in the hull frame plus the commanded yaw rate, so
            // it leans both when strafing and when auto-steering through a curve.
            // Composed on top of the interpolated pose here — never as physics —
            // so it stays purely visual and survives the per-frame interpolation.
            if (_feelMode)
            {
                // Right strafe (lateral>0) and right turn (yawRate<0 about +Y)
                // should both roll the right side (+X) down — a negative roll
                // about +Z. Hence -lateral and +yawRate combine with one sign.
                float lateral = LinearVelocity.Dot(GlobalBasis.X);          // +X = right
                float yawRate = AngularVelocity.Dot(GlobalBasis.Y);
                float target  = Mathf.Clamp(
                    (yawRate * 2.2f - lateral) * BankStrength, -MaxBank, MaxBank);
                _bankAngle = Mathf.Lerp(_bankAngle, target,
                    Mathf.Min(1f, BankResponse * (float)delta));
                // Roll about the hull's forward axis (local Z); forward is -Z.
                xform.Basis *= new Basis(new Vector3(0f, 0f, 1f), _bankAngle);
            }

            _visual.GlobalTransform = xform;

            if (_feelMode)
                UpdateFeelVisuals((float)delta);
        }

        // Render-frame speed reactions (TestDrive feel): a FOV kick, the speed-line
        // overlay, and an under-craft glow pulse, all scaled by an eased 0..1 speed
        // fraction. Visual only — no physics — so it's safe in _Process.
        private void UpdateFeelVisuals(float dt)
        {
            float hSpeed   = new Vector2(LinearVelocity.X, LinearVelocity.Z).Length();
            float targetFr = MaxSpeed > 8f
                ? Mathf.Clamp((hSpeed - 8f) / (MaxSpeed - 8f), 0f, 1f)
                : 0f;
            _speedIntensity = Mathf.Lerp(_speedIntensity, targetFr, Mathf.Min(1f, 5f * dt));

            if (AimCamera != null)
                AimCamera.Fov = Mathf.Lerp(
                    AimCamera.Fov, _baseFov + MaxFovKick * _speedIntensity, Mathf.Min(1f, 6f * dt));

            _speedLines?.SetShaderParameter("intensity", _speedIntensity);

            if (_hoverGlowLight != null)
                _hoverGlowLight.LightEnergy = _glowBase + _speedIntensity * 2.5f;
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
            // Aim-driven pitch bias: convert the target hull pitch into a per-ray
            // target-height offset. forward = -Z, so a ray at local z is lifted by
            // -z*slope — front rays (z<0) rise and rear rays (z>0) sink for nose-up.
            // For nose-down (slope<0) the front rays are pinned at HoverHeight so
            // the nose can't drop below ~1 m; the rear lifts to make the angle.
            float pitchSlope = Mathf.Tan(_pitchBiasTarget);

            for (int i = 0; i < _hoverRays.Length; i++)
            {
                var ray = _hoverRays[i];
                if (!ray.IsColliding()) continue;

                float zr = _rayLocalZ[i];
                float targetHeight = HoverHeight - zr * pitchSlope;
                if (pitchSlope < 0f && zr < 0f)
                    targetHeight = HoverHeight; // pin the front: never dip below ~1 m
                if (targetHeight < 0f)
                    targetHeight = 0f; // a sinking rear simply gets no support

                float rayLength = -ray.TargetPosition.Y; // 2.5
                float distToGround = ray.GlobalPosition.DistanceTo(ray.GetCollisionPoint());

                float compression = rayLength - distToGround;
                float equilibriumCompression = rayLength - targetHeight;
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
            //
            // Torque is applied about the hull's own up axis (GlobalBasis.Y), not
            // world up: when the hull is tilted far from vertical a world-Y torque
            // leaks into roll/pitch and fights ProcessSelfRighting. Damping reads
            // the yaw rate about that same axis. For an upright tank GlobalBasis.Y
            // == world up, so normal driving is unchanged.
            Vector3 yawAxis = GlobalBasis.Y;
            float   yawRate = AngularVelocity.Dot(yawAxis);

            // Command a constant turn rate toward the target heading, saturated at
            // AutoSteerMaxRate. Only within AutoSteerSettleBand does the commanded
            // rate taper, so the tank keeps turning at full speed instead of slowing
            // as the error shrinks. The torque is a stiff drive toward that rate,
            // which also damps any residual yaw when the commanded rate is zero.
            //
            // The heading is undefined when the forward axis nears vertical
            // (Atan2(0,0)→0). Command zero rate there so a garbage error can't kick
            // the already-tilted tank into a spin; the drive still bleeds off any
            // residual yaw rate.
            float desiredRate = 0f;
            if (MathUtils.TryGetHeading(Basis, out float tankYaw))
            {
                float error = MathUtils.AngleDiff(input.AimYaw, tankYaw);
                float scale = AutoSteerSettleBand > 0f
                    ? Mathf.Clamp(error / AutoSteerSettleBand, -1f, 1f)
                    : Mathf.Sign(error);
                desiredRate = scale * AutoSteerMaxRate;
            }

            ApplyTorque(yawAxis * (AutoSteerDamp * (desiredRate - yawRate)));

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

        // ────────────────────────────────────────────────────────────────────
        // Airborne aim pitch: a PD torque about the hull's pitch axis that tilts
        // the nose toward the look direction. Only runs when the tank is off the
        // ground — grounded pitch is owned by the hover-height bias, and a torque
        // there would just fight the (far stronger) springs. With the jets held,
        // the clamp opens up to MaxPitchUpAir so the tank can pitch up and over.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessBodyPitchAir(float targetPitch)
        {
            if (AimCamera == null) return; // AI/server tanks don't aim the hull

            int contacts = 0;
            foreach (var ray in _hoverRays)
                if (ray.IsColliding()) contacts++;
            if (contacts >= GroundedRayCount) return;

            // Current hull pitch: +ve = nose up. forward = -Z.
            Vector3 forward = -GlobalBasis.Z;
            float currentPitch = Mathf.Atan2(forward.Y,
                new Vector2(forward.X, forward.Z).Length());

            // Torque about +X pitches the nose up (right-hand rule), matching the
            // +ve = nose-up convention, so a positive error drives toward target.
            Vector3 pitchAxis = GlobalBasis.X;
            float pitchRate = AngularVelocity.Dot(pitchAxis);
            float torque = (targetPitch - currentPitch) * PitchAirGain
                         - pitchRate * PitchAirDamp;
            ApplyTorque(pitchAxis * torque);
        }

        // Called during client reconciliation: applies input forces without hover
        // (hover forces are environment-driven and don't need re-simulation).
        public void ApplyInputForces(TankInput input)
        {
            ProcessMovement(input);
            ProcessJumpJets(input);
        }

        // ────────────────────────────────────────────────────────────────────
        // Self-righting: torque that rotates the tank's local +Y toward world
        // +Y. Combined with the existing TiltDrag (D-term on roll/pitch rate),
        // this forms a PD controller that keeps the tank upright long-term but
        // lets it tumble freely in the short term — so a jump-jet backflip
        // plays out fully before the tank rolls itself back over.
        // ────────────────────────────────────────────────────────────────────
        private void ProcessSelfRighting()
        {
            Vector3 localUp = GlobalBasis.Y;
            // (localUp × worldUp) is a world-space vector whose magnitude is
            // sin(tilt) and whose direction is the axis of shortest rotation
            // from localUp to worldUp.
            Vector3 axis = localUp.Cross(Vector3.Up);

            // Fully inverted: cross product collapses to zero and provides no
            // restoring direction. Nudge about the tank's local roll axis so
            // the P-term has something to work with on the next tick.
            if (localUp.Y < InvertedThreshold && axis.LengthSquared() < 0.01f)
                axis = GlobalBasis.Z;

            ApplyTorque(axis * UprightGain);
        }
    }
}
