using System;
using System.Collections.Generic;
using System.IO;
using RodgerClans.Util;

namespace RodgerClans.Shared
{
    public sealed class ClanRegistry
    {
        private static readonly ClanRegistry _instance = new ClanRegistry();
        public static ClanRegistry Instance => _instance;

        private readonly Dictionary<string, Clan> _clansById =
            new Dictionary<string, Clan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ClanMember> _membersBySteamId =
            new Dictionary<string, ClanMember>(StringComparer.OrdinalIgnoreCase);
        // Peer roster: nickname → SteamID64. Server packages this in RegistrySync so
        // clients can resolve player names (chat, nameplate) to SteamIDs without their own peer access.
        private readonly Dictionary<string, string> _steamIdByPlayerName =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _playerNameBySteamId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsHydrated { get; private set; }
        public int ClanCount   => _clansById.Count;
        public int MemberCount => _membersBySteamId.Count;
        public int PeerCount   => _steamIdByPlayerName.Count;

        // Atomic replace of all in-memory state. Set IsHydrated only at the end so partial
        // failures elsewhere (e.g. HydrateFromPackage) leave the registry not-hydrated.
        public void Hydrate(
            IEnumerable<Clan> clans,
            IEnumerable<ClanMember> members,
            IEnumerable<KeyValuePair<string, string>> peerRoster)
        {
            _clansById.Clear();
            _membersBySteamId.Clear();
            _steamIdByPlayerName.Clear();
            _playerNameBySteamId.Clear();

            if (clans != null)
                foreach (var c in clans)
                    if (c != null && !string.IsNullOrEmpty(c.Id)) _clansById[c.Id] = c;

            if (members != null)
                foreach (var m in members)
                    if (m != null && !string.IsNullOrEmpty(m.SteamId64)) _membersBySteamId[m.SteamId64] = m;

            if (peerRoster != null)
                foreach (var kv in peerRoster)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                    _steamIdByPlayerName[kv.Key] = kv.Value;
                    _playerNameBySteamId[kv.Value] = kv.Key;
                }

            IsHydrated = true;
        }

        public void Clear()
        {
            _clansById.Clear();
            _membersBySteamId.Clear();
            _steamIdByPlayerName.Clear();
            _playerNameBySteamId.Clear();
            IsHydrated = false;
        }

        // ----- Lookups (cookbook-aligned naming) ---------------------------------

        // "Get clan for this SteamID64" — used wherever the cookbook writes
        // ClanRegistry.Instance.TryGetClan(steamId, out var clan).
        public bool TryGetClan(string steamId64, out Clan clan)
        {
            clan = null;
            if (string.IsNullOrEmpty(steamId64)) return false;
            if (!_membersBySteamId.TryGetValue(steamId64, out var m)) return false;
            return _clansById.TryGetValue(m.ClanId, out clan);
        }

        public bool TryGetClanForPlayerName(string playerName, out Clan clan)
        {
            clan = null;
            if (string.IsNullOrEmpty(playerName)) return false;
            if (!_steamIdByPlayerName.TryGetValue(playerName, out var steamId)) return false;
            return TryGetClan(steamId, out clan);
        }

        public bool TryGetClanForPlayer(Player player, out Clan clan)
        {
            clan = null;
            if (player == null) return false;
            string steamId = PlayerIdHelper.GetSteamIdForCharacter(player);
            if (steamId != null && TryGetClan(steamId, out clan)) return true;
            return TryGetClanForPlayerName(player.GetPlayerName(), out clan);
        }

        public string GetPlayerNameForSteamId(string steamId64) =>
            !string.IsNullOrEmpty(steamId64) && _playerNameBySteamId.TryGetValue(steamId64, out var name)
                ? name
                : null;

        public IReadOnlyList<Clan> GetAllClans() => new List<Clan>(_clansById.Values);
        public IReadOnlyList<ClanMember> GetAllMembers() => new List<ClanMember>(_membersBySteamId.Values);

        public IReadOnlyList<KeyValuePair<string, string>> GetPeerRoster()
        {
            var list = new List<KeyValuePair<string, string>>(_steamIdByPlayerName.Count);
            foreach (var kv in _steamIdByPlayerName) list.Add(kv);
            return list;
        }

        // Friendly-fire predicate. Server-side: returns true iff both players are members
        // of the same clan. Self-damage (a == b) returns false so vanilla self-damage paths
        // (fall, explosion-on-self) are unaffected.
        public bool AreAllies(Player a, Player b)
        {
            if (a == null || b == null) return false;
            if (a == b) return false;
            // GetSteamIdForPlayer applies the local-self Splatform fallback so the host
            // on a hosted server resolves correctly even when it has no peer entry for
            // itself in ZNet.GetPeers().
            string sa = PlayerIdHelper.GetSteamIdForPlayer(a);
            string sb = PlayerIdHelper.GetSteamIdForPlayer(b);
            if (string.IsNullOrEmpty(sa) || string.IsNullOrEmpty(sb)) return false;
            if (!_membersBySteamId.TryGetValue(sa, out var ma)) return false;
            if (!_membersBySteamId.TryGetValue(sb, out var mb)) return false;
            return string.Equals(ma.ClanId, mb.ClanId, StringComparison.OrdinalIgnoreCase);
        }

        // Deserialise a ZPackage produced by Server/RegistryPackager.Build and atomically
        // replace the in-memory state. Schema and field order MUST stay in lock-step with
        // RegistryPackager.cs.
        public void HydrateFromPackage(ZPackage p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            int schema = p.ReadInt();
            if (schema != 1)
                throw new InvalidDataException($"Unknown registry schema {schema}");

            int clanCount = p.ReadInt();
            var clans = new List<Clan>(clanCount);
            for (int i = 0; i < clanCount; i++)
            {
                string id   = p.ReadString();
                string name = p.ReadString();
                string tag  = p.ReadString();
                string hex  = p.ReadString();
                clans.Add(new Clan(id, name, tag, hex));
            }

            int memberCount = p.ReadInt();
            var members = new List<ClanMember>(memberCount);
            for (int i = 0; i < memberCount; i++)
            {
                string steamId = p.ReadString();
                string clanId  = p.ReadString();
                int rankInt    = p.ReadInt();
                members.Add(new ClanMember(steamId, clanId, (ClanRank)rankInt));
            }

            int peerCount = p.ReadInt();
            var roster = new List<KeyValuePair<string, string>>(peerCount);
            for (int i = 0; i < peerCount; i++)
            {
                string playerName = p.ReadString();
                string steamId    = p.ReadString();
                roster.Add(new KeyValuePair<string, string>(playerName, steamId));
            }

            Hydrate(clans, members, roster);
        }

#if DEBUG
        // Hardcoded smoke-test data — used only in Debug builds. Mirrors clans.example.json
        // so a quick local Debug build can exercise the visual patches before the JSON loader
        // and RPC wiring exist (steps 3 and 4).
        public void LoadHardcodedTestData()
        {
            var clans = new[]
            {
                new Clan("DRW", "DRWale", "DRW", "#3B82F6"),
                new Clan("SMR", "Smrody", "SMR", "#EF4444"),
                new Clan("TRL", "Trole",  "TRL", "#22C55E"),
            };
            var members = new[]
            {
                new ClanMember("76561198343378545", "DRW", ClanRank.Jarl),
                new ClanMember("76561198349595799", "SMR", ClanRank.Member),
            };
            Hydrate(clans, members, new KeyValuePair<string, string>[0]);
        }
#endif
    }
}
