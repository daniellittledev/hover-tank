using Godot;
using Godot.Collections;
using HoverTank.Network;
using System.Collections.Generic;

namespace HoverTank
{
    // Autoload singleton. Registered in project.godot as "NetworkManager".
    // Handles ENet lifecycle, player spawning, and RPC dispatch.
    // Call Initialize(tanksRoot) from the game scene before starting a session.
    public partial class NetworkManager : Node
    {
        private const int Port       = 7777;
        private const int MaxClients = 8;

        // Set to false on a dedicated server to skip spawning a local player.
        [Export] public bool SpawnLocalPlayer = true;

        private ServerSimulation? _server;
        private ClientSimulation? _client;
        private readonly Dictionary<int, RemoteEntityInterpolator> _remotes = new();

        // Incremented every _PhysicsProcess.
        public int CurrentTick { get; private set; }

        // The Node3D container where tank nodes are added.
        // Set by Initialize() once the game scene is ready.
        private Node3D? _tanksRoot;

        public override void _Ready()
        {
            // Detect dedicated server: launched headless or with --server flag.
            bool isDedicated = DisplayServer.GetName() == "headless"
                            || OS.GetCmdlineArgs().Contains("--server");
            if (isDedicated)
            {
                SpawnLocalPlayer = false;
                GD.Print("[Net] Dedicated server mode detected — will auto-host.");
                // Defer until GameSetup has called Initialize() and the scene tree
                // is ready. GameSetup will call StartHost() for us in that case, but
                // if the game is launched directly into Main.tscn we fall back to a
                // deferred call here so the tanks root is guaranteed to exist.
                CallDeferred(MethodName.StartHost);
            }
        }

        // Called by GameSetup._Ready() after the game scene is loaded.
        public void Initialize(Node3D tanksRoot)
        {
            _tanksRoot = tanksRoot;
        }

        // ── Connection setup ─────────────────────────────────────────────────

        // Single-player: no network, one local tank driven by LocalInputHandler.
        public void StartSinglePlayer()
        {
            if (_tanksRoot == null) { GD.PrintErr("[Net] Initialize() not called."); return; }

            var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                         .Instantiate<HoverTank>();
            tank.Name = "Tank_Local";
            tank.GlobalPosition = new Vector3(0f, 5f, 0f);
            _tanksRoot.AddChild(tank);

            var handler = new LocalInputHandler
            {
                Target      = tank,
                PlayerIndex = 0,
                Camera      = tank.AimCamera,
            };
            tank.AddChild(handler);

            // Unit commander: handles ally selection and orders in single-player.
            tank.AddChild(new UnitCommander { Name = "UnitCommander" });
        }

        public void StartHost()
        {
            if (_tanksRoot == null) { GD.PrintErr("[Net] Initialize() not called."); return; }

            var peer = new ENetMultiplayerPeer();
            var err  = peer.CreateServer(Port, MaxClients);
            if (err != Error.Ok) { GD.PrintErr($"[Net] Host failed: {err}"); return; }

            Multiplayer.MultiplayerPeer = peer;
            Multiplayer.PeerConnected    += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;

            _server = new ServerSimulation(this);
            _client = new ClientSimulation(this);

            if (SpawnLocalPlayer)
                SpawnPlayerRpc(Multiplayer.GetUniqueId());

            GD.Print($"[Net] Hosting on port {Port}");
        }

        public void StartClient(string address)
        {
            if (_tanksRoot == null) { GD.PrintErr("[Net] Initialize() not called."); return; }

            var peer = new ENetMultiplayerPeer();
            var err  = peer.CreateClient(address, Port);
            if (err != Error.Ok) { GD.PrintErr($"[Net] Connect failed: {err}"); return; }

            Multiplayer.MultiplayerPeer    = peer;
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed  += OnConnectionFailed;
            GD.Print($"[Net] Connecting to {address}:{Port}");
        }

