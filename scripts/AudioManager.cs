using Godot;
using System;

namespace HoverTank
{
    // Procedural audio system — all sounds are synthesised from PCM at startup.
    // No audio files are required; every sound is generated as an AudioStreamWav.
    //
    // Sounds:
    //   Engine hum  — looping multi-harmonic tone, pitch/volume driven by throttle.
    //   Minigun     — short noise/tonal crack burst (70 ms).
    //   Rocket fire — frequency-swept whoosh (500 ms).
    //   Shell fire  — deep bass boom (600 ms).
    //   Bullet hit  — snappy crack (120 ms).
    //   Explosion   — layered thump + noise, two sizes (small 1 s, large 1.8 s).
    //   Ambient     — low-pass filtered wind noise, 4-second seamless loop.
    public partial class AudioManager : Node
    {
        public static AudioManager? Instance { get; private set; }

        private const int SampleRate = 22050;
        private const int PoolSize   = 16;

        // Exposed so HoverTank can create per-tank players with the right stream.
        public AudioStreamWav EngineHumStream    { get; private set; } = null!;

        private AudioStreamWav _minigunStream        = null!;
        private AudioStreamWav _rocketFireStream     = null!;
        private AudioStreamWav _shellFireStream      = null!;
        private AudioStreamWav _bulletImpactStream   = null!;
        private AudioStreamWav _explosionSmallStream = null!;
        private AudioStreamWav _explosionLargeStream = null!;
        private AudioStreamWav _ambientStream        = null!;

        // Pool of spatial players reused for all one-shot sounds.
        private AudioStreamPlayer3D[] _pool = null!;
        private int _poolNext; // round-robin steal index, always kept in [0, PoolSize)

        public override void _Ready()
        {
            Instance = this;
            GenerateStreams();
            BuildPool();
            StartAmbient();
        }

        // ── Public API ───────────────────────────────────────────────────────

        // Creates a looping AudioStreamPlayer3D for engine hum, pre-configured
        // for spatial attenuation. Caller adds it as a child of the tank node.
        public AudioStreamPlayer3D CreateEnginePlayer()
        {
            return new AudioStreamPlayer3D
            {
                Stream           = EngineHumStream,
                VolumeDb         = -14f,
                MaxDb            = 3f,
                UnitSize         = 10f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                PitchScale       = 0.75f,
                Autoplay         = true,
            };
        }

        // Called by WeaponManager once per Fire() invocation.
        public void PlayWeaponFire(ProjectileKind kind, Vector3 position)
        {
            var (stream, vol, pitch) = kind switch
            {
                ProjectileKind.Bullet => (_minigunStream,    -6f,  0.9f + (float)GD.RandRange(0.0, 0.2)),
                ProjectileKind.Rocket => (_rocketFireStream, -3f,  0.9f + (float)GD.RandRange(0.0, 0.15)),
                ProjectileKind.Shell  => (_shellFireStream,  -2f,  0.85f + (float)GD.RandRange(0.0, 0.15)),
                _                     => (_minigunStream,    -6f,  1f),
            };
            PlayAt(stream, position, vol, pitch);
        }

        // Called by Projectile when it hits something (not a range timeout).
        public void PlayImpact(ProjectileKind kind, Vector3 position)
        {
            if (kind == ProjectileKind.Bullet)
                PlayAt(_bulletImpactStream,   position, -8f, 0.85f + (float)GD.RandRange(0.0, 0.3));
            else
                PlayAt(_explosionSmallStream, position, -2f, 0.9f  + (float)GD.RandRange(0.0, 0.2));
        }

        // Called by HoverTank when a tank is destroyed.
        public void PlayExplosion(Vector3 position)
        {
            PlayAt(_explosionLargeStream, position, 0f, 0.9f + (float)GD.RandRange(0.0, 0.15));
        }

        // Called by HoverTank._PhysicsProcess() to modulate the engine hum each tick.
        // All audio tuning constants live here, not in the physics script.
        // Lerp factor 0.08 at 60 Hz gives a ~0.18 s response time.
        public void UpdateEngineThrottle(AudioStreamPlayer3D player, float throttle, bool jumping)
        {
            float targetPitch = jumping ? 1.55f : 0.75f + 0.55f * throttle; // 0.75–1.30 idle→full, 1.55 jump
            float targetVol   = jumping ? -4f   : -14f  + 8f   * throttle;  // -14–-6 dB idle→full, -4 jump
            player.PitchScale = Mathf.Lerp(player.PitchScale, targetPitch, 0.08f);
            player.VolumeDb   = Mathf.Lerp(player.VolumeDb,   targetVol,   0.08f);
        }

