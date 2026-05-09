# RodgerClans

Mod dla **Valheim 0.221.12** dodający serwerowo-autorytatywny system klanów: tagi nad nameplate'em i w czacie, prywatny chat klanowy `/t`, brak friendly fire między członkami klanu, globalny zasięg wiadomości Normal i piny sojuszników na minimapie niezależnie od ustawienia "Share Location".

Plugin oparty o **BepInEx 5.4** + **Jotunn 2.x (JVL)** + **Harmony 2**, jeden DLL na serwerze i klientach.

---

## Status implementacji

**v1 funkcjonalnie kompletny.** Wszystkie 7 funkcji zaimplementowane i samo-przetestowane na hosted serwerze (Host & Play). Pełen E2E z drugim peerem pozostaje do wykonania.

| # | Krok migracji | Stan | Self-test | E2E |
|---|---|---|---|---|
| 1 | Bootstrap projektu (`.sln`, `.csproj`, Plugin skeleton) | done | OK | n/a |
| 2 | Modele + ClanRegistry + helpery (Util) | done | OK | n/a |
| 3 | JSON loader + ServerBootstrap | done | OK | n/a |
| 4 | RPC plumbing + pierwszy Harmony patch (`PeerInfoServerPatch`) | done | OK | **pending** |
| 5a | Nameplate tag (Player.GetHoverName) | done | OK (przez własny trup) | **pending** |
| 5b | Chat panel tag (TerminalAddStringPatch + ChatPanelPatch) | done | OK | n/a |
| 5c | Globalny zasięg dla Normal (Talker.RPC_Say) | done | OK (brak regresji) | **pending** |
| 5d | Tag w bąbelku in-world | covered by 5b (sender.Name mutation działa dla bąbelka) | OK (Shout) | **pending** |
| 5e | Piny sojuszników na minimapie (ZNet.GetOtherPublicPlayers) | done | OK (brak crashy) | **pending** |
| 6 | `/t` chat klanowy | done | OK | **pending** |
| 7 | Friendly fire (Character.Damage server prefix) | done | OK (brak regresji self-damage / mob damage) | **pending** |
| 8 | Graceful no-op (server bez moda) | implementuje `ClientBootstrap.cs` z step 4 | n/a | **pending** |

**Pending E2E** wymaga drugiego gracza / drugiego konta Steam — testujemy:
- Sojusznik z wyłączonym "Share Location" → widoczny pin (5e).
- Atak sojusznika → 0 damage; atak gracza spoza klanu → normalny damage (7).
- `/t hej` → wszyscy klanowicze widzą, gracze spoza klanu nie widzą (6).
- Normal chat z odległości >15m → globalnie widoczny w panelu (5c).
- Klient na vanilla serwerze (bez moda) → graceful no-op, log `Server has no RodgerClans — running in passive mode.` (8).

---

## Co mod robi

### Klany
- Klan: `id` (string, internal), `name` (display), `tag` (3 znaki), `colorHex`.
- Każdy gracz w max jednym klanie, identyfikowany po `SteamID64` (czyste 17 cyfr, bez prefixu `Steam_`).
- Konfiguracja w **`BepInEx/config/RodgerClans/clans.json`** na serwerze. Klienci nigdy nie czytają tego pliku — server rozsyła snapshot przez RPC po każdym `RPC_PeerInfo`.
- Hot-reload poza scope v1 (zmiana JSON-a wymaga restartu serwera).

### Wizualnie
1. **Nameplate** nad zdrowiem gracza — tag w kolorze klanu prepend.
2. **Chat panel** — `[TAG] Nick: wiadomość` z tagiem klanowym przed nick-format'em vanilla'i.
3. **Bąbelek shouta in-world** — tag w bąbelku gdy gracz krzyknie.

### Komunikacja
- **`/t <wiadomość>`** — prywatny chat klanowy w jasnoniebieskim (`#9ECEFF`). Routowany przez serwer wyłącznie do online'owych członków klanu.
- **Globalny Normal chat** — vanilla 0.221.12 ucinał Normal po 15m na kliencie (`Talker.RPC_Say` distance check); klient z modem przepuszcza Normal niezależnie od dystansu. Whisper (4m) i Shout (70m) zachowują vanilla. Bąbelek in-world dalej fade'uje przez `Minimap.m_nomapPingDistance`.

