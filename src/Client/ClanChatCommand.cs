using Jotunn.Entities;

namespace RodgerClans.Client
{
    public class ClanChatCommand : ConsoleCommand
    {
        public override string Name        => "t";
        public override string Help        => "/t <wiadomość> — wyślij wiadomość tylko do swojego klanu.";
        public override bool   IsCheat     => false;
        public override bool   IsNetwork   => false;
        public override bool   OnlyServer  => false;

        public override void Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.instance?.Print("Użycie: /t <wiadomość>");
                return;
            }
            ClanChatClient.Send(string.Join(" ", args));
        }
    }
}
