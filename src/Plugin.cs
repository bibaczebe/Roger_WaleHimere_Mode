using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using RodgerClans.Client;
using RodgerClans.Net;
using RodgerClans.Server;
using RodgerClans.Shared;

namespace RodgerClans
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class RodgerClansPlugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "org.rodgerclans";
        public const string PluginName    = "RodgerClans";
        public const string PluginVersion = "0.1.0";

        // Static log source so static helpers (Harmony patches, RPC handlers) can log
        // without needing a Plugin instance reference. Set in Awake before any patch fires.
        public static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            // Touch the registry singleton once so all Shared/* and Util/* types load now,
            // not lazily later on first patch invocation. Surfaces type-load errors here.
            Logger.LogInfo(
                $"Registry ready: hydrated={ClanRegistry.Instance.IsHydrated}, " +
                $"clans={ClanRegistry.Instance.ClanCount}, " +
                $"members={ClanRegistry.Instance.MemberCount}.");

#if DEBUG
            ClanRegistry.Instance.LoadHardcodedTestData();
            Logger.LogInfo(
                $"[DEBUG] Test data loaded: " +
                $"{ClanRegistry.Instance.ClanCount} clans, " +
                $"{ClanRegistry.Instance.MemberCount} members.");
#endif

            // Register RPCs BEFORE PatchAll so PeerInfoServerPatch can rely on
            // ClanRpc.RegistrySync being non-null when the first peer connects.
            ClanRpc.Register(Logger);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"Harmony patches applied under id '{PluginGuid}'.");

            ClanCommands.Register(Logger);

            StartCoroutine(WaitForZNetThenDispatch());
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // ZNet.instance is null at plugin Awake — it appears once the world / dedicated server
        // initialises. Yield until it shows up, then branch by IsServer().
        private IEnumerator WaitForZNetThenDispatch()
        {
            while (ZNet.instance == null) yield return null;

            if (ZNet.instance.IsServer())
            {
                Logger.LogInfo("ZNet ready and IsServer — running ServerBootstrap.");
                ServerBootstrap.Run(Logger);
            }
            else
            {
                Logger.LogInfo("ZNet ready and not server — running ClientBootstrap watchdog.");
                StartCoroutine(ClientBootstrap.WatchForRegistrySync(Logger));
            }
        }
    }
}
