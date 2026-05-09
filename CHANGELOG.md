# Changelog

## v0.1.0-step6 — /t channel (chat klanowy)

- `src\Net\ClanRpc.cs` — wypełnione placeholdery z step 4. `OnClanChatServer` parsuje payload i wywołuje `ClanChatServer.Route`. `OnClanChatClient` parsuje i wywołuje `ClanChatClient.Display`. Try/catch z log error w razie zepsutej paczki.
- `src\Server\ClanChatServer.cs` — `Route(senderUid, payload)`. Resolve sender clan → drop jeśli sender nie w klanie. Iteruj `ZNet.GetPeers()`, dla każdego peera w tym samym klanie `SendPackage(peer.m_uid, ...)`. Plus dispatch hosted-self: jeśli `Player.m_localPlayer != null` i host w tym samym klanie → wywołaj `ClanChatClient.Display` lokalnie (host nie ma peer entry dla siebie). Log Info z liczbą odbiorców.
- `src\Client\ClanChatCommand.cs` — Jotunn `ConsoleCommand` z `Name = "t"`. `Run(args)` woła `ClanChatClient.Send(string.Join(" ", args))`. Pusty arg → print użycia.
- `src\Client\ClanChatClient.cs` — `Send(message)` i `Display(payload)`. Display używa `Chat.instance.AddString(string)` overload #3 (bare string) — bypass `TerminalAddStringPatch` żeby zachować jasnoniebieski kolor #9ECEFF zamiast vanilla format. Sentinel marker zachowany defensywnie. `ResolveName` ma 4 ścieżki: local player, peer roster, ZNet.GetPlayerList fallback (dla hosted hosta gdzie roster jest pusty), fallback SteamID.
- `src\Client\ClanCommands.cs` — wrapper dla `CommandManager.Instance.AddConsoleCommand`.
- `src\Plugin.cs` — wywołanie `ClanCommands.Register(Logger)` po `Harmony.PatchAll()`.

## v0.1.0-step5e+7 — Minimap ally pins + friendly fire (server)

