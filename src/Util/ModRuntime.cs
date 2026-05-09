using RodgerClans.Shared;

namespace RodgerClans.Util
{
    public static class ModRuntime
    {
        public static bool IsServer    => ZNet.instance != null && ZNet.instance.IsServer();
        public static bool IsClient    => ZNet.instance != null && !ZNet.instance.IsServer();
        public static bool IsDedicated => ZNet.instance != null && ZNet.instance.IsDedicated();
        public static bool RegistryReady => ClanRegistry.Instance != null && ClanRegistry.Instance.IsHydrated;
    }
}