        // Cleanly close whatever peer is open and reset simulation state.
        // Call this before changing back to the main menu.
        public void Disconnect()
        {
            if (Multiplayer.MultiplayerPeer != null &&
                Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer)
            {
                Multiplayer.MultiplayerPeer.Close();
                Multiplayer.MultiplayerPeer = null!;
            }

            // Unsubscribe events to avoid dangling handlers on the next session.
            Multiplayer.PeerConnected    -= OnPeerConnected;
            Multiplayer.PeerDisconnected -= OnPeerDisconnected;
            Multiplayer.ConnectedToServer -= OnConnectedToServer;
            Multiplayer.ConnectionFailed  -= OnConnectionFailed;

            _server    = null;
            _client    = null;
            _tanksRoot = null;

            foreach (var interp in _remotes.Values)
                interp.QueueFree();
            _remotes.Clear();

            GD.Print("[Net] Disconnected");
        }

        // ── Peer events ──────────────────────────────────────────────────────

        private void OnConnectedToServer()
        {
            _client = new ClientSimulation(this);
            GD.Print("[Net] Connected to server");
            EmitSignal(SignalName.ConnectedToServer);
        }

        private void OnConnectionFailed()
        {
            GD.PrintErr("[Net] Connection failed");
            EmitSignal(SignalName.ConnectionFailed);
        }

        [Signal] public delegate void ConnectedToServerEventHandler();
        [Signal] public delegate void ConnectionFailedEventHandler();

        private void OnPeerConnected(long peerId)
        {
            if (!Multiplayer.IsServer()) return;
            GD.Print($"[Net] Peer connected: {peerId}");
            Rpc(MethodName.SpawnPlayerRpc, (int)peerId);
        }

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
            if (_tanksRoot == null) return;

            bool isLocalPlayer = (peerId == Multiplayer.GetUniqueId());
            bool isServer      = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

            if (isServer)
            {
                var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                             .Instantiate<HoverTank>();
                tank.Name           = $"Tank_{peerId}";
                tank.GlobalPosition = new Vector3(peerId % 4 * 4f, 5f, 0f);
                _tanksRoot.AddChild(tank);
                _server!.RegisterTank(peerId, tank);

                if (isLocalPlayer && tank.Weapons != null)
                    tank.Weapons.Fired += (kind, xform) => BroadcastProjectileSpawn(kind, xform);
            }
            else
            {
                if (isLocalPlayer)
                {
                    var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                                 .Instantiate<HoverTank>();
                    tank.Name           = $"Tank_{peerId}";
                    tank.GlobalPosition = new Vector3(0f, 5f, 0f);
                    _tanksRoot.AddChild(tank);
                    _client!.SetLocalTank(tank);
                    _client.Camera = tank.AimCamera;

                    if (tank.Weapons != null)
                    {
                        tank.Weapons.FireMode = WeaponFireMode.LocalPrediction;
                        tank.Weapons.Fired   += (kind, xform) => SendFireToServer(kind, xform);
                    }
                }
                else
                {
                    var ghost = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                                  .Instantiate<HoverTank>();
                    ghost.Name           = $"Tank_{peerId}";
                    ghost.GlobalPosition = new Vector3(peerId % 4 * 4f, 5f, 0f);
                    ghost.Freeze         = true;

                    var cam = ghost.GetNodeOrNull<Camera3D>("CameraMount/Camera");
                    if (cam != null) cam.Current = false;

                    if (ghost.Weapons != null)
                        ghost.Weapons.FireMode = WeaponFireMode.NetworkGhost;

                    _tanksRoot.AddChild(ghost);

                    var interp = new RemoteEntityInterpolator();
                    interp.Name       = $"Interp_{peerId}";
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
            _tanksRoot?.GetNodeOrNull($"Tank_{peerId}")?.QueueFree();
            if (_remotes.TryGetValue(peerId, out var interp))
            {
                interp.QueueFree();
                _remotes.Remove(peerId);
            }
            _server?.UnregisterTank(peerId);
        }

        // ── Projectile fire RPCs ─────────────────────────────────────────────

        private const float ProjectileReplicationRange = 200f;

        private bool IsPeerInProjectileRange(int peerId, Vector3 origin)
        {
            if (_tanksRoot == null) return true;
            var tank = _tanksRoot.GetNodeOrNull<HoverTank>($"Tank_{peerId}");
            if (tank == null) return true;
            return tank.GlobalPosition.DistanceSquaredTo(origin) <=
                   ProjectileReplicationRange * ProjectileReplicationRange;
        }

        private void BroadcastProjectileSpawn(ProjectileKind kind, Transform3D xform)
        {
            var q = xform.Basis.GetRotationQuaternion();
            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (!IsPeerInProjectileRange(peerId, xform.Origin)) continue;
                RpcId(peerId, MethodName.SpawnProjectileRpc,
                      (byte)kind,
                      xform.Origin.X, xform.Origin.Y, xform.Origin.Z,
                      q.X, q.Y, q.Z, q.W);
            }
        }

