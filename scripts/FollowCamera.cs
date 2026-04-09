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

        // Mouse / right-stick sensitivity (radians per pixel / per unit).
        [Export] public float MouseSensitivity = 0.003f;

        // Pitch limits (radians). Negative = look up, positive = look down.
        [Export] public float PitchMin = -0.26f;  // ~-15°
        [Export] public float PitchMax =  0.61f;  //  ~35°

        // World-space camera yaw (radians). Read by LocalInputHandler → TankInput.
        public float CurrentYaw   { get; private set; }
        // Camera pitch (radians). Read by LocalInputHandler → TankInput.
        public float CurrentPitch { get; private set; }
        // World-space point the camera crosshair hits. Read by WeaponManager for rockets.
        public Vector3 AimTarget  { get; private set; }

        private HoverTank? _tank;
        private Vector3 _smoothOrbitCenter;
        private bool _initialised;

        public override void _Ready()
        {
            // Camera3D is: Camera → CameraMount → HoverTank
            _tank = GetParent().GetParent<HoverTank>();

            // Initialise yaw to match the tank's current facing so there's no snap.
            if (_tank != null)
            {
                CurrentYaw   = Mathf.Atan2(-_tank.Basis.Z.X, -_tank.Basis.Z.Z);
                CurrentPitch = 0.40f; // ~23° downward — matches old CameraMount tilt
                _smoothOrbitCenter = _tank.GlobalPosition + Vector3.Up * OrbitCenterHeight;
            }

            _initialised = false;

            // Capture mouse so it doesn't leave the window while driving.
            Input.MouseMode = Input.MouseModeEnum.Captured;

            SetPhysicsProcess(false);
            SetProcess(true);
        }

        // Accumulate mouse motion events for smooth look.
        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion)
            {
                CurrentYaw   -= motion.Relative.X * MouseSensitivity;
                CurrentPitch += motion.Relative.Y * MouseSensitivity;
                CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMin, PitchMax);
            }
        }

        public override void _Process(double delta)
        {
            if (_tank == null) return;
            float dt = (float)delta;

            // Right analog stick also drives camera look (gamepad support).
            float stickX = Input.GetAxis("look_left",  "look_right");
            float stickY = Input.GetAxis("look_up",    "look_down");
            float stickSens = MouseSensitivity * 200f * dt;
            CurrentYaw   -= stickX * stickSens;
            CurrentPitch += stickY * stickSens;
            CurrentPitch  = Mathf.Clamp(CurrentPitch, PitchMin, PitchMax);

            // Spring-follow the orbit centre so sudden tank jolts don't snap the camera.
            Vector3 targetCenter = _tank.GlobalPosition + Vector3.Up * OrbitCenterHeight;
            if (!_initialised)
            {
                _smoothOrbitCenter = targetCenter;
                _initialised = true;
            }
            else
            {
                _smoothOrbitCenter = _smoothOrbitCenter.Lerp(targetCenter, PositionLag * dt);
            }

            // Compute camera position from yaw + pitch orbit.
            //   yaw=0, pitch=0 → camera sits directly behind the tank on the +Z axis.
            float cosP = Mathf.Cos(CurrentPitch);
            float sinP = Mathf.Sin(CurrentPitch);
            float cosY = Mathf.Cos(CurrentYaw);
            float sinY = Mathf.Sin(CurrentYaw);

            // Orbit offset in world space: yaw rotates around Y, pitch tilts up/down.
            var offset = new Vector3(
                 sinY * cosP,
                 sinP,
                 cosY * cosP
            ) * OrbitRadius;

            GlobalPosition = _smoothOrbitCenter + offset;
            LookAt(_smoothOrbitCenter, Vector3.Up);

            // Raycast from camera through screen centre to find AimTarget.
            UpdateAimTarget();
        }

        private void UpdateAimTarget()
        {
            // Forward direction is the camera's -Z in world space.
            Vector3 rayOrigin = GlobalPosition;
            Vector3 rayDir    = -GlobalBasis.Z;
            Vector3 rayEnd    = rayOrigin + rayDir * 500f;

            var space = GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
            // Exclude the tank itself so we don't hit our own collider.
            if (_tank != null)
                query.Exclude = new Godot.Collections.Array<Rid> { _tank.GetRid() };

            var hit = space.IntersectRay(query);
            AimTarget = hit.Count > 0
                ? hit["position"].As<Vector3>()
                : rayEnd;
        }
    }
}
