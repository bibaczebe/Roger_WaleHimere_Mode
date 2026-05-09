using HarmonyLib;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Client.Patches
{
    // Real signature in Valheim 0.221.12:
    //   public void OnNewChatMessage(GameObject go, long senderID, Vector3 pos,
    //                                Talker.Type type, UserInfo sender, string text)
    //
    // Harmony param names must match the originals exactly. We declare only the
    // parameters we mutate or branch on; the rest (`go`, `senderID`, `pos`) are omitted.
    //
    // Scope of THIS patch: tag in the in-world chat bubble. Vanilla resolves the bubble
    // text via sender.GetDisplayName() → sender.Name, so mutating sender.Name here
    // affects the bubble. The chat PANEL is rendered via Terminal.AddString(PlatformUserID,
    // ...) which uses the peer-roster name, not sender.Name — that surface is handled
    // separately by TerminalAddStringPatch. The /t sentinel is also handled there
    // (sentinel is on Terminal.AddString text, never reaches OnNewChatMessage).
    [HarmonyPatch(typeof(Chat), nameof(Chat.OnNewChatMessage))]
    internal static class ChatPanelPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref UserInfo sender, ref string text, Talker.Type type)
        {
            if (!ModRuntime.RegistryReady) return;
            if (sender == null) return;

            // Vanilla's OnNewChatMessage skips Ping (no AddString call). Tagging it would
            // do nothing visible — bail out for clarity.
            if (type == Talker.Type.Ping) return;

            string steamId = sender.UserId.m_userID;
            if (string.IsNullOrEmpty(steamId)) return;
            if (!ClanRegistry.Instance.TryGetClan(steamId, out var clan)) return;

            // Clone UserInfo so downstream listeners (other mods, anything reading the
            // original sender) see the unmodified instance — only this call's bubble path
            // gets the prefixed Name.
            sender = new UserInfo
            {
                Name = $"{ClanFormatter.TagPrefix(clan)} {sender.Name}",
                UserId = sender.UserId,
            };
        }
    }
}
