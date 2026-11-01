using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Logging;

namespace DOL.GS.PacketHandler
{
    /// <summary>
    /// Song icons on the original client's skill list and quickbar. The client (1.127) reads a song's icon there as a
    /// spells.csv spell ID and shows that spell's icon, while the effects bar shows Spell.Icon correctly; so the songs of
    /// minstrels and bards showed other spells' pictures. The server therefore sends a song's Spell.Icon translated through
    /// the client's spells.csv (spell ID -> icons.csv ID, 5875 unique pairs from gamedata.mpk; IDs 1011 and 2221, listed
    /// twice in the client table, are left out). Unknown IDs are sent unchanged. Taken over from the owner's
    /// "Song-Icons v2" hotfix (2026-10-28) that patched GameServer.dll directly.
    /// The new client translates spell icons itself, so it needs the setting song_icon_client_map off.
    /// </summary>
    public static class SongIconClientMap
    {
        private const string ResourceSuffix = "song_icon_client_map.json";
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Lazy<IReadOnlyDictionary<int, ushort>> Map = new(Load, true);

        public static int Count => Map.Value.Count;

        /// <summary>The icon to send for a song: its Spell.Icon translated through the client table, else unchanged.</summary>
        public static ushort IconForSong(ushort spellIcon) =>
            Map.Value.TryGetValue(spellIcon, out ushort mapped) ? mapped : spellIcon;

        private static IReadOnlyDictionary<int, ushort> Load()
        {
            try
            {
                Assembly assembly = typeof(SongIconClientMap).Assembly;
                string resource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
                if (resource == null)
                {
                    if (Log.IsErrorEnabled)
                        Log.Error($"Song icon map resource '{ResourceSuffix}' is missing; song icons are sent unchanged.");
                    return new Dictionary<int, ushort>();
                }

                using Stream stream = assembly.GetManifestResourceStream(resource);
                Dictionary<string, int> raw = JsonSerializer.Deserialize<Dictionary<string, int>>(stream);
                var map = new Dictionary<int, ushort>(raw?.Count ?? 0);
                foreach (KeyValuePair<string, int> pair in raw ?? new Dictionary<string, int>())
                {
                    if (int.TryParse(pair.Key, out int spellId) && spellId is >= 0 and <= ushort.MaxValue &&
                        pair.Value is >= 0 and <= ushort.MaxValue)
                        map[spellId] = (ushort)pair.Value;
                }
                return map;
            }
            catch (Exception e)
            {
                if (Log.IsErrorEnabled)
                    Log.Error("Song icon map could not be read; song icons are sent unchanged.", e);
                return new Dictionary<int, ushort>();
            }
        }
    }
}