- **5e**: `src\Client\Patches\MinimapAllyPatch.cs` — `[HarmonyPostfix]` na `ZNet.GetOtherPublicPlayers`. Lepsza ścieżka niż skill cookbook section 6 (Minimap.UpdatePlayerPins z dictionary'em wstrzykniętych pinów): vanilla Minimap pyta ZNet o listę graczy do narysowania, my ją tylko poszerzamy o klanowiczów z `m_publicPosition == false`. Vanilla zarządza lifecycle pinów, stylem i czyszczeniem — my tylko dodajemy wpisy. Klan resolwowany przez nowe pole `ZNet.PlayerInfo.m_userInfo` (struct `CrossNetworkUserInfo` zawierający `PlatformUserID m_id`).
- **7**: `src\Server\Patches\FriendlyFirePatch.cs` — `[HarmonyPrefix]` na `Character.Damage`. Sygnatura w 0.221.12: `public void Damage(HitData hit)` (nie virtual; brak Player/Humanoid override). Zerowanie wszystkich pól damage przez `hit.m_damage.Modify(0f)`. Knockback nietknięty per spec.
- `src\Util\PlayerIdHelper.cs` — nowy helper `GetSteamIdForPlayer(Player)` z lokalnym Splatform fallback'iem (analogicznie do NameplatePatch z 5a). Eliminuje bug: na hosted serverze host atakujący sojusznika nie był rezolwowany (brak peer entry dla samego siebie), więc AreAllies zwracał false i damage przechodził. Teraz spójnie używamy fallback'a wszędzie gdzie resolwujemy SteamID dla `Player`.
- `src\Shared\ClanRegistry.cs` — `AreAllies` używa `PlayerIdHelper.GetSteamIdForPlayer` zamiast `GetSteamIdForCharacter`. Naprawia hosted-self bug.

## v0.1.0-step5c — Globalny zasięg dla Normal chat

- Odkrycie sygnatury `Talker.RPC_Say(long sender, int ctype, UserInfo user, string text)` w 0.221.12. Distance-check jest WYŁĄCZNIE po stronie odbiorcy, w `RPC_Say`: `Vector3.Distance(transform.position, Player.m_localPlayer.transform.position) < num` gdzie `num` to `m_visperDistance/m_normalDistance/m_shoutDistance` (4/15/70). Wysyłka (`Talker.Say` → ZRoutedRpc) jest globalna do każdego peera.
- Skill `patch-cookbook.md` sekcja 5 proponowała Prefix na `Chat.OnNewChatMessage` rewrite'ujący `pos`. To rozwiązuje problem dla starszych Valheimów, gdzie filtr był w `OnNewChatMessage`. W 0.221.12 panel czatu w `OnNewChatMessage` już nie filtruje — filtr migrował do `Talker.RPC_Say`. Patchujemy tam.
- `src\Client\Patches\GlobalNormalChatPatch.cs` — `[HarmonyPrefix]` na `Talker.RPC_Say` (private — atrybut stringiem). Dla `ctype == Talker.Type.Normal` (1) forwardujemy do `Chat.instance.OnNewChatMessage(...)` z `headPoint` z `__instance.GetComponent<Character>().GetHeadPoint()` i wracamy `false` (skip vanilla). Whisper (0) i Shout (2) zachowują vanilla. Bąbelek in-world ścina się dalej osobnym dystans-checkiem wewnątrz `OnNewChatMessage` (przez `Minimap.m_nomapPingDistance`) — globalny robi się TYLKO panel czatu, zgodnie ze specyfikacją.
- Patch nie używa `ClanRegistry`/`ModRuntime` — działa też gdy registry nie jest hydrated (czyli np. klient gra na serwerze bez moda — globalny Normal działa, brak tagów). Świadoma decyzja: skill sekcja "What the mod does" wprost mówi że globalny Normal to wbudowane zachowanie po stronie klienta.

## v0.1.0-step5b-fix — TerminalAddStringPatch (chat panel surface)

- Odkrycie: vanilla `Chat.OnNewChatMessage` rendering w panelu chat idzie przez `Terminal.AddString(PlatformUserID, string, Talker.Type, bool)`, które buduje display nick z `ZNet.TryGetPlayerByPlatformUserID(...).m_name` (peer roster), NIE z `sender.Name`. Czyli `ChatPanelPatch` mutujący `sender.Name` nie wpływał na panel — tylko na in-world bąbelek (przez `sender.GetDisplayName()`).
- `src\Client\Patches\TerminalAddStringPatch.cs` — `[HarmonyPrefix]` na `Terminal.AddString(PlatformUserID, string, Talker.Type, bool)` z `return false` (skip vanilla). Sami formatujemy linię: timestamp + tag + `<color=orange>nick</color>: <color=#RRGGBB>text</color>`, używając `Terminal.AddString(string)` overload bezpośrednio. Replikujemy vanilla'owe casing/color rules dla Shout (yellow + UPPER) i Whisper (alpha 0.75 + lower). Display name resolvowany przez `ZNet.instance.TryGetPlayerByPlatformUserID` + `CensorShittyWords.FilterUGC`. Klan resolvowany przez `user.m_userID` (ten sam SteamID co w ChatPanelPatch).
- Sentinel marker `ClanFormatter.SkipMarker` przeniesiony tutaj z `ChatPanelPatch` — sentinel /t sieci się przez `Terminal.AddString` text, nigdy nie dochodzi do `OnNewChatMessage`. Wykrycie sentinelu → strip + `__instance.AddString(string)` plain + `return false`.
- `src\Client\Patches\ChatPanelPatch.cs` — usunięto sentinel-block (przeniesiony). Patch zachowany jako Prefix mutujący `sender.Name` dla in-world bąbelka. Komentarz nagłówka zaktualizowany żeby jasno wskazać scope.

## v0.1.0-step5b — ChatPanelPatch

- `src\Client\Patches\ChatPanelPatch.cs` — `[HarmonyPrefix]` na `Chat.OnNewChatMessage`. Sygnatura w 0.221.12: `(GameObject go, long senderID, Vector3 pos, Talker.Type type, UserInfo sender, string text)`. Harmony param names muszą się zgadzać (`sender`, NIE `user` jak w skill cookbook 3b — istotny błąd skilla).
- Resolucja klanu **bezpośrednio przez `sender.UserId.m_userID`** (PlatformUserID struct), nie przez peer roster i nazwę gracza. UserInfo to class, klonujemy żeby nie poison'ować referenci dla innych listenerów.
- Bail-out na `Talker.Type.Ping` (vanilla i tak nie wrzuca Ping do chat panelu) i na sentinel marker `ClanFormatter.SkipMarker` (gdy /t-formatowana wiadomość już ma tag w środku).

## v0.1.0-step5a — NameplatePatch + local-self SteamID resolution

- `src\Util\PlayerIdHelper.cs` — `GetLocalSteamId64()` z dwustopniowym resolverem: peer iteration (vanilla path) + Splatform fallback (`PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.m_userID`) dla hosted servera, gdzie host nie ma własnego peer entry. Filtr na `m_platform.ToString() == "Steam"` (Platform to struct enum-like, `.ToString()` jest wymagane). `using Splatform;`.
- `src\Client\Patches\NameplatePatch.cs` — pierwszy visual patch. `[HarmonyPostfix]` na `Player.GetHoverName` (NIE `Character.GetHoverName` jak w skill cookbook section 2 — w 0.221.12 Player.GetHoverName jest override bez base call, patch na bazie nie odpalłby się dla graczy). Tag prepend dla local i remote graczy; local idzie przez `GetLocalSteamId64`, remote przez `GetSteamIdForCharacter`.

## v0.1.0-step4 — RPC plumbing + pierwszy Harmony patch

- `src\Net\ClanRpc.cs` — rejestracja dwóch RPC przez `Jotunn.Managers.NetworkManager.AddRPC`: `RodgerClans.RegistrySync` (server → client, hydrate registry) i `RodgerClans.ClanChat` (placeholder dla step 6, handlery serwer/klient zostawione jako no-op `yield return null`).
- `src\Server\RegistryPackager.cs` — serializacja `ClanRegistry` + aktywnego peer roster do `ZPackage`. Schema 1: `(int schema, int clanCount, [id,name,tag,colorHex] × N, int memberCount, [steamId64,clanId,rank] × M, int peerCount, [playerName,steamId64] × K)`.
- `src\Server\Patches\PeerInfoServerPatch.cs` — pierwszy Harmony patch w projekcie. `[HarmonyPostfix]` na `ZNet.RPC_PeerInfo`. Sygnatura w Valheim 0.221.12: `private void RPC_PeerInfo(ZRpc rpc, ZPackage pkg)` — postfix bierze tylko `ZRpc rpc` (`pkg` pominięte, Harmony pozwala). `ZNet.GetPeer(ZRpc)` jest private → iteracja `ZNet.GetPeers()` z porównaniem `peer.m_rpc == rpc`. Wysyła `RegistryPackager.Build()` przez `ClanRpc.RegistrySync.SendPackage(peer.m_uid, ...)`.
- `src\Client\ClientBootstrap.cs` — watchdog 5-sekundowy. Jeśli `ClanRegistry.IsHydrated == false` po timeout → log `Server has no RodgerClans — running in passive mode.` (graceful no-op zgodnie z architekturą).
- `src\Shared\ClanRegistry.cs` — nowa metoda `HydrateFromPackage(ZPackage)`: schema check + odczyt clans/members/peers w identycznej kolejności co writer w `RegistryPackager`, atomic `Hydrate(...)` na końcu.
- `src\Plugin.cs` — `public static ManualLogSource Log` (dla statycznych helperów); `_harmony = new Harmony(PluginGuid); _harmony.PatchAll()` (pierwszy raz!); `ClanRpc.Register(Logger)` przed `PatchAll`; coroutine dispatch'uje `ServerBootstrap` lub `ClientBootstrap` w zależności od `IsServer()`; `OnDestroy` robi `UnpatchSelf`.

## v0.1.0-step3 — JSON loader + ServerBootstrap

- `src\Server\ClansFileLoader.cs` — whole-or-nothing parser i walidator dla `clans.json`. Używa `SimpleJson` shipowanego z Jotunnem (namespace `SimpleJson`, `SimpleJson.SimpleJson.DeserializeObject`). Walidacja zgodna z `references/clans-config-schema.md` "Validation rules" (version=1, unique clan ids, tag dokładnie 3 znaki bez whitespace, colorHex parseable, steamId64 17 cyfr, unikalne, clanId musi referować istniejący klan, opcjonalny rank ∈ {member, jarl}).
- `src\Server\ServerBootstrap.cs` — orchestracja: tworzy `BepInEx/config/RodgerClans/`, jeśli `clans.json` brak — zapisuje template z embedded resource, ładuje przez `ClansFileLoader`, hydrate'uje `ClanRegistry` (z pustym peer rosterem; roster wleci w kroku 4).
- `RodgerClans.csproj` — `<EmbeddedResource Include="config\clans.example.json">` z `<LogicalName>RodgerClans.clans.example.json</LogicalName>` — template wbudowany w DLL.
- `Plugin.cs` — `StartCoroutine(WaitForZNetThenBootstrapServer())`. Coroutine czeka na `ZNet.instance != null`, sprawdza `IsServer()`, wywołuje `ServerBootstrap.Run`. Wybór coroutine vs Harmony patch na `ZNet.Awake`: bez nowych Harmony patchów w step 3 (pierwszy będzie `ZNet.RPC_PeerInfo` w step 4), brak konieczności weryfikacji nowej sygnatury w ILSpy w tej iteracji.

## v0.1.0-step2 — modele + registry skeleton

- `src\Shared\Clan.cs`, `ClanMember.cs`, `ClanRank.cs` — modele.
- `src\Shared\ClanFormatter.cs` — `TagPrefix(Clan)` (Unity rich-text color), `SkipMarker = "​__RC__​"` jako string sentinel dla /t.
- `src\Shared\ClanChatPayload.cs` — `Schema=1` + `ToPackage()/Read(ZPackage)` używane przez kroki 4-6.
- `src\Shared\ClanRegistry.cs` — singleton z `IsHydrated`, peer rosterem, lookupami `TryGetClan(steamId)`, `TryGetClanForPlayer(Player)`, `TryGetClanForPlayerName(name)`, `GetPlayerNameForSteamId`, `GetAllClans/Members/PeerRoster`, `AreAllies(Player, Player)`. Zgodnie z `patch-cookbook.md`.
- `src\Util\PlayerIdHelper.cs` — `ToPlatformId/TryParseSteamId` + `GetSteamIdForCharacter(Character)` (peer iteration) + `GetLocalSteamId64()`.
- `src\Util\ModRuntime.cs` — `IsServer/IsClient/IsDedicated/RegistryReady`.
- `Plugin.cs` Awake loguje `Registry ready: hydrated=False, clans=0, members=0` w Release (touch — wymusza JIT typów). W Debug dodatkowo `LoadHardcodedTestData()` i log `Test data loaded: 3 clans, 2 members`.
- Bez Harmony/PatchAll, bez RPC, bez JSON. Wszystko to wleci w krokach 3-7.

## v0.1.0-step1 — bootstrap projektu

- Utworzona solucja `RodgerClans.sln` + `RodgerClans.csproj` (net48, SDK-style).
- Referencje do Valheim/BepInEx/Jotunn rozwiązywane przez `$(ValheimGameDir)` z domyślną ścieżką do folderu gry użytkownika.
- Target `DeployToBepInEx` automatycznie kopiuje DLL do `BepInEx/plugins/RodgerClans/` po Release build.
- `Plugin.cs`: szkielet `RodgerClansPlugin : BaseUnityPlugin` z atrybutami BepInEx + Jotunn `NetworkCompatibility`. Awake loguje wersję — bez Harmony, bez PatchAll.
- `config/clans.example.json`: szablon konfiguracji klanów (3 klany, 2 członków). Plik docelowy `BepInEx/config/RodgerClans/clans.json` zostanie utworzony przez ServerBootstrap w kroku 3.
