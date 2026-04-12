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

        // Optional world-space target for guided rockets. When set, the rocket
        // steers toward this point at MaxTurnRadPerSec. Null = unguided.
        public Vector3? TargetPosition { get; set; } = null;

        // Maximum turn rate for guided rockets (radians per second).
        [Export] public float MaxTurnRadPerSec = 1.8f;

        private float          _age;
        private bool           _dying;
        private GpuParticles3D _trail = null!;
        // Cached to avoid per-frame allocation in _PhysicsProcess.
        private Godot.Collections.Array<Rid> _excludeRids = null!;

        public override void _Ready()
        {
            _excludeRids = new Godot.Collections.Array<Rid>();
            if (OwnerRid != default)
                _excludeRids.Add(OwnerRid);
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

        // ── Stats ────────────────────────────────────────────────────────────
        // Single source of truth for per-kind speed / damage / lifetime.
        // Both WeaponManager and NetworkManager call this so the values stay in sync.
        public static (float Speed, float Damage, float Lifetime) GetStats(ProjectileKind kind) =>
            kind switch
            {
                ProjectileKind.Bullet => (90f,  5f,   2.5f),
                ProjectileKind.Rocket => (28f,  50f,  6.0f),
                ProjectileKind.Shell  => (45f,  100f, 6.0f),
                _                     => (90f,  5f,   2.5f),
            };

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

            // Guided rocket steering: curve toward TargetPosition at limited turn rate.
            if (TargetPosition.HasValue && Kind == ProjectileKind.Rocket)
            {
                Vector3 toTarget   = (TargetPosition.Value - GlobalPosition).Normalized();
                Vector3 currentFwd = -GlobalBasis.Z;
                float   angle      = currentFwd.AngleTo(toTarget);
                if (angle > 0.001f)
                {
                    float   maxAngle = MaxTurnRadPerSec * (float)delta;
                    float   t        = Mathf.Min(1f, maxAngle / angle);
                    Vector3 newFwd   = currentFwd.Slerp(toTarget, t).Normalized();
                    GlobalBasis      = Basis.LookingAt(-newFwd, Vector3.Up);
                }
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
                query.Exclude = _excludeRids;

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

            // Play impact sound only on real collisions, not range timeouts.
            // Visual-only projectiles (client ghosts) skip the sound — the
            // authoritative server projectile plays it for the remote player.
            if (_age < Lifetime && !IsVisualOnly)
            {
                AudioManager.Instance?.PlayImpact(Kind, GlobalPosition);
                SpawnImpactEffect(Kind, GlobalPosition);
            }

            _trail.Emitting = false;
            // Reparent trail to the scene root so it outlives this node
            _trail.Reparent(GetTree().CurrentScene);
            var timer = GetTree().CreateTimer(1.0);
            timer.Timeout += _trail.QueueFree;

            QueueFree();
        }

        // ── Impact particle burst + light flash ──────────────────────────────
        private void SpawnImpactEffect(ProjectileKind kind, Vector3 pos)
        {
            var scene = GetTree().CurrentScene;

            Color baseColor;
            int   amount;
            float lifetime, velMin, velMax, scaleMin, scaleMax, flashEnergy, flashRange;

            switch (kind)
            {
                case ProjectileKind.Bullet:
                    baseColor = new Color(1f, 0.85f, 0.30f);
                    amount    = 8;   lifetime = 0.25f;
                    velMin    = 2f;  velMax   = 8f;
                    scaleMin  = 0.02f; scaleMax = 0.06f;
                    flashEnergy = 1.5f; flashRange = 2f;
                    break;
                case ProjectileKind.Rocket:
                    baseColor = new Color(1f, 0.50f, 0.10f);
                    amount    = 30;  lifetime = 0.80f;
                    velMin    = 3f;  velMax   = 12f;
                    scaleMin  = 0.08f; scaleMax = 0.25f;
                    flashEnergy = 4f; flashRange = 6f;
                    break;
                default: // Shell
                    baseColor = new Color(1f, 0.40f, 0.05f);
                    amount    = 60;  lifetime = 1.20f;
                    velMin    = 5f;  velMax   = 20f;
                    scaleMin  = 0.12f; scaleMax = 0.40f;
                    flashEnergy = 8f; flashRange = 12f;
                    break;
            }

            var burst = new GpuParticles3D();
            var pmat  = new ParticleProcessMaterial
            {
                Direction          = Vector3.Up,
                Spread             = 180f,
                InitialVelocityMin = velMin,
                InitialVelocityMax = velMax,
                Gravity            = new Vector3(0f, -5f, 0f),
                ScaleMin           = scaleMin,
                ScaleMax           = scaleMax,
            };
            var grad = new Gradient();
            grad.SetColor(0, new Color(baseColor.R, baseColor.G, baseColor.B, 0.95f));
            grad.SetColor(1, new Color(0.15f, 0.15f, 0.15f, 0.0f)); // fade to smoke
            pmat.ColorRamp       = new GradientTexture1D { Gradient = grad };
            burst.ProcessMaterial  = pmat;
            burst.Amount           = amount;
            burst.Lifetime         = lifetime;
            burst.OneShot          = true;
            burst.Explosiveness    = 0.85f;
            burst.LocalCoords      = false;
            burst.Emitting         = true;
            scene.AddChild(burst);
            burst.GlobalPosition   = pos;

            var flash = new OmniLight3D
            {
                LightColor    = new Color(1f, 0.65f, 0.20f),
                LightEnergy   = flashEnergy,
                OmniRange     = flashRange,
                ShadowEnabled = false,
                LightBakeMode = Light3D.BakeMode.Disabled,
            };
            scene.AddChild(flash);
            flash.GlobalPosition = pos;

            var flashTimer = GetTree().CreateTimer(0.08);
            flashTimer.Timeout += flash.QueueFree;
            var cleanTimer = GetTree().CreateTimer(lifetime + 0.5f);
            cleanTimer.Timeout += burst.QueueFree;
        }
    }
}
