using System;
using SQLite;

namespace NihongoVocab.Models
{
    [Table("PairedDevices")]
    public class PairedDevice
    {
        [PrimaryKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [NotNull]
        public string DeviceName { get; set; } = string.Empty;

        [Indexed(Unique = true), NotNull]
        public string AuthToken { get; set; } = string.Empty;

        public string? ClientIp { get; set; }

        [NotNull]
        public long LastSeenAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [NotNull]
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public string? DeviceModel { get; set; }

        public string? CustomAlias { get; set; }

        [Ignore]
        public bool IsOnline { get; set; }

        [Ignore]
        public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomAlias)
            ? CustomAlias
            : (!string.IsNullOrWhiteSpace(DeviceModel) ? $"{DeviceName} ({DeviceModel})" : DeviceName);

        [Ignore]
        public string LastSeenDisplay => DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, LastSeenAt)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        [Ignore]
        public string CreatedAtDisplay => DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, CreatedAt)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
