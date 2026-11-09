using Il2Cpp;
using Il2CppTLD.Trader;
using UnityEngine;
using SkyCoopServer;

namespace SkyCoop
{
    public static class TraderSync
    {
        private static TraderManager s_Manager;

        private static string s_PendingState = "";
        private static string s_LastHostState = "";

        private static bool s_HostRestoreAttempted;
        private static bool s_Applying;

        private static float s_NextUpdate;

        private const float UpdateInterval = 1f;

        private static TraderManager GetManager()
        {
            if (s_Manager == null)
            {
                s_Manager = UnityEngine.Object.FindObjectOfType<TraderManager>();
            }

            return s_Manager;
        }

        public static void Receive(string state)
        {
            if (string.IsNullOrEmpty(state))
                return;

            s_PendingState = state;
        }

        private static bool ApplyState(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
                return false;

            TraderManager manager = GetManager();

            if (manager == null)
                return false;

            try
            {
                s_Applying = true;

                TraderState state =
                    Utils.DeserializeObject<TraderState>(serialized);

                if (state == null)
                    return false;

                manager.m_CurrentState = state;

                return true;
            }
            catch (System.Exception e)
            {
                Logger.Log(
                    ConsoleColor.Red,
                    "[TraderSync] Failed to apply trader state: " + e.Message
                );

                return false;
            }
            finally
            {
                s_Applying = false;
            }
        }

        private static string CaptureState()
        {
            TraderManager manager = GetManager();

            if (manager == null || manager.m_CurrentState == null)
                return "";

            try
            {
                return Utils.SerializeObject(manager.m_CurrentState);
            }
            catch (System.Exception e)
            {
                Logger.Log(
                    ConsoleColor.Red,
                    "[TraderSync] Failed to capture trader state: " + e.Message
                );

                return "";
            }
        }

        public static void Update()
        {
            if (s_Applying)
                return;

            if (Time.unscaledTime < s_NextUpdate)
                return;

            s_NextUpdate = Time.unscaledTime + UpdateInterval;

            // HOST: server state is authoritative.
            if (ModMain.Server != null && ModMain.Server.m_IsReady)
            {
                UpdateHost();
                return;
            }

            // CLIENT: wait until TraderManager exists, then apply the latest snapshot.
            if (!string.IsNullOrEmpty(s_PendingState))
            {
                if (ApplyState(s_PendingState))
                {
                    Logger.Log(
                        ConsoleColor.Green,
                        "[TraderSync] Server trader state applied."
                    );

                    s_PendingState = "";
                }
            }
        }

        private static void UpdateHost()
        {
            TraderManager manager = GetManager();

            if (manager == null || manager.m_CurrentState == null)
                return;

            // Restore saved server state once, after the game creates TraderManager.
            if (!s_HostRestoreAttempted)
            {
                if (!string.IsNullOrEmpty(ModMain.Server.m_TraderStateJson))
                {
                    if (!ApplyState(ModMain.Server.m_TraderStateJson))
                        return;

                    Logger.Log(
                        ConsoleColor.Green,
                        "[TraderSync] Saved trader state restored."
                    );
                }

                s_HostRestoreAttempted = true;

                s_LastHostState = ModMain.Server.m_TraderStateJson ?? "";
            }

            string currentState = CaptureState();

            if (string.IsNullOrEmpty(currentState))
                return;

            if (currentState == s_LastHostState)
                return;

            s_LastHostState = currentState;

            // Keep the latest state in the server save data.
            ModMain.Server.m_TraderStateJson = currentState;

            // Send the authoritative state to every connected player.
            ServerSend.BroadcastTraderState(
                currentState,
                ModMain.Server
            );

            Logger.Log(
                ConsoleColor.Green,
                "[TraderSync] Trader state synchronized."
            );
        }

        public static void OnSceneChanged()
        {
            s_Manager = null;
            s_PendingState = "";

            // Do not reset the host's saved state or restore flag here.
            // The manager can be recreated when changing scenes.
        }
    }
}