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

        public override void _Ready()
        {
            _tank = GetParent<HoverTank>();
        }

        public override void _Process(double delta)
        {
            if (_tank == null) return;
            float dt = (float)delta;

            // Tank's current world-space yaw.
            float tankYaw = Mathf.Atan2(-_tank.Basis.Z.X, -_tank.Basis.Z.Z);

            // Desired turret yaw relative to tank body, clamped to ±MaxYawDeg.
            float desiredRel = AngleDiff(TargetAimYaw, tankYaw);
            float maxRad     = Mathf.DegToRad(MaxYawDeg);
            desiredRel       = Mathf.Clamp(desiredRel, -maxRad, maxRad);

            // Slew at limited angular speed.
            float maxStep = Mathf.DegToRad(SlewDegPerSec) * dt;
            float newYaw  = Mathf.MoveToward(Rotation.Y, desiredRel, maxStep);
            Rotation = new Vector3(0f, newYaw, 0f);

            // Barrel pitch — Barrel mesh is already rotated 90° on X in the scene
            // (cylinder axis aligned with -Z). We add elevation on top of that.
            var barrel = GetNodeOrNull<Node3D>("Barrel");
            if (barrel != null)
            {
                float pitchClamped = Mathf.Clamp(TargetAimPitch,
                    Mathf.DegToRad(-5f), Mathf.DegToRad(20f));
                // Subtract pitch so positive pitch (camera looking down) raises barrel.
                barrel.Rotation = new Vector3(Mathf.Pi / 2f - pitchClamped, 0f, 0f);
            }
        }

        /// <summary>
        /// World-space forward direction of the turret. Used by WeaponManager to
        /// orient cannon shells and rockets along the turret aim line.
        /// </summary>
        public Vector3 GetAimForward() => -GlobalBasis.Z;

        // Returns the shortest signed angle from 'from' to 'to' in [-π, π].
        private static float AngleDiff(float to, float from)
        {
            float d = (to - from + Mathf.Pi * 3f) % (Mathf.Pi * 2f) - Mathf.Pi;
            return d;
        }
    }
}
