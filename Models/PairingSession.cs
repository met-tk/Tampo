using System;
using SQLite;

namespace NihongoVocab.Models
{
    [Table("PairingSessions")]
    public class PairingSession
    {
        [PrimaryKey]
        public string Code { get; set; } = string.Empty;

        [NotNull]
        public long ExpiresAt { get; set; }

        [NotNull]
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [Ignore]
        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= ExpiresAt;
    }
}
