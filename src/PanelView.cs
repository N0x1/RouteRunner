using UnityEngine;

namespace RouteRunner
{
    // Disabled when there is nothing to draw, so Unity does not dispatch idle IMGUI events.
    internal sealed class PanelView : MonoBehaviour
    {
        internal Plugin Owner;
        void OnGUI() { Owner?.RenderPanel(); }
    }
}
