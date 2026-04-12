using Godot;

namespace HoverTank
{
    /// <summary>
    /// Halo-style orbital camera with a toggleable Battlezone-style first-person mode.
    ///
    /// Third-person (default): orbits around the tank at a fixed radius. Mouse X/Y
    /// (or right analog stick) rotates the camera independently from the tank body,
    /// and the tank auto-steers toward the camera's yaw direction.
    ///
    /// First-person (F1): camera sits at turret height. Pitch is free within
    /// FpsPitchMin/Max so the reticle aims vertically. Yaw is clamped to a cone
    /// around the hull heading (FpsYawConeHalfWidth), so the reticle can lead
    /// the hull by a fixed amount; the existing auto-steer PD in HoverTank pulls
    /// the hull along at its natural turn rate. Press F2 to return to TPS.
    ///
    /// Exposes CurrentYaw, CurrentPitch, and AimTarget so LocalInputHandler and
    /// WeaponManager can read the aiming state — both modes share the same state.
    ///
    /// Yaw convention: CurrentYaw=0 places the camera at +Z from the orbit
    /// centre, which is directly behind a tank whose forward axis is -Z. This
    /// convention is shared by HoverTank.ProcessMovement and TurretController
    /// so that AimYaw==tankYaw produces zero auto-steer torque.
    ///
    /// Attach to the Camera3D child of CameraMount inside HoverTank.tscn.
    /// </summary>
    public partial class FollowCamera : Camera3D
    {
        public enum ViewMode { ThirdPerson, FirstPerson }

        // Distance from orbit centre to camera (metres).
        [Export] public float OrbitRadius = 8f;

        // Height above tank origin that the camera orbits around.
        [Export] public float OrbitCenterHeight = 1.5f;

        // How fast camera position spring-follows the tank (per second).
        // Must be high enough that lateral strafe doesn't leave the orbit centre
        // behind the tank — otherwise the crosshair (camera -Z) drifts off the
        // tank's actual forward axis and no longer matches where bullets go.
        // 25 gives ~40 ms time constant → ~0.5 m lag at max strafe speed.
        [Export] public float PositionLag = 25.0f;

        // Mouse sensitivity (radians per pixel).
        [Export] public float MouseSensitivity = 0.003f;

        // Right-stick sensitivity (radians per second at full deflection).
        [Export] public float StickSensitivity = 2.5f;

        // Pitch limits (radians). Negative = look up, positive = look down.
        [Export] public float PitchMin = -0.26f;  // ~-15°
        [Export] public float PitchMax =  0.61f;  //  ~35°

        // ── First-person mode ───────────────────────────────────────────────
        // Eye position in tank-local space (turret height, slightly aft of barrel).
        [Export] public Vector3 FpsEyeOffset = new Vector3(0f, 0.7f, 0.1f);

        // How far the reticle yaw may lead the hull yaw (radians). Existing
        // HoverTank auto-steer pulls the hull toward CurrentYaw, so this
        // effectively sets the Battlezone "reticle lead" cone.
        [Export] public float FpsYawConeHalfWidth = 0.35f; // ~20°

        // Pitch limits used in FPS (wider than TPS since there's no chase geometry).
        [Export] public float FpsPitchMin = -0.6f; // ~-34°, look up
        [Export] public float FpsPitchMax =  0.6f; // ~ 34°, look down

        // NodePath to the owning tank. Default works for Camera→CameraMount→HoverTank
        // but can be overridden in the Inspector if the hierarchy changes.
        [Export] NodePath _tankPath = "../..";

        // World-space camera yaw. Convention: 0 = camera at +Z from orbit centre
        // (behind a default -Z-facing tank). Read by LocalInputHandler → TankInput.
        public float CurrentYaw   { get; private set; }
        // Camera pitch (radians). Read by TurretController for barrel elevation.
        public float CurrentPitch { get; private set; }
        // World-space point the crosshair hits. Read by WeaponManager for rockets.
        public Vector3 AimTarget  { get; private set; }

        private HoverTank? _tank;
        private Vector3 _smoothOrbitCenter;

        // Active view mode. Defaults to ThirdPerson so existing behaviour is
        // unchanged until the player presses F1.
        private ViewMode _mode = ViewMode.ThirdPerson;

        // Turret mesh reference, cached so we can hide it in FPS to avoid
        // clipping the in-cockpit camera. Turret rotation logic (TurretController)
        // stays active so weapons keep aiming correctly — only the mesh toggles.
        private Node3D? _turretNode;

