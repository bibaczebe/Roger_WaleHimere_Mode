using HarmonyLib;

namespace RodgerClans.Client.Patches
{
    // PvP locked to ON globally. Vanilla SetPVP toggle in mapie wciąż "działa"
    // (komunikat "$msg_pvpon/$msg_pvpoff", zapis do ZDO), ale każdy kaller
    // pytający IsPVPEnabled dostaje true niezależnie od ustawienia.
    // Damage między graczami spoza klanu zawsze przechodzi; FriendlyFirePatch
    // (v1.0) dalej zeruje damage między klanowiczami.
    [HarmonyPatch(typeof(Player), nameof(Player.IsPVPEnabled))]
    internal static class ForcePvpPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result) => __result = true;
    }
}
