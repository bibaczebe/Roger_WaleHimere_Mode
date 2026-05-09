using System;
using System.Collections;
using BepInEx.Logging;
using Jotunn.Entities;
using Jotunn.Managers;
using RodgerClans.Client;
using RodgerClans.Server;
using RodgerClans.Shared;

namespace RodgerClans.Net
{
    // Two custom RPCs registered through Jotunn's NetworkManager:
    //   RodgerClans.RegistrySync   — server → one client, fired from PeerInfoServerPatch.
    //   RodgerClans.ClanChat       — client ↔ server, /t routing (handlers wired in step 6).
    //
    // Registration must happen in Plugin.Awake before any caller (Harmony patch, command,
    // bootstrap) can invoke them.
    public static class ClanRpc
    {
        public const string RpcRegistrySync = "RodgerClans.RegistrySync";
        public const string RpcClanChat     = "RodgerClans.ClanChat";

        public static CustomRPC RegistrySync;
        public static CustomRPC ClanChat;

        private static ManualLogSource _log;

        public static void Register(ManualLogSource log)
        {
            _log = log;

            RegistrySync = NetworkManager.Instance.AddRPC(
                RpcRegistrySync,
                serverReceive: null,
                clientReceive: OnRegistrySyncClient);
            log.LogInfo($"RPC registered: {RpcRegistrySync}");

            ClanChat = NetworkManager.Instance.AddRPC(
                RpcClanChat,
                serverReceive: OnClanChatServer,
                clientReceive: OnClanChatClient);
            log.LogInfo($"RPC registered: {RpcClanChat}");
        }

        // ----- handlers ----------------------------------------------------------

        private static IEnumerator OnRegistrySyncClient(long sender, ZPackage package)
        {
            try
            {
                ClanRegistry.Instance.HydrateFromPackage(package);
                _log?.LogInfo(
                    $"Hydrated registry from server: " +
                    $"{ClanRegistry.Instance.ClanCount} clans, " +
                    $"{ClanRegistry.Instance.MemberCount} members, " +
                    $"{ClanRegistry.Instance.PeerCount} peers.");
            }
            catch (Exception e)
            {
                _log?.LogError($"Failed to hydrate registry from RegistrySync: {e.Message}");
            }
            yield return null;
        }

        // /t message arrived from a client. Server filters peers by clan and re-emits.
        private static IEnumerator OnClanChatServer(long sender, ZPackage package)
        {
            try
            {
                var msg = ClanChatPayload.Read(package);
                ClanChatServer.Route(sender, msg);
            }
            catch (Exception e)
            {
                _log?.LogError($"OnClanChatServer failed: {e.Message}");
            }
            yield return null;
        }

        // /t message routed from server to this client. Render to chat panel.
        private static IEnumerator OnClanChatClient(long sender, ZPackage package)
        {
            try
            {
                var msg = ClanChatPayload.Read(package);
                ClanChatClient.Display(msg);
            }
            catch (Exception e)
            {
                _log?.LogError($"OnClanChatClient failed: {e.Message}");
            }
            yield return null;
        }
    }
}
