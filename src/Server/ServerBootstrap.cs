using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using RodgerClans.Shared;
using RodgerClans.Util;

namespace RodgerClans.Server
{
    // Runs once on the server side after ZNet is ready. Owns the lifecycle of
    // BepInEx/config/RodgerClans/clans.json: creates it from the embedded template if missing,
    // loads it via ClansFileLoader, and hydrates ClanRegistry on success.
    //
    // Hot reload is intentionally out of scope for v1 — to pick up edits, restart the server.
    public static class ServerBootstrap
    {
        private const string ConfigSubdir     = "RodgerClans";
        private const string ConfigFileName   = "clans.json";
        // LogicalName declared in RodgerClans.csproj's <EmbeddedResource>.
        private const string TemplateResource = "RodgerClans.clans.example.json";

        public static string ConfigDir  => Path.Combine(Paths.ConfigPath, ConfigSubdir);
        public static string ConfigFile => Path.Combine(ConfigDir, ConfigFileName);

        public static void Run(ManualLogSource log)
        {
            if (!ModRuntime.IsServer)
            {
                log.LogInfo("ServerBootstrap.Run called but not server — aborting.");
                return;
            }

            try { Directory.CreateDirectory(ConfigDir); }
            catch (Exception e)
            {
                log.LogError($"Could not create config dir '{ConfigDir}': {e.Message}. Registry will be empty.");
                return;
            }

            if (!File.Exists(ConfigFile))
            {
                log.LogWarning($"clans.json not found at {ConfigFile} — writing template from embedded resource.");
                if (!TryWriteTemplate(log))
                {
                    log.LogError("Could not write template clans.json. Registry will be empty.");
                    return;
                }
                log.LogInfo($"Template written to {ConfigFile}. Edit and restart server to populate clans.");
            }

            var result = ClansFileLoader.Load(ConfigFile);
            if (!result.IsSuccess)
            {
                log.LogError($"Failed to load clans.json: {result.Error}. Registry will be empty.");
                return;
            }

            // Step 3 hydrates with an empty peer roster. The roster is populated in step 4
            // when the RPC sync ships it together with the clan/member tables.
            ClanRegistry.Instance.Hydrate(
                result.Clans,
                result.Members,
                Array.Empty<KeyValuePair<string, string>>());

            log.LogInfo($"Loaded {result.Clans.Count} clans, {result.Members.Count} members from clans.json.");
        }

        private static bool TryWriteTemplate(ManualLogSource log)
        {
            try
            {
                using (var stream = typeof(ServerBootstrap).Assembly.GetManifestResourceStream(TemplateResource))
                {
                    if (stream == null)
                    {
                        log.LogError($"Embedded template resource '{TemplateResource}' not found in assembly.");
                        return false;
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        File.WriteAllText(ConfigFile, reader.ReadToEnd());
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                log.LogError($"Writing template failed: {e.Message}");
                return false;
            }
        }
    }
}
