using Godot;
using Godot.Collections;
using HoverTank.Network;
using System.Collections.Generic;

namespace HoverTank
{
    // Autoload singleton. Registered in project.godot as "NetworkManager".
    // Handles ENet lifecycle, player spawning, and RPC dispatch.
    // Press F1 in-game to host, F2 to join localhost.
    public partial class NetworkManager : Node
    {
        private const int Port = 7777;
        private const int MaxClients = 8;

        // Set to false on a dedicated server if you don't want a local player.
        [Export] public bool SpawnLocalPlayer = true;

        private ServerSimulation? _server;
        private ClientSimulation? _client;
        private readonly Dictionary<int, RemoteEntityInterpolator> _remotes = new();

        // Incremented every _PhysicsProcess. Both client and server share the
        // same counter (server is canonical; client tracks its own local tick).
        public int CurrentTick { get; private set; }

        // The Node3D container in Main.tscn where tank nodes are added.
        private Node3D _tanksRoot = null!;

        public override void _Ready()
        {
            _tanksRoot = GetNode<Node3D>("/root/Main/Tanks");
        }

        public override void _Input(InputEvent evt)
        {
            if (evt is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.F1) StartHost();
                if (key.Keycode == Key.F2) StartClient("127.0.0.1");
            }
        }

        // ── Connection setup ─────────────────────────────────────────────────

        public void StartHost()
        {
            var peer = new ENetMultiplayerPeer();
            var err = peer.CreateServer(Port, MaxClients);
            if (err != Error.Ok) { GD.PrintErr($"[Net] Host failed: {err}"); return; }

            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerConnected    += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;

            _server = new ServerSimulation(this);
            _client = new ClientSimulation(this);

            // Spawn the host's own tank immediately (server is peer 1).
            SpawnPlayerRpc(Multiplayer.GetUniqueId());
            GD.Print($"[Net] Hosting on port {Port}");
        }

        public void StartClient(string address)
        {
            var peer = new ENetMultiplayerPeer();
            var err = peer.CreateClient(address, Port);
            if (err != Error.Ok) { GD.PrintErr($"[Net] Connect failed: {err}"); return; }

            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            GD.Print($"[Net] Connecting to {address}:{Port}");
        }

        // ── Peer events ──────────────────────────────────────────────────────

        private void OnConnectedToServer()
        {
            _client = new ClientSimulation(this);
            GD.Print("[Net] Connected to server");
        }

        // Server only — called when a new client joins.
        private void OnPeerConnected(long peerId)
        {
            if (!Multiplayer.IsServer()) return;
            GD.Print($"[Net] Peer connected: {peerId}");
            Rpc(MethodName.SpawnPlayerRpc, (int)peerId);
        }

        // Server only — called when a client leaves.
        private void OnPeerDisconnected(long peerId)
        {
            if (!Multiplayer.IsServer()) return;
            GD.Print($"[Net] Peer disconnected: {peerId}");
            Rpc(MethodName.DespawnPlayerRpc, (int)peerId);
        }

        // ── Spawn RPCs ───────────────────────────────────────────────────────