### Gameplay
- **Brak friendly fire** między członkami tego samego klanu — server-authoritative prefix na `Character.Damage` zeruje `hit.m_damage.Modify(0f)`. Knockback nietknięty. Self-damage (spadanie, własna bomba) działa normalnie.
- **Piny klanowiczów na minimapie** — sojusznik z wyłączonym "Share Location" jest widoczny dla swojego klanu w czasie rzeczywistym. Nie-klanowicze widzą tylko tych z włączonym Share (vanilla).

---

## Architektura

**Single DLL** (`RodgerClans.dll`) na serwer i klient, z folderowym podziałem na `Server/`, `Client/`, `Shared/`, `Net/`, `Util/`. Każdy patch sprawdza `ZNet.instance` w runtime i bail-out'uje na niewłaściwej stronie.

### Server-authoritative registry
- Server jest **jedynym** źródłem prawdy. Ładuje `clans.json` w `ServerBootstrap.Run()` (ścieżka: coroutine `WaitForZNetThenDispatch` → `ZNet.IsServer()` → bootstrap).
- Po każdym `RPC_PeerInfo` (peer authentykuje się), `PeerInfoServerPatch` wysyła do tego peera `RodgerClans.RegistrySync` z snapshot'em (clans + members + peer roster).
- Klient hydratuje `ClanRegistry.Instance.IsHydrated = true` w handlerze RPC.
- **Cheat-resistant**: klient nie może sfałszować członkostwa, bo registry to read-only mirror. Friendly fire egzekwowane na serwerze.

### Custom RPCs przez Jotunn `NetworkManager`
- `RodgerClans.RegistrySync` — server → one peer, on connect.
- `RodgerClans.ClanChat` — bidirectional, `/t` routing.

### Identyfikacja gracza
- `clans.json` używa SteamID64 jako string 17 cyfr.
- Konwersja `Steam_XXX ↔ SteamID64` przez `Util/PlayerIdHelper.{ToPlatformId, TryParseSteamId}`.
- **Hosted server quirk**: host nie ma własnego peer entry w `ZNet.GetPeers()`. `PlayerIdHelper.GetLocalSteamId64()` ma fallback przez Splatform `PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.m_userID`.
- `PlayerIdHelper.GetSteamIdForPlayer(Player)` — używany w `ClanRegistry.AreAllies` i `NameplatePatch` — automatycznie idzie ścieżką local-self (Splatform) gdy `player == Player.m_localPlayer`.

### Graceful no-op
- Klient na vanilla serwerze: po 5s `ClientBootstrap.WatchForRegistrySync` sprawdza `IsHydrated == false` → log `Server has no RodgerClans — running in passive mode.`. Wszystkie patche mają `if (!ModRuntime.RegistryReady) return;` na początku — patch po prostu nic nie robi.

---

## Build i deploy

### Wymagania
- **.NET Framework 4.8** SDK (przez `dotnet build`).
- Lokalna instalacja Valheim ze ścieżką `C:\Program Files (x86)\Steam\steamapps\common\Valheim` (default w `.csproj`) lub override przez `-p:ValheimGameDir="..."`.
- **BepInEx 5.4** + **Jotunn (JVL) 2.x** zainstalowany w folderze gry (`BepInEx/plugins/Jotunn.dll`).

### Build
```powershell
cd C:\Dev\RodgerClans
dotnet build -c Release
```

Target `DeployToBepInEx` (w `.csproj`, tylko Release) automatycznie kopiuje `bin/Release/RodgerClans.dll` do `<ValheimGameDir>/BepInEx/plugins/RodgerClans/RodgerClans.dll`.

### Inny `ValheimGameDir`
```powershell
dotnet build -c Release -p:ValheimGameDir="D:\Steam\steamapps\common\Valheim"
```

### Pierwsze uruchomienie
1. Zbuduj DLL (deploy automatyczny do plugins).
2. Odpal Valheim → Host & Play. Jeśli `clans.json` brakuje, server stworzy go z embedded template (3 przykładowe klany + 2 członków). Edytuj `BepInEx/config/RodgerClans/clans.json` żeby ustawić własne klany / SteamIDy, restart serwera.

