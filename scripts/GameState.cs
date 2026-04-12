using Godot;

namespace HoverTank
{
	public enum GameMode { SinglePlayer, NetworkHost, NetworkJoin, SplitScreen }

	// Sub-mode chosen inside SinglePlayer:
	//   TestDrive     – empty sandbox: player tank only, no allies, no enemies.
	//   StandardWaves – classic wave-based survival (WaveManager).
	public enum SinglePlayerMode { TestDrive, StandardWaves }

	// Autoload singleton. Carries game-mode intent from the main menu into the
	// game scene. Registered in project.godot before NetworkManager so it is
	// available when NetworkManager._Ready() runs.
	public partial class GameState : Node
	{
		public static GameState Instance { get; private set; } = null!;

		public GameMode         Mode             { get; set; } = GameMode.SinglePlayer;
		public SinglePlayerMode SinglePlayerMode { get; set; } = SinglePlayerMode.StandardWaves;
		public string           JoinAddress      { get; set; } = "127.0.0.1";

		public override void _Ready() => Instance = this;
	}
}
