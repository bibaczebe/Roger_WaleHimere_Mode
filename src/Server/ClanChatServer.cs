using RodgerClans.Net;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Server
{
    // Routes a /t message to all online players in the sender's clan.
    //
    // Hosted-server quirk: the host has no peer entry for itself in ZNet.GetPeers(), so
    // peer iteration alone misses the host. If Player.m_localPlayer != null and the
    // host's SteamID is in the same clan as the sender, we dispatch to
    // ClanChatClient.Display locally on top of the peer iteration. On dedicated server
    // m_localPlayer is null and only peer iteration runs.
    public static class ClanChatServer
    {
        public static void Route(long senderUid, ClanChatPayload msg)
        {
            if (!ModRuntime.IsServer || !ModRuntime.RegistryReady) return;
            if (msg == null) return;
            if (string.IsNullOrEmpty(msg.SenderSteamId64)) return;

            if (!ClanRegistry.Instance.TryGetClan(msg.SenderSteamId64, out var senderClan))
            {
                RodgerClansPlugin.Log?.LogInfo(
                    $"/t from {msg.SenderSteamId64} dropped: sender not in any clan.");
                return;
            }

            int delivered = 0;
            var peers = ZNet.instance != null ? ZNet.instance.GetPeers() : null;
            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];
                    if (peer == null) continue;
                    string platformId = peer.m_socket?.GetHostName();
                    if (!PlayerIdHelper.TryParseSteamId(platformId, out var peerSteamId)) continue;
                    if (!ClanRegistry.Instance.TryGetClan(peerSteamId, out var peerClan)) continue;
                    if (peerClan.Id != senderClan.Id) continue;

                    ClanRpc.ClanChat.SendPackage(peer.m_uid, msg.ToPackage());
                    delivered++;
                }
            }

            // Hosted-server: deliver to the host locally (no peer entry for self).
            if (Player.m_localPlayer != null)
            {
                string hostSteamId = PlayerIdHelper.GetLocalSteamId64();
                if (!string.IsNullOrEmpty(hostSteamId)
                    && ClanRegistry.Instance.TryGetClan(hostSteamId, out var hostClan)
                    && hostClan.Id == senderClan.Id)
                {
                    Client.ClanChatClient.Display(msg);
                    delivered++;
                }
            }

            RodgerClansPlugin.Log?.LogInfo(
                $"/t routed: sender {msg.SenderSteamId64} ({senderClan.Id}) -> {delivered} recipient(s).");
        }
    }
}
