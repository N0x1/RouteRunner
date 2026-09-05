using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RouteRunner
{
    internal static class GameBridge
    {
        internal static readonly FieldInfo AnimatorField = typeof(FirstPersonController).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static readonly FieldInfo ArmsField = typeof(PlayerSetup).GetField("fpArms", BindingFlags.Instance | BindingFlags.NonPublic);
        static FirstPersonController healthPlayer;
        static PlayerHealth playerHealth;
        static int mapHandle = int.MinValue;
        static string mapName;
        internal static void ClearPlayerCache() { healthPlayer = null; playerHealth = null; }

        internal static bool Exploring => SteamLobby.Instance != null && SteamLobby.Instance.isInExplorationMap;
        internal static FirstPersonController Player => Settings.Instance == null ? null : Settings.Instance.localPlayer;
        internal static string Map
        {
            get
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.handle != mapHandle) { mapHandle = scene.handle; mapName = scene.name; }
                return mapName;
            }
        }
        internal static bool MenuOpen => PauseManager.Instance != null && (PauseManager.Instance.pause || PauseManager.Instance.chatting || PauseManager.Instance.rebinding || PauseManager.Instance.otherPauseBools);
        internal static bool Ready
        {
            get
            {
                var p = Player;
                if (!Exploring || p == null || !p.IsOwner || !p.gameObject.activeInHierarchy) return false;
                if (PauseManager.Instance == null || PauseManager.Instance.inMainMenu || PauseManager.Instance.inVictoryMenu) return false;
                if (healthPlayer != p || playerHealth == null) { healthPlayer = p; playerHealth = p.GetComponent<PlayerHealth>(); }
                return playerHealth != null && playerHealth.health > 0 && !playerHealth.isKilled;
            }
        }

        internal static byte State(FirstPersonController p) => (byte)((p.safeGrounded ? 1 : 0) | (p.isCrouching ? 2 : 0) | (p.isSliding ? 4 : 0) | (p.isSprinting ? 8 : 0) | (p.isLeaning ? 16 : 0));
    }
}
