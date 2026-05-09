using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SimpleJson;
using UnityEngine;
using RodgerClans.Shared;

namespace RodgerClans.Server
{
    public sealed class LoadResult
    {
        public bool IsSuccess { get; }
        public IReadOnlyList<Clan> Clans { get; }
        public IReadOnlyList<ClanMember> Members { get; }
        public string Error { get; }

        private LoadResult(bool ok, IReadOnlyList<Clan> clans, IReadOnlyList<ClanMember> members, string error)
        {
            IsSuccess = ok;
            Clans = clans;
            Members = members;
            Error = error;
        }

        public static LoadResult Success(IReadOnlyList<Clan> clans, IReadOnlyList<ClanMember> members) =>
            new LoadResult(true, clans, members, null);

        public static LoadResult Fail(string error) =>
            new LoadResult(false, Array.Empty<Clan>(), Array.Empty<ClanMember>(), error);
    }

    // Whole-or-nothing JSON loader and validator. Any single rule violation rejects the
    // entire file — partial loads could let half the population have wrong friendly-fire
    // / chat behaviour, which is worse than no clans at all.
    public static class ClansFileLoader
    {
        public static LoadResult Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return LoadResult.Fail("path is empty");
            if (!File.Exists(path))         return LoadResult.Fail($"file not found: {path}");

            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) { return LoadResult.Fail($"read error: {e.Message}"); }

            object root;
            try { root = SimpleJson.SimpleJson.DeserializeObject(raw); }
            catch (Exception e) { return LoadResult.Fail($"JSON parse error: {e.Message}"); }

            if (!(root is JsonObject obj))
                return LoadResult.Fail("root is not a JSON object");

            // -------- version --------
            if (!obj.TryGetValue("version", out var versionObj))
                return LoadResult.Fail("missing 'version'");
            int version = ToInt(versionObj);
            if (version != 1)
                return LoadResult.Fail($"unsupported 'version' {version} (expected 1)");

            // -------- clans --------
            if (!obj.TryGetValue("clans", out var clansObj) || !(clansObj is JsonArray clansArr))
                return LoadResult.Fail("missing or invalid 'clans' array");

            var clans   = new List<Clan>(clansArr.Count);
            var clanIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < clansArr.Count; i++)
            {
                if (!(clansArr[i] is JsonObject co))
                    return LoadResult.Fail($"clans[{i}]: not a JSON object");

                string id   = TrimField(co, "id");
                string name = TrimField(co, "name");
                string tag  = TrimField(co, "tag");
                string hex  = TrimField(co, "colorHex");

                if (id.Length == 0)   return LoadResult.Fail($"clans[{i}]: 'id' is empty");
                if (name.Length == 0) return LoadResult.Fail($"clans[{i}]: 'name' is empty");
                if (tag.Length == 0)  return LoadResult.Fail($"clans[{i}]: 'tag' is empty");
                if (hex.Length == 0)  return LoadResult.Fail($"clans[{i}]: 'colorHex' is empty");

                if (id.Length < 2 || id.Length > 16)
                    return LoadResult.Fail($"clans[{i}].id '{id}': must be 2-16 chars");
                if (!IsAsciiAlnum(id))
                    return LoadResult.Fail($"clans[{i}].id '{id}': must be ASCII letters/digits only");
                if (!clanIds.Add(id))
                    return LoadResult.Fail($"clans[{i}].id '{id}': duplicate clan id");

                if (name.Length > 32)
                    return LoadResult.Fail($"clans[{i}].name: must be 1-32 chars");

                if (tag.Length != 3)
                    return LoadResult.Fail($"clans[{i}].tag '{tag}': must be EXACTLY 3 characters");
                foreach (var c in tag)
                    if (char.IsWhiteSpace(c))
                        return LoadResult.Fail($"clans[{i}].tag '{tag}': whitespace not allowed");

                if (!ColorUtility.TryParseHtmlString(hex, out _))
                    return LoadResult.Fail($"clans[{i}].colorHex '{hex}': cannot be parsed as HTML color");

                clans.Add(new Clan(id, name, tag, hex));
            }

            // -------- members --------
            if (!obj.TryGetValue("members", out var membersObj) || !(membersObj is JsonArray membersArr))
                return LoadResult.Fail("missing or invalid 'members' array");

            var members      = new List<ClanMember>(membersArr.Count);
            var seenSteamIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < membersArr.Count; i++)
            {
                if (!(membersArr[i] is JsonObject mo))
                    return LoadResult.Fail($"members[{i}]: not a JSON object");

                string steamId = TrimField(mo, "steamId64");
                string clanId  = TrimField(mo, "clanId");
                string rankStr = mo.TryGetValue("rank", out var rankRaw) && rankRaw != null
                    ? rankRaw.ToString().Trim()
                    : "";

                if (steamId.Length == 0)
                    return LoadResult.Fail($"members[{i}]: 'steamId64' is empty");
                if (steamId.Length != 17 || !IsAllDigits(steamId))
                    return LoadResult.Fail($"members[{i}].steamId64 '{steamId}': must be exactly 17 ASCII digits");
                if (!seenSteamIds.Add(steamId))
                    return LoadResult.Fail($"members[{i}].steamId64 '{steamId}': appears more than once");

                if (clanId.Length == 0)
                    return LoadResult.Fail($"members[{i}]: 'clanId' is empty");
                if (!clanIds.Contains(clanId))
                    return LoadResult.Fail($"members[{i}].clanId '{clanId}': does not match any clan");

                ClanRank rank = ClanRank.Member;
                if (rankStr.Length > 0)
                {
                    switch (rankStr)
                    {
                        case "member": rank = ClanRank.Member; break;
                        case "jarl":   rank = ClanRank.Jarl;   break;
                        default:
                            return LoadResult.Fail($"members[{i}].rank '{rankStr}': must be 'member' or 'jarl'");
                    }
                }

                members.Add(new ClanMember(steamId, clanId, rank));
            }

            return LoadResult.Success(clans, members);
        }

        // -------- helpers --------

        private static string TrimField(JsonObject obj, string key)
        {
            if (!obj.TryGetValue(key, out var v) || v == null) return "";
            return v.ToString().Trim();
        }

        private static int ToInt(object v)
        {
            if (v == null) return 0;
            if (v is long l)   return (int)l;
            if (v is int i)    return i;
            if (v is double d) return (int)d;
            return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static bool IsAsciiAlnum(string s)
        {
            foreach (var c in s)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                    return false;
            return true;
        }

        private static bool IsAllDigits(string s)
        {
            foreach (var c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
