using UnityEngine;

namespace RodgerClans.Shared
{
    public static class ClanFormatter
    {
        // Sentinel string prepended to /t-formatted lines so ChatPanelPatch.Prefix can detect
        // and skip its own tag-adding logic, avoiding double tags. Chosen to be very unlikely
        // in regular user input: zero-width spaces around a literal token.
        public const string SkipMarker = "​__RC__​";

        public static string TagPrefix(Clan clan)
        {
            if (clan == null) return string.Empty;
            string hex = ColorUtility.ToHtmlStringRGB(clan.Color);
            return $"<color=#{hex}>[{clan.Tag}]</color>";
        }
    }
}
