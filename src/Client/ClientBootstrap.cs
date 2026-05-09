using System.Collections;
using BepInEx.Logging;
using RodgerClans.Shared;
using UnityEngine;

namespace RodgerClans.Client
{
    // Client-side watchdog. Started by Plugin.Awake's dispatch coroutine when ZNet exists
    // and we're not the server. Waits up to TimeoutSeconds for RegistrySync to arrive; if
    // it doesn't, logs a single passive-mode line. Every visual / chat patch checks
    // ModRuntime.RegistryReady (== ClanRegistry.IsHydrated) and bails out, so when the
    // server has no mod the patches naturally do nothing.
    public static class ClientBootstrap
    {
        private const float TimeoutSeconds = 5f;

        public static IEnumerator WatchForRegistrySync(ManualLogSource log)
        {
            log.LogInfo($"ClientBootstrap: waiting up to {TimeoutSeconds}s for RegistrySync from server.");

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ClanRegistry.Instance.IsHydrated)
                {
                    // OnRegistrySyncClient already logged the hydration counts.
                    yield break;
                }
                yield return null;
            }

            if (!ClanRegistry.Instance.IsHydrated)
            {
                log.LogInfo("Server has no RodgerClans — running in passive mode.");
            }
        }
    }
}
