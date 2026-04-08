using Godot;

namespace HoverTank
{
    public enum ProjectileKind { Bullet, Rocket, Shell }

    public partial class Projectile : Node3D
    {
        public float         Speed    = 90f;
        public float         Damage   = 5f;
        public float         Lifetime = 2.5f;
        public ProjectileKind Kind    = ProjectileKind.Bullet;
        public Rid           OwnerRid;

        // When true the projectile moves visually but skips collision detection
        // and damage. Used on clients for projectiles owned by remote players.
        public bool IsVisualOnly = false;

        private float          _age;
        private bool           _dying;
        private GpuParticles3D _trail = null!;

        public override void _Ready()
        {
            BuildMesh();
            BuildTrail();
        }

        // ── Visual mesh ─────────────────────────────────────────────────────
        private void BuildMesh()
        {
            var mesh = new MeshInstance3D();
            var mat  = new StandardMaterial3D
            {
                EmissionEnabled = true,
                ShadingMode     = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };

            switch (Kind)
            {
                case ProjectileKind.Bullet:
                    mesh.Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f };
                    mat.AlbedoColor = new Color(1f, 0.95f, 0.4f);
                    mat.Emission    = new Color(1f, 0.80f, 0.1f);
                    mat.EmissionEnergyMultiplier = 8f;
                    break;

                case ProjectileKind.Rocket:
                    mesh.Mesh = new CylinderMesh
                        { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 0.45f };
                    // Cylinder's axis is Y; rotate 90° around X so it faces -Z (forward)
                    mesh.RotationDegrees = new Vector3(90f, 0f, 0f);
                    mat.AlbedoColor = new Color(0.85f, 0.35f, 0.1f);
                    mat.Emission    = new Color(1f,    0.40f, 0.1f);
                    mat.EmissionEnergyMultiplier = 5f;
                    break;

                case ProjectileKind.Shell:
                    mesh.Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f };
                    mat.AlbedoColor = new Color(1f,    0.78f, 0.15f);
                    mat.Emission    = new Color(1f,    0.55f, 0.0f);
                    mat.EmissionEnergyMultiplier = 4f;
                    break;
            }

            mesh.SetSurfaceOverrideMaterial(0, mat);
            AddChild(mesh);
        }

        // ── Particle trail ───────────────────────────────────────────────────
        private void BuildTrail()
        {
            _trail = new GpuParticles3D();

            // +Z in the node's local space points backward (projectile faces -Z).
            // With LocalCoords=false, particles are placed in world space each frame
            // but their initial velocity direction is relative to this node's rotation.
            var pmat = new ParticleProcessMaterial
            {
                Direction          = new Vector3(0f, 0f, 1f),
                Spread             = 12f,
                InitialVelocityMin = 0.5f,
                InitialVelocityMax = 2.5f,
                Gravity            = Vector3.Zero,
            };

            Color baseColor;
            switch (Kind)
            {
                case ProjectileKind.Bullet:
                    baseColor              = new Color(1f, 0.60f, 0.15f);
                    pmat.ScaleMin          = 0.02f;
                    pmat.ScaleMax          = 0.06f;
                    _trail.Amount          = 12;
                    _trail.Lifetime        = 0.18;
                    break;

                case ProjectileKind.Rocket:
                    baseColor                  = new Color(1f, 0.35f, 0.05f);
                    pmat.Spread                = 28f;
                    pmat.InitialVelocityMin    = 4f;
                    pmat.InitialVelocityMax    = 12f;
                    pmat.ScaleMin              = 0.08f;
                    pmat.ScaleMax              = 0.24f;
                    _trail.Amount              = 45;
                    _trail.Lifetime            = 0.55;
                    break;

                default: // Shell
                    baseColor       = new Color(1f, 0.50f, 0.10f);
                    pmat.ScaleMin   = 0.05f;
                    pmat.ScaleMax   = 0.14f;
                    _trail.Amount   = 22;
                    _trail.Lifetime = 0.32;
                    break;
            }

            // Bright opaque → fully transparent over particle lifetime
            var grad = new Gradient();
            grad.SetColor(0, new Color(baseColor.R, baseColor.G, baseColor.B, 0.9f));
            grad.SetColor(1, new Color(baseColor.R, baseColor.G, baseColor.B, 0.0f));
            pmat.ColorRamp = new GradientTexture1D { Gradient = grad };

            _trail.ProcessMaterial = pmat;
            _trail.LocalCoords     = false;
            _trail.Emitting        = true;

            AddChild(_trail);
        }

        // ── Physics update: move + collision ────────────────────────────────

        // Conservative margin added to the spatial grid query radius to account
        // for a tank's physical extent (BoxShape3D hull ≈ 2–3 m half-diagonal).
        private const float TankCheckRadius = 4f;

        public override void _PhysicsProcess(double delta)
        {
            if (_dying) return;

            _age += (float)delta;
            if (_age >= Lifetime)
            {
                Die();
                return;
            }

            Vector3 velocity = -GlobalTransform.Basis.Z * Speed;
            Vector3 from     = GlobalPosition;
            Vector3 to       = GlobalPosition + velocity * (float)delta;

            if (!IsVisualOnly)
            {
                // Spatial grid pre-filter: skip the ray cast when the server-side
                // grid confirms no tank centre is within this step's sweep radius.
                // The grid is only populated on the server (Count > 0); when it is
                // empty (offline / clients) we always proceed to the full ray cast.
                float stepRadius = Speed * (float)delta + TankCheckRadius;
                if (ProjectileSpatialGrid.Instance.Count > 0
                    && !ProjectileSpatialGrid.Instance.HasAnyWithin(from, stepRadius))
                {
                    GlobalPosition += velocity * (float)delta;
                    return;
                }

                var space = GetWorld3D().DirectSpaceState;
                var query = PhysicsRayQueryParameters3D.Create(from, to);
                if (OwnerRid != default)
                    query.Exclude = new Godot.Collections.Array<Rid> { OwnerRid };

                var hit = space.IntersectRay(query);
                if (hit.Count > 0)
                {
                    if (hit["collider"].As<GodotObject>() is HoverTank tank)
                        tank.TakeDamage(Damage);

                    Die();
                    return;
                }
            }

            GlobalPosition += velocity * (float)delta;
        }

        // ── Cleanup: detach trail so its particles finish naturally ──────────
        private void Die()
        {
            if (_dying) return;
            _dying = true;

            _trail.Emitting = false;
            // Reparent trail to the scene root so it outlives this node
            _trail.Reparent(GetTree().CurrentScene);
            var timer = GetTree().CreateTimer(1.0);
            timer.Timeout += _trail.QueueFree;

            QueueFree();
        }
    }
}
