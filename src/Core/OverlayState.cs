namespace RouteRunner.Core
{
    public enum OverlayMode { Hidden, Hud, Panel }

    public static class OverlayState
    {
        public static OverlayMode Mode(bool available, bool panel, bool routeActive, bool idleHint, float now, float statusUntil, bool showHud = true)
        {
            if (!available) return OverlayMode.Hidden;
            if (panel) return OverlayMode.Panel;
            if (!showHud) return OverlayMode.Hidden;
            return routeActive || idleHint || now < statusUntil ? OverlayMode.Hud : OverlayMode.Hidden;
        }
    }
}
