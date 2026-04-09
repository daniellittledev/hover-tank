using Godot;

namespace HoverTank
{
    /// <summary>
    /// Halo-style orbital camera. Orbits around the tank at a fixed radius.
    /// Mouse X/Y (or right analog stick) rotates the camera independently from
    /// the tank body. The tank auto-steers toward the camera's yaw direction.
    ///
    /// Exposes CurrentYaw, CurrentPitch, and AimTarget so LocalInputHandler and
    /// WeaponManager can read the aiming state.
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
        // Distance from orbit centre to camera (metres).
        [Export] public float OrbitRadius = 8f;

        // Height above tank origin that the camera orbits around.
        [Export] public float OrbitCenterHeight = 1.5f;

        // How fast camera position spring-follows the tank (per second).
        [Export] public float PositionLag = 6.0f;

        // Mouse sensitivity (radians per pixel).
        [Export] public float MouseSensitivity = 0.003f;

        // Right-stick sensitivity (radians per second at full deflection).
        [Export] public float StickSensitivity = 2.5f;

        // Pitch limits (radians). Negative = look up, positive = look down.
        [Export] public float PitchMin = -0.26f;  // ~-15°
        [Export] public float PitchMax =  0.61f;  //  ~35°

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

            // Escape toggles mouse capture so the player can reach the OS.
            if (@event is InputEventKey key && key.Keycode == Key.Escape
                                            && key.Pressed && !key.Echo)
            {
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
                return;
            }

            // Only accumulate mouse look when the cursor is captured.
            if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

            if (@event is InputEventMouseMotion motion)
            {
                CurrentYaw   -= motion.Relative.X * MouseSensitivity;
                CurrentPitch += motion.Relative.Y * MouseSensitivity;
                CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMin, PitchMax);
            }
        }

        public override void _Process(double delta)
        {
            // Skip processing for non-active (ghost) cameras entirely.
            if (!Current || _tank == null) return;

            float dt = (float)delta;

            // Right analog stick — only when mouse is captured (game is focused).
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                CurrentYaw   -= Input.GetAxis("look_left",  "look_right") * StickSensitivity * dt;
                CurrentPitch += Input.GetAxis("look_up",    "look_down")  * StickSensitivity * dt;
                CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMin, PitchMax);
            }

            // Spring-follow the orbit centre so sudden tank jolts don't snap the camera.
            Vector3 targetCenter = _tank.GlobalPosition + Vector3.Up * OrbitCenterHeight;
            _smoothOrbitCenter = _smoothOrbitCenter.Lerp(targetCenter, PositionLag * dt);

            // Compute camera position from yaw + pitch orbit.
            // At CurrentYaw=0, pitch=0 the camera sits at (0, 0, +OrbitRadius) from the
            // orbit centre — directly behind a tank whose -Z axis is forward.
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
                GlobalPosition += GlobalBasis.X * ((float)GD.RandRange(-1.0, 1.0) * _shakeAmplitude);
                GlobalPosition += GlobalBasis.Y * ((float)GD.RandRange(-1.0, 1.0) * _shakeAmplitude);
                _shakeAmplitude = Mathf.Max(0f, _shakeAmplitude - ShakeDecay * dt);
            }

            LookAt(_smoothOrbitCenter, Vector3.Up);

            // Raycast from camera through screen centre to find AimTarget.
            // NOTE: DirectSpaceState queries from _Process are safe with Godot's
            // default single-threaded physics. If multithreaded physics is ever
            // enabled this should move to _PhysicsProcess.
            UpdateAimTarget();
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
