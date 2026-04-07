using Godot;

namespace HoverTank
{
    /// <summary>
    /// Smooth spring-follow camera for a Battlezone-style behind-the-tank view.
    ///
    /// Attach this to the Camera3D child of CameraMount inside HoverTank.tscn.
    /// The camera node is already positioned and angled by the CameraMount
    /// transform in the scene — this script just smooths the world-space
    /// transform so the camera lags slightly behind sudden tank movements.
    ///
    /// PositionLag controls how quickly the camera position tracks the mount
    /// (higher = more responsive, lower = more floaty).
    /// RotationLag controls orientation tracking separately so the camera
    /// can pan smoothly without snapping on sharp turns.
    /// </summary>
    public partial class FollowCamera : Camera3D
    {
        // Speed at which camera position interpolates toward the mount (per second).
        // 0 = frozen, 1 = instant. Typical range: 3–8.
        [Export] public float PositionLag = 6.0f;

        // Speed at which camera rotation interpolates toward the mount.
        [Export] public float RotationLag = 5.0f;

        // World-space transform smoothed each frame
        private Transform3D _smoothTransform;
        private bool _initialised = false;

        public override void _Ready()
        {
            // Snap to mount position immediately on first frame — no startup drift.
            _smoothTransform = GlobalTransform;
            _initialised = false;
            // Use _Process so the camera updates after physics (smoother result).
            SetPhysicsProcess(false);
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // On the very first frame after _Ready the global transform may not
            // yet reflect the mounted position, so snap on frame 2.
            if (!_initialised)
            {
                _smoothTransform = GlobalTransform;
                _initialised = true;
                return;
            }

            Transform3D target = GlobalTransform;

            // Lerp position
            Vector3 smoothPos = _smoothTransform.Origin.Lerp(target.Origin, PositionLag * dt);

            // Slerp orientation (Basis)
            Basis smoothBasis = _smoothTransform.Basis.Slerp(target.Basis, RotationLag * dt);

            _smoothTransform = new Transform3D(smoothBasis, smoothPos);
            GlobalTransform = _smoothTransform;
        }
    }
}