        // Cached to avoid per-frame allocations in UpdateAimTarget.
        private readonly Godot.Collections.Array<Rid> _excludeRids = new();
        private PhysicsRayQueryParameters3D _aimQuery = null!;

        // Camera shake state. Amplitude decays linearly to zero each frame.
        private float _shakeAmplitude;
        private const float ShakeDecay = 1.0f; // amplitude units lost per second

        // Adds trauma to the camera. Amplitude represents max offset in metres.
        // Calling multiple times keeps the largest pending value.
        public void AddShake(float amplitude)
        {
            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
        }

        public override void _Ready()
        {
            _tank = GetNodeOrNull<HoverTank>(_tankPath);
            if (_tank != null)
            {
                // Initialise yaw so the camera starts directly behind the tank
                // with no auto-steer torque on the first frame.
                // Convention: yaw = Atan2(Basis.Z.X, Basis.Z.Z) (backward direction).
                CurrentYaw   = Mathf.Atan2(_tank.Basis.Z.X, _tank.Basis.Z.Z);
                CurrentPitch = 0.40f; // ~23° downward — matches old CameraMount tilt
                _smoothOrbitCenter = _tank.GlobalPosition + Vector3.Up * OrbitCenterHeight;
                _excludeRids.Add(_tank.GetRid());

                // Cache turret ref so FPS mode can hide the mesh without a per-frame lookup.
                _turretNode = _tank.GetNodeOrNull<Node3D>("Turret");
            }

            // Pre-allocate aim raycast query; From/To are updated each frame.
            _aimQuery = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition + Vector3.Forward * 500f);
            _aimQuery.Exclude = _excludeRids;

            // Only the active (non-ghost) camera captures the mouse.
            if (Current)
                Input.MouseMode = Input.MouseModeEnum.Captured;

            SetPhysicsProcess(false);
            SetProcess(true);
        }

