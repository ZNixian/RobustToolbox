using Robust.Client.Interfaces.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Robust.Client.Input
{
    internal sealed class ClydeInputManager : InputManager
    {
#pragma warning disable 649
        [Dependency] private readonly IClyde _clyde;
#pragma warning restore 649

        public override Vector2 MouseScreenPosition => _clyde.MouseScreenPosition;
    }
}
