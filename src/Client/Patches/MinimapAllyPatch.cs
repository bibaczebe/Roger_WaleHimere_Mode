using System.Collections.Generic;
using HarmonyLib;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Client.Patches
{
    // Skill cookbook section 6 patched Minimap.UpdatePlayerPins and tracked injected pins
    // in a private dictionary. In Valheim 0.221.12 the cleaner hook is ZNet.GetOtherPublicPlayers:
    // vanilla Minimap.UpdatePlayerPins calls it to obtain the list of players to draw,
    // and we expand that list with our clan mates whose `m_publicPosition` is false.
    // This way vanilla manages pin lifecycle, styling and cleanup — we just inject entries.
    //
    // Real signature in 0.221.12:
    //   public void GetOtherPublicPlayers(List<PlayerInfo> playerList) {
    //       foreach (PlayerInfo p in m_players)
    //           if (p.m_publicPosition && !p.m_characterID.IsNone()
    //               && !(p.m_characterID == m_characterID))
    //               playerList.Add(p);
    //   }
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.GetOtherPublicPlayers))]
    internal static class MinimapAllyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZNet __instance, List<ZNet.PlayerInfo> playerList)
        {
            if (!ModRuntime.RegistryReady) return;
            if (Player.m_localPlayer == null) return;

            string myId = PlayerIdHelper.GetLocalSteamId64();
            if (string.IsNullOrEmpty(myId)) return;
            if (!ClanRegistry.Instance.TryGetClan(myId, out var myClan)) return;

            var all = __instance.GetPlayerList();
            if (all == null) return;

            // Track who's already in the list (vanilla added the public-position ones)
            // so we don't double-add. We extend with the private-position clan mates.
            var alreadyHave = new HashSet<ZDOID>();
            for (int i = 0; i < playerList.Count; i++)
            {
                alreadyHave.Add(playerList[i].m_characterID);
            }

            ZDOID myCharId = Player.m_localPlayer.GetZDOID();

            for (int i = 0; i < all.Count; i++)
            {
                var player = all[i];
                if (player.m_publicPosition) continue;        // already added by vanilla
                if (player.m_characterID.IsNone()) continue;
                if (player.m_characterID == myCharId) continue;
                if (!alreadyHave.Add(player.m_characterID)) continue;

                string theirId = player.m_userInfo.m_id.m_userID;
                if (string.IsNullOrEmpty(theirId)) continue;
                if (!ClanRegistry.Instance.TryGetClan(theirId, out var theirClan)) continue;
                if (theirClan.Id != myClan.Id) continue;

                // PlayerInfo is a struct — copy, flip m_publicPosition so vanilla pin
                // rendering accepts it, append.
                var injected = player;
                injected.m_publicPosition = true;
                playerList.Add(injected);
            }
        }
    }
}