---

## Konfiguracja `clans.json`

Schema v1, walidacja whole-or-nothing (jeden błąd → registry pusty, server dalej działa, log error z konkretną przyczyną):

```json
{
  "version": 1,
  "clans": [
    {
      "id":       "DRW",
      "name":     "DRWale",
      "tag":      "DRW",
      "colorHex": "#3B82F6"
    }
  ],
  "members": [
    { "steamId64": "76561198343378545", "clanId": "DRW", "rank": "jarl" }
  ]
}
```

### Reguły walidacji
- `version == 1` — inne wersje rejected.
- `id`: 2-16 znaków ASCII alfanumeryczne, unikalny w pliku.
- `name`: 1-32 znaków.
- `tag`: **dokładnie 3 znaki**, bez whitespace.
- `colorHex`: parseable przez `ColorUtility.TryParseHtmlString` (`#RRGGBB` lub `#RRGGBBAA`).
- `steamId64`: dokładnie 17 cyfr ASCII, unikalny w pliku.
- `clanId` w members: musi referować istniejący klan.
- `rank`: opcjonalny; `"member"` lub `"jarl"` (lowercase). Inne wartości → reject.

Loader używa **SimpleJson** dostarczanego przez Jotunn (`SimpleJson.SimpleJson.DeserializeObject`). Brak `Newtonsoft.Json` w deployu.

---

## Struktura projektu

```
RodgerClans/
├── RodgerClans.sln
├── RodgerClans.csproj           ← refs do Valheim/BepInEx/Jotunn przez $(ValheimGameDir),
│                                  embedded resource clans.example.json,
│                                  auto-deploy do BepInEx/plugins/RodgerClans/
├── README.md
├── CHANGELOG.md                 ← log per-step (1, 2, 3, 4, 5a, 5b, 5b-fix, 5c, 5e+7, 6)
├── .gitignore
├── config/
│   └── clans.example.json       ← embedded resource (LogicalName: RodgerClans.clans.example.json)
└── src/
    ├── Plugin.cs                                    ← BepInPlugin entry, Awake, dispatch coroutine
    ├── Shared/
    │   ├── Clan.cs                                  ← model: Id/Name/Tag/ColorHex (cached Color)
    │   ├── ClanMember.cs                            ← model: SteamId64/ClanId/Rank
    │   ├── ClanRank.cs                              ← enum Member/Jarl (rank stub, v2)
    │   ├── ClanRegistry.cs                          ← singleton, IsHydrated, lookups, AreAllies, HydrateFromPackage
    │   ├── ClanFormatter.cs                         ← TagPrefix(Clan), SkipMarker (defensive sentinel)
    │   └── ClanChatPayload.cs                       ← ZPackage Read/Write dla /t
    ├── Server/
    │   ├── ClansFileLoader.cs                       ← SimpleJson + walidacja schema v1
    │   ├── ServerBootstrap.cs                       ← Run(): mkdir + (optional copy template) + load + hydrate
    │   ├── RegistryPackager.cs                      ← Build() ZPackage z registry + active peer roster
    │   ├── ClanChatServer.cs                        ← Route(): peer iteration filtrująca po klanie + hosted-self dispatch
    │   └── Patches/
    │       ├── PeerInfoServerPatch.cs               ← postfix ZNet.RPC_PeerInfo (private, atrybut stringiem)
    │       └── FriendlyFirePatch.cs                 ← prefix Character.Damage, hit.m_damage.Modify(0f)
    ├── Client/
    │   ├── ClientBootstrap.cs                       ← 5s watchdog; passive-mode log
    │   ├── ClanChatClient.cs                        ← Send / Display / ResolveName / ResolveServerPeerUid
    │   ├── ClanChatCommand.cs                       ← Jotunn ConsoleCommand "t" (Run → Send)
    │   ├── ClanCommands.cs                          ← Register helper (CommandManager.AddConsoleCommand)
    │   └── Patches/
    │       ├── NameplatePatch.cs                    ← postfix Player.GetHoverName (NIE Character)
    │       ├── ChatPanelPatch.cs                    ← prefix Chat.OnNewChatMessage (mutuje sender.Name dla bąbelka)
    │       ├── TerminalAddStringPatch.cs            ← prefix Terminal.AddString(PlatformUserID,...) (panel rendering)
    │       ├── GlobalNormalChatPatch.cs             ← prefix Talker.RPC_Say (private), zdejmuje 15m filtr dla Normal
    │       └── MinimapAllyPatch.cs                  ← postfix ZNet.GetOtherPublicPlayers, dorzuca klanowiczów
    ├── Net/
    │   └── ClanRpc.cs                               ← Register(), 2 Custom RPCs, handlery
    └── Util/
        ├── ModRuntime.cs                            ← IsServer/IsClient/IsDedicated/RegistryReady
        └── PlayerIdHelper.cs                        ← Steam_↔SteamID64, GetSteamIdForCharacter, GetLocalSteamId64 (Splatform fallback), GetSteamIdForPlayer
```

