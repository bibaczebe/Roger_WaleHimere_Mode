using HarmonyLib;
using UnityEngine;

namespace RodgerClans.Client.Patches
{
    // Real signature in Valheim 0.221.12:
    //   private void RPC_Say(long sender, int ctype, UserInfo user, string text) {
    //       if (Player.m_localPlayer == null) return;
    //       float num = 0f;
    //       switch (ctype) {
    //           case 0: num = m_visperDistance; break;  // Whisper 4f
    //           case 1: num = m_normalDistance; break;  // Normal 15f
    //           case 2: num = m_shoutDistance;  break;  // Shout 70f
    //       }
    //       if (Vector3.Distance(transform.position, Player.m_localPlayer.transform.position) < num
    //           && Chat.instance) {
    //           Vector3 headPoint = m_character.GetHeadPoint();
    //           Chat.instance.OnNewChatMessage(gameObject, sender, headPoint,
    //                                           (Type)ctype, user, text);
    //       }
    //   }
    //
    // Wysyłka (Talker.Say) i tak idzie do każdego peera — filtr odległości jest TYLKO
    // tutaj, na odbiorcy. Dla Normal (ctype=1) podmieniamy ścieżkę: forwardujemy do
    // OnNewChatMessage bez sprawdzania dystansu. Whisper i Shout zachowują vanilla
    // behaviour. Bubble in-world i tak zostanie ucięty osobnym dystans-checkiem
    // wewnątrz OnNewChatMessage (Minimap.m_nomapPingDistance), więc tylko panel czatu
    // staje się globalny — zgodnie ze specyfikacją.
    //
    // RPC_Say jest private → atrybut z literalnym stringiem (jak ZNet.RPC_PeerInfo).
    [HarmonyPatch(typeof(Talker), "RPC_Say")]
    internal static class GlobalNormalChatPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Talker __instance, long sender, int ctype,
                                   UserInfo user, string text)
        {
            // Tylko Normal — pozostałe typy zostawiamy vanilla.
            if (ctype != (int)Talker.Type.Normal) return true;

            if (Player.m_localPlayer == null) return true;
            if (Chat.instance == null) return true;

            var character = __instance.GetComponent<Character>();
            if (character == null) return true;

            Vector3 headPoint = character.GetHeadPoint();
            Chat.instance.OnNewChatMessage(
                __instance.gameObject, sender, headPoint,
                Talker.Type.Normal, user, text);

            return false;
        }
    }
}
