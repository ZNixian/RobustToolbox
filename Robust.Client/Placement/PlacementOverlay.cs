using Robust.Client.Graphics.Drawing;
using Robust.Client.Graphics.Overlays;

namespace Robust.Client.Placement
{
    public partial class PlacementManager
    {
        internal class PlacementOverlay : Overlay
        {
            private readonly PlacementManager _manager;
            public override bool AlwaysDirty => true;
            public override OverlaySpace Space => OverlaySpace.WorldSpace;

            public PlacementOverlay(PlacementManager manager) : base("placement")
            {
                _manager = manager;
                ZIndex = 100;
            }

            protected override void Draw(DrawingHandle handle)
            {
                _manager.Render((DrawingHandleWorld) handle);
            }
        }
    }
}