        [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
             TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
        private void SpawnPlayerRpc(int peerId)
        {
            bool isLocalPlayer = (peerId == Multiplayer.GetUniqueId());
            bool isServer = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

            if (isServer)
            {
                // Server: spawn an authoritative physics tank.
                var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                             .Instantiate<HoverTank>();
                tank.Name = $"Tank_{peerId}";
                tank.GlobalPosition = new Vector3(peerId % 4 * 4f, 5f, 0f);
                _tanksRoot.AddChild(tank);
                _server!.RegisterTank(peerId, tank);

                // For the host's own tank: subscribe to fire events so we can
                // broadcast a visual-spawn RPC to all connected clients.
                if (isLocalPlayer && tank.Weapons != null)
                    tank.Weapons.Fired += (kind, xform) => BroadcastProjectileSpawn(kind, xform);
            }
            else if (!isServer)
            {
                if (isLocalPlayer)
                {
                    // Client: spawn local prediction tank, hand to ClientSimulation.
                    var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                                 .Instantiate<HoverTank>();
                    tank.Name = $"Tank_{peerId}";
                    tank.GlobalPosition = new Vector3(0f, 5f, 0f);
                    _tanksRoot.AddChild(tank);
                    _client!.SetLocalTank(tank);

                    // Local prediction: projectiles are visual-only; relay shots to server.
                    if (tank.Weapons != null)
                    {
                        tank.Weapons.FireMode = WeaponFireMode.LocalPrediction;
                        tank.Weapons.Fired += (kind, xform) => SendFireToServer(kind, xform);
                    }
                }
                else
                {
                    // Client: spawn a ghost node for remote interpolation.
                    var ghost = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                                  .Instantiate<HoverTank>();
                    ghost.Name = $"Tank_{peerId}";
                    ghost.GlobalPosition = new Vector3(peerId % 4 * 4f, 5f, 0f);
                    // Disable physics on the ghost — it's driven by interpolation.
                    ghost.Freeze = true;
                    // Hide the camera on remote ghosts.
                    var cam = ghost.GetNodeOrNull<Camera3D>("CameraMount/Camera");
                    if (cam != null) cam.Current = false;
                    // Ghost never fires locally — projectiles arrive via SpawnProjectileRpc.
                    if (ghost.Weapons != null)
                        ghost.Weapons.FireMode = WeaponFireMode.NetworkGhost;
                    _tanksRoot.AddChild(ghost);

                    var interp = new RemoteEntityInterpolator();
                    interp.Name = $"Interp_{peerId}";
                    interp.TargetNode = ghost;
                    AddChild(interp);
                    _remotes[peerId] = interp;
                }
            }
        }

        [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
             TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
        private void DespawnPlayerRpc(int peerId)
        {
            _tanksRoot.GetNodeOrNull($"Tank_{peerId}")?.QueueFree();
            if (_remotes.TryGetValue(peerId, out var interp))
            {
                interp.QueueFree();
                _remotes.Remove(peerId);
            }
            _server?.UnregisterTank(peerId);
        }

        // ── Projectile fire RPCs ─────────────────────────────────────────────
        //
        // Design:
        //   • Host fires locally (Standalone WeaponManager) → Fired event →
        //       BroadcastProjectileSpawn → SpawnProjectileRpc to all clients.
        //   • Client fires locally (visual-only prediction) → Fired event →
        //       SendFireToServer → SubmitFireRpc → server spawns authoritative
        //       projectile and sends SpawnProjectileRpc to all OTHER clients.
        //   • Other clients receive SpawnProjectileRpc → spawn visual-only projectile.

        // Called by the host's own tank's Fired event.
        private void BroadcastProjectileSpawn(ProjectileKind kind, Transform3D xform)
        {
            var q = xform.Basis.GetRotationQuaternion();
            foreach (var peerId in Multiplayer.GetPeers())
            {
                RpcId(peerId, MethodName.SpawnProjectileRpc,
                      (byte)kind,
                      xform.Origin.X, xform.Origin.Y, xform.Origin.Z,
                      q.X, q.Y, q.Z, q.W);
            }
        }

        // Called by the local client's WeaponManager.Fired event.
        private void SendFireToServer(ProjectileKind kind, Transform3D xform)
        {
            var q = xform.Basis.GetRotationQuaternion();
            RpcId(1, MethodName.SubmitFireRpc,
                  (byte)kind,
                  xform.Origin.X, xform.Origin.Y, xform.Origin.Z,
                  q.X, q.Y, q.Z, q.W);
        }

        // Client → server: "I fired a shot from this transform."
        // Server spawns the authoritative (damage-dealing) projectile and forwards
        // a visual-spawn message to all other connected clients.
        [Rpc(MultiplayerApi.RpcMode.AnyPeer,
             TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
        public void SubmitFireRpc(byte kind, float px, float py, float pz,
                                   float rx, float ry, float rz, float rw)
        {
            if (!Multiplayer.IsServer()) return;

            int shooterPeerId = Multiplayer.GetRemoteSenderId();
            var xform = new Transform3D(
                new Basis(new Quaternion(rx, ry, rz, rw)),
                new Vector3(px, py, pz));

            // Authoritative projectile on the server (deals damage).
            SpawnNetworkProjectile((ProjectileKind)kind, xform, shooterPeerId, isVisual: false);

            // Tell every other client to show a visual-only copy.
            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (peerId == shooterPeerId) continue;
                RpcId(peerId, MethodName.SpawnProjectileRpc, kind, px, py, pz, rx, ry, rz, rw);
            }
        }

        // Server → client: "Spawn a visual-only projectile at this transform."
        [Rpc(MultiplayerApi.RpcMode.Authority,
             TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
        public void SpawnProjectileRpc(byte kind, float px, float py, float pz,
                                        float rx, float ry, float rz, float rw)
        {
            var xform = new Transform3D(
                new Basis(new Quaternion(rx, ry, rz, rw)),
                new Vector3(px, py, pz));
            SpawnNetworkProjectile((ProjectileKind)kind, xform, ownerPeerId: -1, isVisual: true);
        }

        // Instantiates a Projectile node from pure network data.
        // ownerPeerId is used to set OwnerRid so the projectile doesn't self-collide.
        private void SpawnNetworkProjectile(ProjectileKind kind, Transform3D xform,
                                             int ownerPeerId, bool isVisual)
        {
            Rid ownerRid = default;
            if (ownerPeerId > 0)
            {
                var tank = _tanksRoot.GetNodeOrNull<HoverTank>($"Tank_{ownerPeerId}");
                if (tank != null) ownerRid = tank.GetRid();
            }

            var (speed, damage, lifetime) = ProjectileStats(kind);
            var proj = new Projectile
            {
                Kind         = kind,
                Speed        = speed,
                Damage       = damage,
                Lifetime     = lifetime,
                OwnerRid     = ownerRid,
                IsVisualOnly = isVisual,
            };
            GetTree().CurrentScene.AddChild(proj);
            proj.GlobalTransform = xform;
        }

        // Mirrors the stats defined in WeaponManager.Fire() — kept in sync manually.
        private static (float speed, float damage, float lifetime) ProjectileStats(ProjectileKind kind) =>
            kind switch
            {
                ProjectileKind.Bullet => (90f, 5f,   2.5f),
                ProjectileKind.Rocket => (28f, 50f,  6.0f),
                ProjectileKind.Shell  => (45f, 100f, 6.0f),
                _                     => (90f, 5f,   2.5f),
            };

        // ── Tick loop ────────────────────────────────────────────────────────

        public override void _PhysicsProcess(double delta)
        {
            CurrentTick++;
            _server?.Tick(CurrentTick);
            _client?.Tick(CurrentTick, (float)delta);
        }

        // ── Input RPC (client → server) ───────────────────────────────────────

        // Client calls this to submit one tick's input to the server.
        [Rpc(MultiplayerApi.RpcMode.AnyPeer,
             TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
        public void SubmitInputRpc(int tick, int sequence, byte flags, float throttle, float steer)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            _server?.OnInputReceived(senderId, new InputPacket
            {
                Tick     = tick,
                Sequence = sequence,
                Input    = TankInput.FromParts(flags, throttle, steer),
            });
        }

        public void SendInput(int tick, int sequence, TankInput input)
        {
            RpcId(1, MethodName.SubmitInputRpc, tick, sequence,
                  input.PackFlags(), input.Throttle, input.Steer);
        }

        // ── Snapshot RPC (server → clients) ──────────────────────────────────

        // Server calls this to push a state snapshot to one client.
        // Data layout: [serverTick, ackedSeq, peerId0, px,py,pz, rx,ry,rz,rw,
        //               lx,ly,lz, ax,ay,az, peerId1, …]
        [Rpc(MultiplayerApi.RpcMode.Authority,
             TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
        public void ReceiveSnapshotRpc(Array data)
        {
            if (_client == null) return;
            var snap = DecodeSnapshot(data);
            _client.OnSnapshotReceived(snap, _remotes);
        }

        public void BroadcastSnapshot(StateSnapshot snap, int excludePeerId = -1)
        {
            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (peerId == excludePeerId) continue;
                var data = EncodeSnapshot(snap, peerId);
                RpcId(peerId, MethodName.ReceiveSnapshotRpc, data);
            }
        }

        // ── Snapshot encoding ─────────────────────────────────────────────────

        // Each client receives a snapshot with its own AckedSequence embedded.
        // Layout per entity: peerId, px,py,pz, rx,ry,rz,rw, lx,ly,lz, ax,ay,az, health
        private Array EncodeSnapshot(StateSnapshot snap, int targetPeerId)
        {
            var data = new Array();
            data.Add(snap.ServerTick);
            data.Add(snap.GetAckedSequenceFor(targetPeerId));
            foreach (var e in snap.Entities)
            {
                data.Add(e.PeerId);
                data.Add(e.Position.X);    data.Add(e.Position.Y);    data.Add(e.Position.Z);
                data.Add(e.Rotation.X);    data.Add(e.Rotation.Y);    data.Add(e.Rotation.Z);
                data.Add(e.Rotation.W);
                data.Add(e.LinearVelocity.X);  data.Add(e.LinearVelocity.Y);  data.Add(e.LinearVelocity.Z);
                data.Add(e.AngularVelocity.X); data.Add(e.AngularVelocity.Y); data.Add(e.AngularVelocity.Z);
                data.Add(e.Health);
            }
            return data;
        }

        private StateSnapshot DecodeSnapshot(Array data)
        {
            int idx = 0;
            var snap = new StateSnapshot
            {
                ServerTick    = data[idx++].AsInt32(),
                AckedSequence = data[idx++].AsInt32(),
            };

            var entities = new System.Collections.Generic.List<EntityState>();
            while (idx < data.Count)
            {
                entities.Add(new EntityState
                {
                    PeerId          = data[idx++].AsInt32(),
                    Position        = new Vector3(data[idx++].AsSingle(), data[idx++].AsSingle(), data[idx++].AsSingle()),
                    Rotation        = new Quaternion(data[idx++].AsSingle(), data[idx++].AsSingle(), data[idx++].AsSingle(), data[idx++].AsSingle()),
                    LinearVelocity  = new Vector3(data[idx++].AsSingle(), data[idx++].AsSingle(), data[idx++].AsSingle()),
                    AngularVelocity = new Vector3(data[idx++].AsSingle(), data[idx++].AsSingle(), data[idx++].AsSingle()),
                    Health          = data[idx++].AsSingle(),
                });
            }
            snap.Entities = entities.ToArray();
            return snap;
        }

        public Node3D GetTanksRoot() => _tanksRoot;
    }
}
