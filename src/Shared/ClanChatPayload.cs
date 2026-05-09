using System.IO;

namespace RodgerClans.Shared
{
    public sealed class ClanChatPayload
    {
        public const int Schema = 1;

        public string SenderSteamId64;
        public string Message;

        public ZPackage ToPackage()
        {
            var p = new ZPackage();
            p.Write(Schema);
            p.Write(SenderSteamId64 ?? string.Empty);
            p.Write(Message ?? string.Empty);
            return p;
        }

        public static ClanChatPayload Read(ZPackage p)
        {
            int schema = p.ReadInt();
            if (schema != Schema)
                throw new InvalidDataException($"Unknown ClanChatPayload schema {schema}");
            return new ClanChatPayload
            {
                SenderSteamId64 = p.ReadString(),
                Message = p.ReadString(),
            };
        }
    }
}
