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
                });
            }
            snap.Entities = entities.ToArray();
            return snap;
        }

        public Node3D GetTanksRoot() => _tanksRoot;
    }
}
