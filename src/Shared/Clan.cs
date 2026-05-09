using UnityEngine;

namespace RodgerClans.Shared
{
    public sealed class Clan
    {
        public string Id { get; }
        public string Name { get; }
        public string Tag { get; }
        public string ColorHex { get; }

        private Color? _cachedColor;
        public Color Color
        {
            get
            {
                if (!_cachedColor.HasValue)
                {
                    _cachedColor = ColorUtility.TryParseHtmlString(ColorHex, out var parsed)
                        ? parsed
                        : UnityEngine.Color.white;
                }
                return _cachedColor.Value;
            }
        }

        public Clan(string id, string name, string tag, string colorHex)
        {
            Id = id;
            Name = name;
            Tag = tag;
            ColorHex = colorHex;
        }
    }
}
