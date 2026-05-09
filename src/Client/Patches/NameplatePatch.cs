using HarmonyLib;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Client.Patches
{
    // Real signature in Valheim 0.221.12:
    //   public override string GetHoverName()
    //   { return CensorShittyWords.FilterUGC(GetPlayerName(), UGCType.CharacterName, GetPlayerID()); }
    //
    // Skill cookbook (section 2) suggested patching Character.GetHoverName with an
    // `is Player` filter. That doesn't work in 0.221.12 — Player.GetHoverName is an
    // override that does NOT call base, so a postfix on Character.GetHoverName never
    // fires for players. We patch Player.GetHoverName directly.
    [HarmonyPatch(typeof(Player), nameof(Player.GetHoverName))]
    internal static class NameplatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ref string __result)
        {
            if (!ModRuntime.RegistryReady) return;
            if (__instance == null) return;

            // For the local player on a hosted server, peer iteration may fail to find
            // a self entry — go through GetLocalSteamId64 which falls back to Splatform.
            string steamId = (__instance == Player.m_localPlayer)
                ? PlayerIdHelper.GetLocalSteamId64()
                : PlayerIdHelper.GetSteamIdForCharacter(__instance);

            if (string.IsNullOrEmpty(steamId)) return;
            if (!ClanRegistry.Instance.TryGetClan(steamId, out var clan)) return;

            __result = $"{ClanFormatter.TagPrefix(clan)} {__result}";
        }
    }
}
