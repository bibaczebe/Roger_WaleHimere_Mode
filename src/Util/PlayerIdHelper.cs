using Splatform;

namespace RodgerClans.Util
{
    public static class PlayerIdHelper
    {
        public const string SteamPrefix = "Steam_";

        public static string ToPlatformId(string steamId64) =>
            string.IsNullOrEmpty(steamId64) ? null : SteamPrefix + steamId64;

        public static bool TryParseSteamId(string platformId, out string steamId64)
        {
            if (!string.IsNullOrEmpty(platformId) && platformId.StartsWith(SteamPrefix))
            {
                steamId64 = platformId.Substring(SteamPrefix.Length);
                return true;
            }
            steamId64 = null;
            return false;
        }

        // Resolve a Character (must be a Player) to its SteamID64 via the peer roster.
        // Server-side and client-side both work as long as ZNet has peers populated.
        public static string GetSteamIdForCharacter(Character character)
        {
            if (character == null || ZNet.instance == null) return null;
            var zdoid = character.GetZDOID();
            var peers = ZNet.instance.GetPeers();
            if (peers == null) return null;
            foreach (var p in peers)
            {
                if (p == null) continue;
                if (p.m_characterID != zdoid) continue;
                string platformId = p.m_socket?.GetHostName();
                return TryParseSteamId(platformId, out var steamId) ? steamId : null;
            }
            return null;
        }

        // Resolve a Player to its SteamID64 with the local-self fallback applied.
        // Use this in patches that may run for either remote or local players (nameplate,
        // friendly fire) — for the local one we go through GetLocalSteamId64 which falls
        // back to Splatform when the host has no peer entry for itself.
        public static string GetSteamIdForPlayer(Player player)
        {
            if (player == null) return null;
            return (player == Player.m_localPlayer)
                ? GetLocalSteamId64()
                : GetSteamIdForCharacter(player);
        }

        // Local player's SteamID64. Two-step resolution:
        //   1. Player.m_localPlayer → GetSteamIdForCharacter (peer iteration). Works
        //      for pure clients that have a peer entry for self.
        //   2. Splatform: PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.
        //      Works for the host on Host & Play where ZNet.GetPeers() may not contain
        //      a self entry.
        // Returns 17 ASCII digits of SteamID64, or null if neither path works.
        public static string GetLocalSteamId64()
        {
            var local = Player.m_localPlayer;
            if (local != null)
            {
                var viaPeer = GetSteamIdForCharacter(local);
                if (!string.IsNullOrEmpty(viaPeer)) return viaPeer;
            }

            try
            {
                var dp = PlatformManager.DistributionPlatform;
                if (dp == null) return null;
                var lu = dp.LocalUser;
                if (lu == null) return null;
                var pid = lu.PlatformUserID;
                if (!pid.IsValid) return null;
                // m_platform is a Platform struct (enum-like); ToString() yields the canonical
                // platform name like "Steam".
                if (pid.m_platform.ToString() != "Steam") return null;
                return string.IsNullOrEmpty(pid.m_userID) ? null : pid.m_userID;
            }
            catch
            {
                return null;
            }
        }
    }
}