---

## Hard rules (NIE zmieniaj bez wyraźnego powodu)

Reguły są pochodną problemów z poprzedniej próby (`RodgerClansClient.dll` v0):

1. **Jeden render-path per powierzchnia.** Tag prepend dokładnie raz dla nameplate, raz dla panelu, raz dla bąbelka. Nigdy nie piętrzyć postfixów.
2. **Server-only registry authority.** Klient czyta tylko to, co dostał przez RegistrySync.
3. **Server-side `Character.Damage`.** Friendly fire egzekwowany autorytatywnie, nigdy na kliencie.
4. **`Talker.Type` enum names, nigdy magic numbers.** v0 mylił `Ping=3` z Shout. W naszym kodzie `if (type == Talker.Type.Ping)`.
5. **`Player.GetHoverName` vs `Character.GetHoverName`** — w 0.221.12 Player override'uje Character bez wywołania `base.GetHoverName()`. Patchujemy `Player.GetHoverName` (skill cookbook section 2 był nieaktualny).
6. **`PlatformUserID` ↔ SteamID64**. Konwersja tylko przez `PlayerIdHelper.{ToPlatformId, TryParseSteamId}`. Brak hardkodowanych SteamIDów w C#.
7. **Bail-out na każdy null.** Każdy patch zaczyna się od `if (!ModRuntime.RegistryReady || ...) return;`.
8. **Brak `Newtonsoft.Json`.** Loader używa `SimpleJson` z Jotunna.

---

## Notes & gotchas (z procesu implementacji)

