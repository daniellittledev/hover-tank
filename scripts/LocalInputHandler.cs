using Godot;
using HoverTank.Network;

namespace HoverTank
{
    // Reads keyboard / mouse input each physics tick and feeds TankInput to its
    // target tank.  Used for single-player mode and split-screen local play.
    //
    //   PlayerIndex 0  →  P1 actions: move_forward / move_backward / move_left /
    //                      move_right / jump_jet / fire_weapon / next_weapon
    //   PlayerIndex 1  →  P2 actions: p2_move_forward / … (arrow keys etc.)
    public partial class LocalInputHandler : Node
    {
        public HoverTank?    Target      { get; set; }
        public int           PlayerIndex { get; set; } = 0;
        // Set by NetworkManager after the tank and camera are spawned.
        public FollowCamera? Camera      { get; set; }

        private string Pfx => PlayerIndex == 0 ? "" : "p2_";

        private bool _jumpLatch;

        public override void _PhysicsProcess(double _)
        {
            if (Target == null) return;

            if (Input.IsActionJustPressed(Pfx + "jump_jet"))
                _jumpLatch = true;

            var input = new TankInput
            {
                Throttle        = Input.GetAxis(Pfx + "move_backward", Pfx + "move_forward"),
                Steer           = Input.GetAxis(Pfx + "move_right",    Pfx + "move_left"),
                JumpJet         = Input.IsActionPressed(Pfx + "jump_jet"),
                JumpJustPressed = _jumpLatch,
                AimYaw          = Camera?.CurrentYaw ?? 0f,
            };

            Target.SetInput(input);
            _jumpLatch = false;
        }
    }
}
