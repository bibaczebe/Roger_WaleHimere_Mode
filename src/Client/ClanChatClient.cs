using System;
using RodgerClans.Net;
using RodgerClans.Server;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Client
{
    // Client-side helpers for the /t channel. Send packs the message and routes it to
    // the server, which fans it out to all clan mates (including back to us). Display
    // formats the line with the clan tag + light-blue body and pushes it to the chat
    // panel via the bare AddString(string) overload — bypassing TerminalAddStringPatch
    // so the line keeps our colors instead of being re-formatted as a normal chat msg.
    public static class ClanChatClient
    {
        private const string LightBlueHex = "#9ECEFF";

        public static void Send(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (!ModRuntime.RegistryReady)
            {
                Console.instance?.Print("RodgerClans: registry nie gotowy.");
                return;
            }

            string localSteam = PlayerIdHelper.GetLocalSteamId64();
            if (string.IsNullOrEmpty(localSteam))
            {
                Console.instance?.Print("RodgerClans: nie udało się ustalić twojego SteamID.");
                return;
            }

            if (!ClanRegistry.Instance.TryGetClan(localSteam, out _))
            {
                Console.instance?.Print("Nie należysz do żadnego klanu.");
                return;
            }

            var payload = new ClanChatPayload
            {
                SenderSteamId64 = localSteam,
                Message = message,
            };

            try
            {
                // On hosted server (host == client + server) the host has no server-peer
                // entry for itself in ZNet.GetPeers — route locally without RPC.
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    ClanChatServer.Route(0L, payload);
                }
                else
                {
                    long serverUid = ResolveServerPeerUid();
                    if (serverUid == 0L)
                    {
                        Console.instance?.Print("RodgerClans: brak połączenia z serwerem.");
                        return;
                    }
                    ClanRpc.ClanChat.SendPackage(serverUid, payload.ToPackage());
                }
            }
            catch (Exception e)
            {
                RodgerClansPlugin.Log?.LogError($"/t send failed: {e.Message}");
                Console.instance?.Print("RodgerClans: nie udało się wysłać.");
            }
        }

        // Find the server peer's uid via the public m_server flag on ZNetPeer.
        // ZRoutedRpc.GetServerPeerID was present in older Valheim versions but is gone
        // in 0.221.12 — we read the flag directly from the peer roster instead.
        // Returns 0L when no server peer is registered (hosted server case — caller
        // handles that branch and routes locally).
        private static long ResolveServerPeerUid()
        {
            if (ZNet.instance == null) return 0L;
            var peers = ZNet.instance.GetPeers();
            if (peers == null) return 0L;
            for (int i = 0; i < peers.Count; i++)
            {
                var p = peers[i];
                if (p != null && p.m_server) return p.m_uid;
            }
            return 0L;
        }

        public static void Display(ClanChatPayload msg)
        {
            if (msg == null) return;
            if (Chat.instance == null) return;
            if (!ClanRegistry.Instance.TryGetClan(msg.SenderSteamId64, out var clan)) return;

            string senderName = ResolveName(msg.SenderSteamId64);
            string tag        = ClanFormatter.TagPrefix(clan);
            string finalText  =
                $"{tag} <color={LightBlueHex}>{senderName}: {msg.Message}</color>";

            Chat.instance.AddString(finalText);
        }

        private static string ResolveName(string steamId64)
        {
            if (string.IsNullOrEmpty(steamId64)) return "?";

            string local = PlayerIdHelper.GetLocalSteamId64();
            if (local == steamId64 && Player.m_localPlayer != null)
                return Player.m_localPlayer.GetPlayerName();

            string fromRegistry = ClanRegistry.Instance.GetPlayerNameForSteamId(steamId64);
            if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry;

            // Fallback for hosted host: the host's local registry has an empty peer
            // roster (only RegistrySync recipients populate it; host never receives
            // its own sync). Query ZNet directly.
            if (ZNet.instance != null)
            {
                var players = ZNet.instance.GetPlayerList();
                if (players != null)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        if (p.m_userInfo.m_id.m_userID == steamId64) return p.m_name;
                    }
                }
            }

            return steamId64;
        }
    }
}