        private void SendFireToServer(ProjectileKind kind, Transform3D xform)
        {
            var q = xform.Basis.GetRotationQuaternion();
            RpcId(1, MethodName.SubmitFireRpc,
                  (byte)kind,
                  xform.Origin.X, xform.Origin.Y, xform.Origin.Z,
                  q.X, q.Y, q.Z, q.W);
        }

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

            SpawnNetworkProjectile((ProjectileKind)kind, xform, shooterPeerId, isVisual: false);

            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (peerId == shooterPeerId) continue;
                if (!IsPeerInProjectileRange(peerId, xform.Origin)) continue;
                RpcId(peerId, MethodName.SpawnProjectileRpc, kind, px, py, pz, rx, ry, rz, rw);
            }
        }

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

        private void SpawnNetworkProjectile(ProjectileKind kind, Transform3D xform,
                                             int ownerPeerId, bool isVisual)
        {
            Rid ownerRid = default;
            if (ownerPeerId > 0 && _tanksRoot != null)
            {
                var tank = _tanksRoot.GetNodeOrNull<HoverTank>($"Tank_{ownerPeerId}");
                if (tank != null) ownerRid = tank.GetRid();
            }

            var (speed, damage, lifetime) = Projectile.GetStats(kind);
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

        // ── Tick loop ────────────────────────────────────────────────────────

        public override void _PhysicsProcess(double delta)
        {
            CurrentTick++;
            _server?.Tick(CurrentTick);
            _client?.Tick(CurrentTick, (float)delta);
        }

        // ── Input RPC (client → server) ───────────────────────────────────────

        [Rpc(MultiplayerApi.RpcMode.AnyPeer,
             TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
        public void SubmitInputRpc(int tick, int sequence, byte flags,
                                   float throttle, float steer, float aimYaw)
        {
            if (!Multiplayer.IsServer()) return;
            int senderId = Multiplayer.GetRemoteSenderId();
            _server?.OnInputReceived(senderId, new InputPacket
            {
                Tick     = tick,
                Sequence = sequence,
                Input    = TankInput.FromParts(flags, throttle, steer, aimYaw),
            });
        }

        public void SendInput(int tick, int sequence, TankInput input)
        {
            RpcId(1, MethodName.SubmitInputRpc, tick, sequence,
                  input.PackFlags(), input.Throttle, input.Steer, input.AimYaw);
        }

        // ── Snapshot RPC (server → clients) ──────────────────────────────────

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

        private Array EncodeSnapshot(StateSnapshot snap, int targetPeerId)
        {
            var data = new Array();
            data.Add(snap.ServerTick);
            data.Add(snap.GetAckedSequenceFor(targetPeerId));
            foreach (var e in snap.Entities)
            {
                data.Add(e.PeerId);
                data.Add(e.Position.X);         data.Add(e.Position.Y);         data.Add(e.Position.Z);
                data.Add(e.Rotation.X);         data.Add(e.Rotation.Y);         data.Add(e.Rotation.Z);
                data.Add(e.Rotation.W);
                data.Add(e.LinearVelocity.X);   data.Add(e.LinearVelocity.Y);   data.Add(e.LinearVelocity.Z);
                data.Add(e.AngularVelocity.X);  data.Add(e.AngularVelocity.Y);  data.Add(e.AngularVelocity.Z);
                data.Add(e.Health);
            }
            return data;
        }

        private StateSnapshot DecodeSnapshot(Array data)
        {
            int idx  = 0;
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

        public Node3D? GetTanksRoot() => _tanksRoot;
    }
}
