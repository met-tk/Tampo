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

        [Ignore]
        public string ClientIpDisplay => !string.IsNullOrWhiteSpace(ClientIp)
            ? ClientIp
            : Services.LocalizationService.Instance.GetString("DeviceUnknownIp", "未知 IP");

        [Ignore]
        public string LastSeenFullDisplay => string.Format(
            Services.LocalizationService.Instance.GetString("DeviceActivePrefix", "最近活跃: {0}"),
            LastSeenDisplay);

        [Ignore]
        public string CreatedAtFullDisplay => string.Format(
            Services.LocalizationService.Instance.GetString("DeviceBoundPrefix", "绑定于: {0}"),
            CreatedAtDisplay);

        [Ignore]
        public string ActionPullText => Services.LocalizationService.Instance.GetString("ButtonPullFromDevice", "从该设备拉取");

        [Ignore]
        public string ActionRenameText => Services.LocalizationService.Instance.GetString("ButtonRenameDevice", "重命名");

        [Ignore]
        public string ActionUnbindText => Services.LocalizationService.Instance.GetString("ButtonUnbindDevice", "解除绑定");
    }
}