- **`ZRoutedRpc.GetServerPeerID()` nie istnieje w 0.221.12** (był w starszych wersjach Valheim). Zastąpione iteracją `ZNet.GetPeers()` z flagą `ZNetPeer.m_server`. Plus hosted-host bypass: `ClanChatClient.Send` na serverze wywołuje `ClanChatServer.Route` bezpośrednio, bez RPC.
- **`Chat.OnNewChatMessage` w 0.221.12 NIE filtruje panelu po dystansie** — ten filtr żył tylko w `Talker.RPC_Say` (zdejmujemy go tam). Skill cookbook section 5 (rewrite `pos`) był z poprzedniej wersji gry.
- **`Terminal.AddString(PlatformUserID, ...)` używa peer rosteru zamiast `sender.Name` z OnNewChatMessage** — dlatego oryginalny `ChatPanelPatch.Prefix` mutujący `sender.Name` nie wpływa na panel. Naprawione przez osobny `TerminalAddStringPatch` z `return false` i własnym format.
- **Jotunn 2.x `ConsoleCommand`** nie ma już `OnlyAdmin` (był w wcześniejszych Jotunn). Aktualne props: `Name`, `Help`, `IsCheat`, `IsNetwork`, `OnlyServer`, `IsSecret`.
- **Hosted host self-resolution** — `Player.m_localPlayer` istnieje, ale brak peer entry dla siebie. Trzeba używać Splatform fallback (PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.m_userID).
- **`UserInfo` to class** w 0.221.12 (nie struct) — prefix `ref UserInfo sender` w Harmony tworzy nowy klon i podstawia, żeby nie poison'ować referenci downstream.
- **`UserInfo.UserId` to `PlatformUserID` struct** (zawiera `m_userID` jako string SteamID64) — można resolwować klan bezpośrednio bez peer rosteru.
- **`ZNet.GetOtherPublicPlayers` jako hook dla minimapy** zamiast `Minimap.UpdatePlayerPins` — vanilla zarządza pin lifecycle, my tylko poszerzamy listę.
- **Sentinel marker (`__RC__` z ZWS)** — pierwotnie planowany jako bypass dla TerminalAddStringPatch w `/t`. Ostatecznie `Display` używa overload `Chat.AddString(string)` (overload #3) który nie jest patchowany, sentinel nie jest potrzebny i wyciekał na ekran. Usunięty z `Display`. `ClanFormatter.SkipMarker` istnieje i `TerminalAddStringPatch.Prefix` go strip'uje — defensywnie, na wypadek gdyby kiedyś `/t` przeszło inną ścieżką.

---

## Test loop

Mod nie ma headless test runnera — testy w grze:

1. `dotnet build -c Release` (auto-deploy do plugins).
2. **Zamknij Valheima** jeśli był otwarty (BepInEx ładuje DLL przy starcie procesu).
3. Odpal `valheim.exe`. Wystarczy menu główne dla sanity testów.
4. Tail `<ValheimGameDir>/BepInEx/LogOutput.log`:
   ```powershell
   Get-Content "<ValheimGameDir>\BepInEx\LogOutput.log" -Tail 100 -Wait
   ```
5. Szukaj 9 linii startowych modu:
   ```
   [Info   :   BepInEx] Loading [RodgerClans 0.1.0]
   [Info   :RodgerClans] RodgerClans 0.1.0 loaded.
   [Info   :RodgerClans] Registry ready: hydrated=False, clans=0, members=0.
   [Info   :RodgerClans] RPC registered: RodgerClans.RegistrySync
   [Info   :RodgerClans] RPC registered: RodgerClans.ClanChat
   [Info   :RodgerClans] Harmony patches applied under id 'org.rodgerclans'.
   [Info   :RodgerClans] Console command registered: /t
   [Info   :RodgerClans] ZNet ready and IsServer — running ServerBootstrap.
   [Info   :RodgerClans] Loaded N clans, M members from clans.json.
   ```

---

## Tech stack

- **Valheim 0.221.12** (Steam, Windows)
- **BepInEx 5.4.x**
- **Jotunn (JVL) 2.x** — `BepInDependency(Jotunn.Main.ModGuid)`, `NetworkCompatibility(EveryoneMustHaveMod, VersionStrictness.Minor)`
- **Harmony 2** (BepInEx core)
- **.NET Framework 4.8**, C# 9.0
- **SimpleJson** (z Jotunna, namespace `SimpleJson`) — JSON loader
- **Splatform** — local user resolution (`PlatformManager.DistributionPlatform.LocalUser.PlatformUserID`)

---

## Następne kroki

1. **Pełen E2E test z drugim peerem** — opisany w sekcji "Status implementacji" wyżej.
2. **(opcjonalnie) Hot reload `clans.json`** — admin `/clans_reload`. Poza scope v1, zaplanowane na v2.
3. **(opcjonalnie) Per-rank permissions** — pole `Rank` w modelu jest stub'em. v2.
4. **(opcjonalnie) Cross-platform** — obecnie filtr `pid.m_platform.ToString() == "Steam"`. Na deployu Xbox/Microsoft trzeba poszerzyć.

---

## Plik konfiguracyjny w deployu

Po pierwszym uruchomieniu serwera (gdy `clans.json` brakuje), `ServerBootstrap.TryWriteTemplate` zapisuje plik z embedded resource. Edytujesz, restart serwera. Server w logu zarejestruje:
- `clans.json not found at <path> — writing template from embedded resource.` (pierwszy start)
- `Loaded N clans, M members from clans.json.` (każdy start)
- Albo `Failed to load clans.json: <reason>. Registry will be empty.` (przy błędnej walidacji — server dalej działa, registry pusty, wszystkie patche idą w no-op).
