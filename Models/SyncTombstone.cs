using System;
using SQLite;

namespace NihongoVocab.Models
{
    [Table("SyncTombstones")]
    public class SyncTombstone
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed, NotNull]
        public string EntityType { get; set; } = string.Empty; // "word" 或 "review_log"

        [Indexed, NotNull]
        public string EntityKey { get; set; } = string.Empty;  // word: lowercase text; review_log: $"{wordText}_{yyyyMMddHHmmss}"

        public DateTime DeletedAt { get; set; } = DateTime.Now;
    }
}
