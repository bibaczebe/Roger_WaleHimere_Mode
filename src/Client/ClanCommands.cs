using BepInEx.Logging;
using Jotunn.Managers;

namespace RodgerClans.Client
{
    public static class ClanCommands
    {
        public static void Register(ManualLogSource log)
        {
            CommandManager.Instance.AddConsoleCommand(new ClanChatCommand());
            log.LogInfo("Console command registered: /t");
        }
    }
}
