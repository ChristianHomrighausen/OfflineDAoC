using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Personal level caps for autonomous gamebots, kept in bot-levelcaps.json in the server directory
    /// ({"Version":1,"Caps":{"&lt;BotId&gt;":&lt;level 1-50&gt;}}; BotId is offline_world_bots.BotId = GameBot.DatabaseID).
    /// A capped bot gains no experience at or above its cap. The file is read once at server start and written
    /// whenever /botlevelcap changes a cap. If the file cannot be read, every gamebot's experience is paused
    /// (the saved caps are never ignored or overwritten) until the file is fixed and the server restarted.
    /// </summary>
    public static class BotLevelCapStore
    {
        public const string FileName = "bot-levelcaps.json";
        public const int MinCap = 1;
        public const int MaxCap = 50;

        private static readonly Logger Log = LoggerManager.Create(typeof(BotLevelCapStore));
        private static readonly object Sync = new();
        private static Dictionary<long, byte> _caps = new();
        private static string _path;
        private static bool _loadFailed;

        /// <summary>True when the file could not be read: no gamebot gains experience until a restart with a good file.</summary>
        public static bool ExperiencePaused
        {
            get { lock (Sync) return _loadFailed; }
        }

        /// <summary>Reads the caps once at server start. A missing file means no caps.</summary>
        public static void Initialize(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("The server root directory is required.", nameof(rootDirectory));

            string path = Path.Combine(rootDirectory, FileName);
            lock (Sync)
            {
                _path = path;
                _caps = new Dictionary<long, byte>();
                _loadFailed = false;
                try
                {
                    if (File.Exists(path))
                        _caps = Parse(File.ReadAllText(path), path);
                    if (Log.IsInfoEnabled)
                        Log.Info($"[BotLevelCap] {_caps.Count} bot level caps loaded from {path}");
                }
                catch (Exception e)
                {
                    _loadFailed = true;
                    if (Log.IsErrorEnabled)
                        Log.Error("[BotLevelCap] Cannot read bot-levelcaps.json. Bot XP is paused to protect saved caps. " +
                                  "Fix the file and restart the server. " + e.Message);
                }
            }
        }

        public static bool TryGetCap(long botId, out byte cap)
        {
            lock (Sync)
                return _caps.TryGetValue(botId, out cap);
        }

        /// <summary>
        /// The highest level a gamebot may reach by experience: its cap (never above maxLevel), or maxLevel.
        /// While the file is unreadable it is the bot's current level, so it gains nothing.
        /// </summary>
        public static byte LevelLimitFor(long botId, byte currentLevel, byte maxLevel)
        {
            lock (Sync)
            {
                if (_loadFailed)
                    return currentLevel;
                return _caps.TryGetValue(botId, out byte cap) ? Math.Min(cap, maxLevel) : maxLevel;
            }
        }

        /// <summary>Sets (cap 1-50) or removes (cap null) a bot's cap and saves the file at once.</summary>
        public static void SetCap(long botId, byte? cap)
        {
            if (botId <= 0)
                throw new ArgumentOutOfRangeException(nameof(botId), "A persistent bot ID must be positive.");
            if (cap is < MinCap or > MaxCap)
                throw new ArgumentOutOfRangeException(nameof(cap), "A bot level cap must be between 1 and 50.");

            lock (Sync)
            {
                if (_loadFailed)
                    throw new InvalidOperationException("Levelcap-Datei konnte nicht geladen werden. Datei korrigieren und Server neu starten.");
                if (_path == null)
                    throw new InvalidOperationException("The bot level cap store is not initialized.");

                var next = new Dictionary<long, byte>(_caps);
                if (cap.HasValue)
                    next[botId] = cap.Value;
                else
                    next.Remove(botId);

                Save(_path, next);
                _caps = next;
            }
        }

        public static Dictionary<long, byte> Parse(string json, string sourceName = FileName)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("the root must be an object");

                int? version = null;
                Dictionary<long, byte> caps = null;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "Version":
                            if (version.HasValue || property.Value.ValueKind != JsonValueKind.Number ||
                                !property.Value.TryGetInt32(out int value) || value != 1)
                                throw new InvalidDataException("Version must occur once and equal 1");
                            version = value;
                            break;
                        case "Caps":
                            if (caps != null || property.Value.ValueKind != JsonValueKind.Object)
                                throw new InvalidDataException("Caps must occur once and be an object");
                            caps = ParseCaps(property.Value);
                            break;
                        default:
                            throw new InvalidDataException($"unknown property '{property.Name}'");
                    }
                }

                if (version == null || caps == null)
                    throw new InvalidDataException("both Version and Caps are required");
                return caps;
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"Invalid JSON in bot level cap file '{sourceName}': {e.Message}. " +
                                               "The saved caps were not ignored or replaced.", e);
            }
        }

        private static Dictionary<long, byte> ParseCaps(JsonElement element)
        {
            var caps = new Dictionary<long, byte>();
            foreach (JsonProperty entry in element.EnumerateObject())
            {
                if (!long.TryParse(entry.Name, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long botId) || botId <= 0)
                    throw new InvalidDataException($"'{entry.Name}' is not a positive bot ID");
                if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out int level) ||
                    level < MinCap || level > MaxCap)
                    throw new InvalidDataException($"the cap of bot {botId} must be a whole number from 1 to 50");
                if (!caps.TryAdd(botId, (byte)level))
                    throw new InvalidDataException($"bot {botId} is listed twice");
            }
            return caps;
        }

        private static void Save(string path, IReadOnlyDictionary<long, byte> caps)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Version", 1);
                    writer.WriteStartObject("Caps");
                    foreach (KeyValuePair<long, byte> pair in caps.OrderBy(pair => pair.Key))
                        writer.WriteNumber(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair.Value);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.Flush();
                    file.Flush(true);
                }
                File.Move(temporary, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