        // ── Internal pool ───────────────────────────────────────────────────

        private void PlayAt(AudioStreamWav stream, Vector3 pos, float volumeDb, float pitch)
        {
            var player = GetIdlePlayer();
            player.Stream         = stream;
            player.VolumeDb       = volumeDb;
            player.PitchScale     = pitch;
            player.GlobalPosition = pos;
            player.Play();
        }

        private AudioStreamPlayer3D GetIdlePlayer()
        {
            // First pass: find a player that has finished.
            for (int i = 0; i < PoolSize; i++)
            {
                if (!_pool[i].Playing) return _pool[i];
            }
            // Pool exhausted: steal the next slot round-robin.
            var stolen = _pool[_poolNext];
            _poolNext = (_poolNext + 1) % PoolSize;
            stolen.Stop();
            return stolen;
        }

        private void BuildPool()
        {
            _pool = new AudioStreamPlayer3D[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var p = new AudioStreamPlayer3D
                {
                    MaxDb    = 3f,
                    UnitSize = 20f,
                    AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                };
                AddChild(p);
                _pool[i] = p;
            }
        }

        private void StartAmbient()
        {
            AddChild(new AudioStreamPlayer
            {
                Stream   = _ambientStream,
                VolumeDb = -20f,
                Autoplay = true,
            });
        }

        // ── PCM generation ──────────────────────────────────────────────────

        private void GenerateStreams()
        {
            var rng = new Random(1337); // fixed seed — deterministic audio every run
            EngineHumStream      = GenerateEngineHum();
            _minigunStream       = GenerateMinigunFire(rng);
            _rocketFireStream    = GenerateRocketFire(rng);
            _shellFireStream     = GenerateShellFire(rng);
            _bulletImpactStream  = GenerateBulletImpact(rng);
            _explosionSmallStream = GenerateExplosion(rng, durationSec: 1.0f);
            _explosionLargeStream = GenerateExplosion(rng, durationSec: 1.8f);
            _ambientStream       = GenerateAmbient(rng);
        }

        // Converts a normalised float sample array [-1, 1] to a little-endian
        // int16 byte array suitable for AudioStreamWav.Data.
        private static byte[] ToPcm(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short s = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767f);
                bytes[i * 2]     = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        private static AudioStreamWav MakeStream(float[] samples, bool loop = false)
        {
            var stream = new AudioStreamWav
            {
                Data    = ToPcm(samples),
                Format  = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = SampleRate,
                Stereo  = false,
            };
            if (loop)
            {
                stream.LoopMode  = AudioStreamWav.LoopModeEnum.Forward;
                stream.LoopBegin = 0;
                stream.LoopEnd   = samples.Length;
            }
            return stream;
        }

        private static float Noise(Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        // ── Engine hum: 1-second looping multi-harmonic tone ─────────────────
        // Layered harmonics (75, 150, 225 Hz) + a faint electric whine (1100 Hz)
        // give the feel of a heavy magnetic levitation drive.
        private static AudioStreamWav GenerateEngineHum()
        {
            int n = SampleRate; // 1 second loop
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                samples[i] =
                    0.52f * MathF.Sin(2f * MathF.PI * 75f   * t) +   // fundamental
                    0.30f * MathF.Sin(2f * MathF.PI * 150f  * t) +   // 2nd harmonic
                    0.14f * MathF.Sin(2f * MathF.PI * 225f  * t) +   // 3rd harmonic
                    0.07f * MathF.Sin(2f * MathF.PI * 1100f * t);    // electric whine
                samples[i] *= 0.78f; // keep headroom
            }
            return MakeStream(samples, loop: true);
        }

        // ── Minigun: 70 ms noise + tonal crack ───────────────────────────────
        private static AudioStreamWav GenerateMinigunFire(Random rng)
        {
            int n = (int)(SampleRate * 0.07f);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t    = (float)i / n;
                float env  = MathF.Exp(-t * 22f) * MathF.Min(t * 400f, 1f);
                float tone = MathF.Sin(2f * MathF.PI * 1800f * ((float)i / SampleRate));
                samples[i] = (0.65f * Noise(rng) + 0.35f * tone) * env;
            }
            return MakeStream(samples);
        }

