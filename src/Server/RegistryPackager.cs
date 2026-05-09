using System.Collections.Generic;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Server
{
    // Builds the ZPackage sent to a single peer on connect, and re-readable on the client by
    // ClanRegistry.HydrateFromPackage. Schema and field order MUST match exactly between
    // the writer here and the reader in ClanRegistry.
    //
    // Schema 1:
    //   int    schema = 1
    //   int    clanCount;   for each: string id, string name, string tag, string colorHex
    //   int    memberCount; for each: string steamId64, string clanId, int rank
    //   int    peerCount;   for each: string playerName, string steamId64
    public static class RegistryPackager
    {
        private const int Schema = 1;

        public static ZPackage Build()
        {
            var p = new ZPackage();
            p.Write(Schema);

            var clans = ClanRegistry.Instance.GetAllClans();
            p.Write(clans.Count);
            for (int i = 0; i < clans.Count; i++)
            {
                var c = clans[i];
                p.Write(c.Id ?? string.Empty);
                p.Write(c.Name ?? string.Empty);
                p.Write(c.Tag ?? string.Empty);
                p.Write(c.ColorHex ?? string.Empty);
            }

            var members = ClanRegistry.Instance.GetAllMembers();
            p.Write(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                p.Write(m.SteamId64 ?? string.Empty);
                p.Write(m.ClanId ?? string.Empty);
                p.Write((int)m.Rank);
            }

            // Active peer roster: nickname + SteamID64 (no Steam_ prefix). Lets clients
            // resolve names → clans without their own peer access.
            var roster = new List<KeyValuePair<string, string>>();
            var peers = ZNet.instance != null ? ZNet.instance.GetPeers() : null;
            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];
                    if (peer == null) continue;
                    if (string.IsNullOrEmpty(peer.m_playerName)) continue;
                    string platformId = peer.m_socket?.GetHostName();
                    if (!PlayerIdHelper.TryParseSteamId(platformId, out var steamId)) continue;
                    roster.Add(new KeyValuePair<string, string>(peer.m_playerName, steamId));
                }
            }

            p.Write(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                p.Write(roster[i].Key);
                p.Write(roster[i].Value);
            }

            return p;
        }
    }
}
