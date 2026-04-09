using Godot;

namespace HoverTank
{
    /// <summary>
    /// Rotates the turret node toward the camera's aim direction.
    /// Attach to the Turret node inside HoverTank.tscn.
    ///
    /// The turret yaw is clamped to ±MaxYawDeg from the tank body's forward
    /// direction. The Barrel child is pitched for barrel elevation.
    ///
    /// WeaponManager reads GetAimForward() to orient cannon and rocket fire.
    ///
    /// Yaw convention: matches FollowCamera and HoverTank — tankYaw is computed
    /// as Atan2(Basis.Z.X, Basis.Z.Z), the same "behind" angle, so TargetAimYaw
    /// equal to tankYaw produces zero turret offset (turret faces straight ahead).
    /// </summary>
    public partial class TurretController : Node3D
    {
        // Maximum yaw swing left or right from the tank's nose (degrees).
        [Export] public float MaxYawDeg     = 90f;

        // How fast the turret can slew (degrees per second).
        [Export] public float SlewDegPerSec = 180f;

        // Target world-space yaw set each frame by HoverTank from camera data.
        public float TargetAimYaw   { get; set; }
        // Target pitch (radians) for barrel elevation.
        public float TargetAimPitch { get; set; }

        private HoverTank? _tank;
        private Node3D?    _barrel;

        // Cached radian conversions so we don't DegToRad every _Process frame.
        private float _maxYawRad;
        private float _slewRadPerSec;

        public override void _Ready()
        {
            _tank          = GetParent<HoverTank>();
            _barrel        = GetNodeOrNull<Node3D>("Barrel");
            _maxYawRad     = Mathf.DegToRad(MaxYawDeg);
            _slewRadPerSec = Mathf.DegToRad(SlewDegPerSec);
        }

        public override void _Process(double delta)
        {
            if (_tank == null) return;
            float dt = (float)delta;

            // Tank's world-space yaw — same convention as FollowCamera.CurrentYaw
            // (Atan2 of the backward direction, not the forward direction).
            float tankYaw    = Mathf.Atan2(_tank.Basis.Z.X, _tank.Basis.Z.Z);
            float desiredRel = MathUtils.AngleDiff(TargetAimYaw, tankYaw);
            desiredRel       = Mathf.Clamp(desiredRel, -_maxYawRad, _maxYawRad);

            // Slew at limited angular speed.
            float newYaw = Mathf.MoveToward(Rotation.Y, desiredRel, _slewRadPerSec * dt);
            Rotation = new Vector3(0f, newYaw, 0f);

            // Barrel pitch — the Barrel mesh is already rotated 90° on X in the scene
            // (cylinder axis aligned with -Z). We add elevation on top of that.
            if (_barrel != null)
            {
                float pitchClamped = Mathf.Clamp(TargetAimPitch,
                    Mathf.DegToRad(-5f), Mathf.DegToRad(20f));
                // Subtract pitch so positive pitch (camera looking down) raises barrel.
                _barrel.Rotation = new Vector3(Mathf.Pi / 2f - pitchClamped, 0f, 0f);
            }
        }

        /// <summary>
        /// World-space forward direction of the turret. Used by WeaponManager to
        /// orient cannon shells and rockets along the turret aim line.
        /// </summary>
        public Vector3 GetAimForward() => -GlobalBasis.Z;
    }
}
