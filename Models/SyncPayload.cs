using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NihongoVocab.Models
{
    public class FlexibleBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True) return true;
            if (reader.TokenType == JsonTokenType.False) return false;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32() != 0;
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (bool.TryParse(str, out var b)) return b;
                if (str == "1") return true;
                if (str == "0") return false;
            }
            return false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }

    public class SyncPayload
    {
        public List<WordList> WordLists { get; set; } = new();
        public List<Word> Words { get; set; } = new();
        public List<ReviewLog> ReviewLogs { get; set; } = new();
        public List<SyncTombstone> Tombstones { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DeviceName { get; set; } = Environment.MachineName;
        public string? SyncEpoch { get; set; }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SyncedLists { get; set; }
        public int SyncedWords { get; set; }
        public int SyncedLogs { get; set; }
        public List<SyncTombstone> Tombstones { get; set; } = new();
        public string? SyncEpoch { get; set; }
    }
}
