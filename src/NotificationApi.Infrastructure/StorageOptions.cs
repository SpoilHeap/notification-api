using System.ComponentModel.DataAnnotations;

namespace NotificationApi.Infrastructure;

/// <summary>Bootstrap storage settings (config section <c>Storage</c>, env <c>Storage__DataPath</c>).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Persistent directory. <c>/data</c> in the add-on; the only path that survives updates.</summary>
    [Required]
    public string DataPath { get; set; } = "/data";

    public string DatabasePath => Path.Combine(DataPath, "notification-api.db");

    public string KeysPath => Path.Combine(DataPath, "keys");
}
