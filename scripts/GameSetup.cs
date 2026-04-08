using Godot;

namespace HoverTank
{
    // Attached to the root node of Main.tscn.
    // Bridges the menu → game transition: tells NetworkManager which node to use
    // as the tanks container, then starts the appropriate game mode from GameState.
    public partial class GameSetup : Node
    {
        public override void _Ready()
        {
            var nm = GetNode<NetworkManager>("/root/NetworkManager");
            nm.Initialize(GetNode<Node3D>("Tanks"));

            switch (GameState.Instance.Mode)
            {
                case GameMode.SinglePlayer:
                    nm.StartSinglePlayer();
                    break;

                case GameMode.NetworkHost:
                    nm.StartHost();
                    break;

                case GameMode.NetworkJoin:
                    nm.StartClient(GameState.Instance.JoinAddress);
                    break;

                // SplitScreen is handled entirely by SplitScreenManager in SplitScreen.tscn.
                // If this scene is loaded in split-screen mode by accident, fall back to SP.
                case GameMode.SplitScreen:
                    nm.StartSinglePlayer();
                    break;
            }
        }
    }
}