        public override void _Input(InputEvent @event)
        {
            if (!Current) return;

            // Only accumulate mouse look when the cursor is captured.
            if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

            if (@event is InputEventMouseMotion motion)
            {
                CurrentYaw   -= motion.Relative.X * MouseSensitivity;
                CurrentYaw    = MathUtils.WrapAngle(CurrentYaw);
                CurrentPitch += motion.Relative.Y * MouseSensitivity;
                CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMinForMode, PitchMaxForMode);
                // Yaw cone clamp for FPS is applied in _Process each frame
                // (rather than here) so it stays consistent as the hull rotates.
            }
        }

        // Escape toggles mouse capture so the player can reach the OS. Uses
        // _UnhandledInput so UnitCommander can consume Escape when it's being
        // used to deselect units instead.
        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Current) return;

            if (@event is InputEventKey key && key.Keycode == Key.Escape
                                            && key.Pressed && !key.Echo)
            {
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
            }
        }

        public override void _Process(double delta)
        {
            // Skip processing for non-active (ghost) cameras entirely.
            if (!Current || _tank == null) return;

            float dt = (float)delta;

            // View-mode toggle (F1 = first-person, F2 = third-person).
            if (Input.IsActionJustPressed("camera_first_person"))
                SetViewMode(ViewMode.FirstPerson);
            else if (Input.IsActionJustPressed("camera_third_person"))
                SetViewMode(ViewMode.ThirdPerson);

            // Right analog stick — only when mouse is captured (game is focused).
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                CurrentYaw   -= Input.GetAxis("look_left",  "look_right") * StickSensitivity * dt;
                CurrentYaw    = MathUtils.WrapAngle(CurrentYaw);
                CurrentPitch += Input.GetAxis("look_up",    "look_down")  * StickSensitivity * dt;
                CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMinForMode, PitchMaxForMode);
            }

            // In FPS, continually clamp reticle yaw to a cone around the hull heading.
            // The hull's own auto-steer PD (HoverTank.ProcessMovement) then pulls
            // the hull toward CurrentYaw at its natural turn rate, so leaning on
            // the edge of the cone drags the tank around Battlezone-style.
            if (_mode == ViewMode.FirstPerson && _tank != null)
            {
                float hullYaw = Mathf.Atan2(_tank.Basis.Z.X, _tank.Basis.Z.Z);
                float delta   = MathUtils.AngleDiff(CurrentYaw, hullYaw);
                delta         = Mathf.Clamp(delta, -FpsYawConeHalfWidth, FpsYawConeHalfWidth);
                CurrentYaw    = MathUtils.WrapAngle(hullYaw + delta);
            }

            // Spring-follow the orbit centre so sudden tank jolts don't snap the camera.
            Vector3 targetCenter = _tank.GlobalPosition + Vector3.Up * OrbitCenterHeight;
            // Cap dt to avoid GC/hitch frames over-correcting the lerp, which
            // would snap the camera forward and make the tank appear to pop
            // backward relative to the view. One physics tick (1/60 s) is the
            // natural upper bound since the tank's position can't change
            // faster than that anyway.
            float smoothDt = Mathf.Min(dt, 1f / 60f);
            _smoothOrbitCenter = _smoothOrbitCenter.Lerp(targetCenter, PositionLag * smoothDt);

            if (_mode == ViewMode.ThirdPerson)
            {
                // Compute camera position from yaw + pitch orbit.
                // At CurrentYaw=0, pitch=0 the camera sits at (0, 0, +OrbitRadius) from
                // the orbit centre — directly behind a tank whose -Z axis is forward.
                float cosP = Mathf.Cos(CurrentPitch);
                float sinP = Mathf.Sin(CurrentPitch);
                float cosY = Mathf.Cos(CurrentYaw);
                float sinY = Mathf.Sin(CurrentYaw);

                var offset = new Vector3(sinY * cosP, sinP, cosY * cosP) * OrbitRadius;

                GlobalPosition = _smoothOrbitCenter + offset;

                // Apply shake offset in camera-local space so it always feels like
                // a screen-plane displacement regardless of camera orientation.
                if (_shakeAmplitude > 0.001f)
                {
                    GlobalPosition += (GlobalBasis.X * (float)GD.RandRange(-1.0, 1.0)
                                     + GlobalBasis.Y * (float)GD.RandRange(-1.0, 1.0)) * _shakeAmplitude;
                    _shakeAmplitude = Mathf.Max(0f, _shakeAmplitude - ShakeDecay * dt);
                }

                LookAt(_smoothOrbitCenter, Vector3.Up);
            }
            else // FirstPerson
            {
                // Eye sits inside the tank at turret height, moving with the hull.
                Vector3 eye = _tank.GlobalTransform * FpsEyeOffset;

                // Build orientation from reticle yaw/pitch using Euler-style
                // composition: pitch first (around local X), then yaw (around
                // world Y). R = R_yaw * R_pitch keeps the camera unrolled at
                // all yaw angles. CurrentYaw=0 looks down world -Z (same as
                // the tank's forward axis and TPS convention), so turret and
                // weapons see a consistent aim direction in both modes.
                var basis = new Basis(Vector3.Up, CurrentYaw)
                          * new Basis(Vector3.Right, -CurrentPitch);

                GlobalTransform = new Transform3D(basis, eye);

                // Shake as a screen-plane displacement, applied after the
                // transform is set so GlobalBasis reflects the new orientation.
                if (_shakeAmplitude > 0.001f)
                {
                    GlobalPosition += (GlobalBasis.X * (float)GD.RandRange(-1.0, 1.0)
                                     + GlobalBasis.Y * (float)GD.RandRange(-1.0, 1.0)) * _shakeAmplitude;
                    _shakeAmplitude = Mathf.Max(0f, _shakeAmplitude - ShakeDecay * dt);
                }
            }

            // Raycast from camera through screen centre to find AimTarget.
            // NOTE: DirectSpaceState queries from _Process are safe with Godot's
            // default single-threaded physics. If multithreaded physics is ever
            // enabled this should move to _PhysicsProcess.
            UpdateAimTarget();
        }

        private float PitchMinForMode => _mode == ViewMode.FirstPerson ? FpsPitchMin : PitchMin;
        private float PitchMaxForMode => _mode == ViewMode.FirstPerson ? FpsPitchMax : PitchMax;

        // Switches view mode, re-clamps pitch against the new range, and toggles
        // the tank's turret mesh so it doesn't clip the in-cockpit camera.
        // Yaw state is preserved across modes so there's no snap when switching.
        private void SetViewMode(ViewMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;

            CurrentPitch = Mathf.Clamp(CurrentPitch, PitchMinForMode, PitchMaxForMode);

            if (_turretNode != null)
                _turretNode.Visible = mode == ViewMode.ThirdPerson;
        }

        private void UpdateAimTarget()
        {
            Vector3 rayOrigin = GlobalPosition;
            Vector3 rayEnd    = rayOrigin + (-GlobalBasis.Z) * 500f;

            _aimQuery.From = rayOrigin;
            _aimQuery.To   = rayEnd;

            var hit = GetWorld3D().DirectSpaceState.IntersectRay(_aimQuery);
            AimTarget = hit.Count > 0
                ? hit["position"].As<Vector3>()
                : rayEnd;
        }
    }
}
