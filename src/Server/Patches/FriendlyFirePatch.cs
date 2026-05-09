using HarmonyLib;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Server.Patches
{
    // Server-authoritative friendly fire prevention. Damage in Valheim 0.221.12 flows
    // from Character.Damage (sender side) → m_nview.InvokeRPC("RPC_Damage", hit) → server,
    // where the damage is actually applied. We patch Character.Damage with a Prefix
    // that zeroes hit.m_damage when both characters are players in the same clan; the
    // RPC then carries 0 damage and the victim takes nothing.
    //
    // Real signature in Valheim 0.221.12:
    //   public void Damage(HitData hit) {
    //       if (m_nview.IsValid()) {
    //           hit.m_weakSpot = FindWeakSpotIndex(hit.m_hitCollider);
    //           m_nview.InvokeRPC("RPC_Damage", hit);
    //       }
    //   }
    // No Player.Damage / Humanoid.Damage override — the base patch hits all subclasses.
    //
    // Knockback (HitData.m_pushForce, m_dir) is left untouched per spec — clan mates
    // can still bump each other away.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class FriendlyFirePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (!ModRuntime.IsServer) return true;
            if (!ModRuntime.RegistryReady) return true;
            if (hit == null) return true;

            if (!(__instance is Player victim)) return true;

            var attackerCharacter = hit.GetAttacker();
            if (!(attackerCharacter is Player attacker)) return true;

            // Self-damage (fall, own explosion) — let vanilla through.
            if (attacker == victim) return true;

            if (!ClanRegistry.Instance.AreAllies(victim, attacker)) return true;

            // DamageTypes.Modify multiplies every damage component (m_damage, m_blunt,
            // m_slash, m_pierce, m_chop, m_pickaxe, m_fire, m_frost, m_lightning,
            // m_poison, m_spirit) by the factor. 0f = full block.
            hit.m_damage.Modify(0f);
            return true;
        }
    }
}
