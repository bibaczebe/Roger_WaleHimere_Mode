using System;
using HarmonyLib;
using RodgerClans.Net;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Server.Patches
{
    // Server-side hook fired once per peer right after authentication (ZNet.RPC_PeerInfo).
    // Sends a fresh RegistrySync ZPackage to that peer so it can hydrate its local
    // ClanRegistry before any visual patch needs it.
    //
    // Real signature in Valheim 0.221.12:
    //   private void RPC_PeerInfo(ZRpc rpc, ZPackage pkg)
    // We declare only `ZRpc rpc` — Harmony allows omitting parameters we don't use.
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class PeerInfoServerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZRpc rpc)
        {
            if (!ModRuntime.IsServer) return;
            if (rpc == null || ZNet.instance == null) return;

            // ZNet.GetPeer(ZRpc) is private in Valheim 0.221.12; replicate its lookup
            // through the public GetPeers() rather than reflecting into the private one.
            ZNetPeer peer = null;
            var peers = ZNet.instance.GetPeers();
            for (int i = 0; i < peers.Count; i++)
            {
                if (peers[i] != null && peers[i].m_rpc == rpc) { peer = peers[i]; break; }
            }
            if (peer == null) return;

            try
            {
                ClanRpc.RegistrySync.SendPackage(peer.m_uid, RegistryPackager.Build());
                RodgerClansPlugin.Log?.LogInfo(
                    $"Sent RegistrySync to peer {peer.m_uid} ({peer.m_playerName}): " +
                    $"{ClanRegistry.Instance.ClanCount} clans, " +
                    $"{ClanRegistry.Instance.MemberCount} members.");
            }
            catch (Exception e)
            {
                RodgerClansPlugin.Log?.LogError(
                    $"Sending RegistrySync to peer {peer.m_uid} failed: {e.Message}");
            }
        }
    }
}
