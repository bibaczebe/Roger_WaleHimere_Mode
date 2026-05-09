using HarmonyLib;
using Splatform;
using UnityEngine;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Client.Patches
{
    // Why a separate patch on Terminal.AddString(PlatformUserID, ...) when we already
    // have a prefix on Chat.OnNewChatMessage:
    //
    // Vanilla Chat.OnNewChatMessage funnels chat-panel rendering through
    //   Terminal.AddString(PlatformUserID user, string text, Talker.Type type, bool timestamp)
    // which builds the displayed nick from ZNet.TryGetPlayerByPlatformUserID(...).m_name —
    // i.e. from the peer roster, NOT from the `sender.Name` we mutated in ChatPanelPatch.
    // So that prefix is invisible in the chat panel; it only affects the in-world bubble
    // where vanilla calls sender.GetDisplayName().
    //
    // We replace the panel rendering wholesale: format the line ourselves with our clan
    // tag prepended and forward to Terminal.AddString(string) — skipping vanilla.
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString),
                  new[] { typeof(PlatformUserID), typeof(string), typeof(Talker.Type), typeof(bool) })]
    internal static class TerminalAddStringPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Terminal __instance, PlatformUserID user, string text,
                                   Talker.Type type, bool timestamp)
        {
            if (!ModRuntime.RegistryReady) return true;

            // Sentinel marker means /t (step 6) already pre-formatted the entire line.
            // Strip the marker and forward to the plain AddString(string) overload —
            // skip our own tag injection so we don't double-tag.
            if (text != null && text.StartsWith(ClanFormatter.SkipMarker))
            {
                __instance.AddString(text.Substring(ClanFormatter.SkipMarker.Length));
                return false;
            }

            string steamId = user.m_userID;
            if (string.IsNullOrEmpty(steamId)) return true;
            if (!ClanRegistry.Instance.TryGetClan(steamId, out var clan)) return true;

            // Replicate vanilla color/text-casing rules per Talker.Type.
            Color msgColor = Color.white;
            switch (type)
            {
                case Talker.Type.Shout:
                    msgColor = Color.yellow;
                    text = text != null ? text.ToUpper() : string.Empty;
                    break;
                case Talker.Type.Whisper:
                    msgColor = new Color(1f, 1f, 1f, 0.75f);
                    text = text != null ? text.ToLowerInvariant() : string.Empty;
                    break;
            }

            // Resolve the displayed nick the same way vanilla does.
            string displayName;
            if (ZNet.TryGetPlayerByPlatformUserID(user, out var playerInfo))
            {
                displayName = CensorShittyWords.FilterUGC(playerInfo.m_name, UGCType.CharacterName, user, 0L);
            }
            else
            {
                displayName = user.ToString();
            }

            string ts = timestamp
                ? "[" + System.DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss") + "] "
                : string.Empty;
            string tag = ClanFormatter.TagPrefix(clan);
            string colorHex = ColorUtility.ToHtmlStringRGBA(msgColor);

            string finalLine =
                ts + tag + " <color=orange>" + displayName + "</color>: " +
                "<color=#" + colorHex + ">" + (text ?? string.Empty) + "</color>";

            __instance.AddString(finalLine);
            return false;
        }
    }
}
