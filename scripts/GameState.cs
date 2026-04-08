using Godot;

namespace HoverTank
{
    public enum GameMode { SinglePlayer, NetworkHost, NetworkJoin, SplitScreen }

    // Autoload singleton. Carries game-mode intent from the main menu into the
    // game scene. Registered in project.godot before NetworkManager so it is
    // available when NetworkManager._Ready() runs.
    public partial class GameState : Node
    {
        public static GameState Instance { get; private set; } = null!;

        public GameMode Mode    { get; set; } = GameMode.SinglePlayer;
        public string JoinAddress { get; set; } = "127.0.0.1";

        public override void _Ready() => Instance = this;
    }
}