        // ── Rocket fire: initial crack + 500 ms swept whoosh ─────────────────
        private static AudioStreamWav GenerateRocketFire(Random rng)
        {
            int   n       = (int)(SampleRate * 0.5f);
            var   samples = new float[n];
            float phase   = 0f;
            for (int i = 0; i < n; i++)
            {
                float t    = (float)i / n;
                // Frequency sweeps 600 → 150 Hz over the duration
                float freq = 600f - 450f * t;
                phase += 2f * MathF.PI * freq / SampleRate;
                float env     = MathF.Exp(-t * 3.5f) * MathF.Min(t * 25f, 1f);
                float whistle = MathF.Sin(phase) * 0.35f;
                float noise   = Noise(rng) * 0.65f * MathF.Exp(-t * 4f);
                samples[i]    = (whistle + noise) * env;
            }
            return MakeStream(samples);
        }

        // ── Shell fire: deep bass boom (90→40 Hz sweep) + initial crack ───────
        private static AudioStreamWav GenerateShellFire(Random rng)
        {
            int   n       = (int)(SampleRate * 0.6f);
            var   samples = new float[n];
            float phase   = 0f;
            for (int i = 0; i < n; i++)
            {
                float t    = (float)i / n;
                float freq = 90f - 50f * t;
                phase += 2f * MathF.PI * freq / SampleRate;
                float env   = MathF.Exp(-t * 4.5f) * MathF.Min(t * 60f, 1f);
                float boom  = MathF.Sin(phase) * 0.55f;
                float crack = Noise(rng) * MathF.Exp(-t * 30f) * 0.7f;
                samples[i]  = (boom + crack) * env;
            }
            return MakeStream(samples);
        }

        // ── Bullet impact: 120 ms snappy crack ───────────────────────────────
        private static AudioStreamWav GenerateBulletImpact(Random rng)
        {
            int n = (int)(SampleRate * 0.12f);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t   = (float)i / n;
                float env = MathF.Exp(-t * 35f) * MathF.Min(t * 600f, 1f);
                samples[i] = Noise(rng) * env;
            }
            return MakeStream(samples);
        }

        // ── Explosion: layered thump + blast + long rumble tail ──────────────
        // Used for both projectile impacts (small, 1 s) and tank deaths (large, 1.8 s).
        private static AudioStreamWav GenerateExplosion(Random rng, float durationSec)
        {
            int   n       = (int)(SampleRate * durationSec);
            var   samples = new float[n];
            float phase   = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                // Low thump: 50 → 25 Hz sweep for that punchy sub-bass
                float freq  = 50f - 25f * t;
                phase      += 2f * MathF.PI * freq / SampleRate;
                float thump  = MathF.Sin(phase) * MathF.Exp(-t * 5f)  * 0.55f;
                float blast  = Noise(rng)        * MathF.Exp(-t * 9f)  * 0.75f;
                float rumble = Noise(rng)        * MathF.Exp(-t * 2f)  * 0.28f;
                samples[i]   = Math.Clamp(thump + blast + rumble, -1f, 1f);
            }
            return MakeStream(samples);
        }

        // ── Ambient: 4-second low-pass filtered wind noise loop ──────────────
        private static AudioStreamWav GenerateAmbient(Random rng)
        {
            int n = SampleRate * 4;
            var raw = new float[n];
            for (int i = 0; i < n; i++)
                raw[i] = Noise(rng);

            // Three passes of box-blur (window = 100 samples ≈ 220 Hz cutoff)
            // approximates a smooth low-pass, giving a breathy wind character.
            int window = 100;
            for (int pass = 0; pass < 3; pass++)
            {
                var buf = new float[n];
                float sum = 0f;
                for (int i = 0; i < window && i < n; i++) sum += raw[i];
                for (int i = 0; i < n; i++)
                {
                    buf[i] = sum / window;
                    if (i + window < n) sum += raw[i + window];
                    sum -= raw[i];
                }
                raw = buf;
            }

            // Gentle slow modulation (0.25 Hz) for a breathing, alive feel.
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                raw[i] *= (0.80f + 0.20f * MathF.Sin(2f * MathF.PI * 0.25f * t)) * 0.65f;
            }

            // Crossfade last 0.1 s into the first 0.1 s to prevent loop click.
            int fade = SampleRate / 10;
            for (int i = 0; i < fade; i++)
            {
                float alpha = (float)i / fade;
                raw[n - fade + i] = raw[n - fade + i] * (1f - alpha) + raw[i] * alpha;
            }

            return MakeStream(raw, loop: true);
        }
    }
}
