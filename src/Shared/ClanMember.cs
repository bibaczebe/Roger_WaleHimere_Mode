namespace RodgerClans.Shared
{
    public sealed class ClanMember
    {
        public string SteamId64 { get; }
        public string ClanId { get; }
        public ClanRank Rank { get; }

        public ClanMember(string steamId64, string clanId, ClanRank rank)
        {
            SteamId64 = steamId64;
            ClanId = clanId;
            Rank = rank;
        }
    }
}
