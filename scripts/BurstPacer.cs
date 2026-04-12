namespace HoverTank
{
    /// <summary>
    /// Tracks burst-fire rhythm for a weapon: a short run of shots followed by
    /// a rest. Pure data; no Godot dependencies. Caller supplies the delta
    /// time and a 0..1 random sample so behaviour is deterministic in tests.
    ///
    /// Typical usage per tick:
    ///   pacer.Tick(delta);
    ///   if (pacer.Ready &amp;&amp; weaponReadyToFire) { fire(); pacer.ConsumeShot(rand01); }
    /// </summary>
    internal struct BurstPacer
    {
        public int   BurstLength;
        public float RestSeconds;
        public float RestJitter;   // extra random seconds added to RestSeconds (0..RestJitter)

        private int   _shotsLeft;
        private float _restTimer;

        public BurstPacer(int burstLength, float restSeconds, float restJitter)
        {
            BurstLength = burstLength;
            RestSeconds = restSeconds;
            RestJitter  = restJitter;
            _shotsLeft  = burstLength;
            _restTimer  = 0f;
        }

        // Advance the rest timer and reload the burst when it elapses.
        public void Tick(float delta)
        {
            if (_shotsLeft > 0) return;
            if (_restTimer > 0f) { _restTimer -= delta; return; }
            _shotsLeft = BurstLength;
        }

        // True when a shot may fire this tick (still have shots and not resting).
        public bool Ready => _shotsLeft > 0 && _restTimer <= 0f;

        // Deduct one shot. If the burst is now empty, begin a rest period.
        // 'rand01' is a uniform random in [0,1); pass 0 for deterministic timing.
        public void ConsumeShot(float rand01)
        {
            if (_shotsLeft <= 0) return;
            _shotsLeft--;
            if (_shotsLeft <= 0)
                _restTimer = RestSeconds + rand01 * RestJitter;
        }
    }
}
