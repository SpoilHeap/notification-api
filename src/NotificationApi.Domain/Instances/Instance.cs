using System.Globalization;

namespace NotificationApi.Domain.Instances;

/// <summary>
/// The single row describing this installation (spec §7): its <see cref="InstanceId"/>, and the revision of the
/// recipient list that <c>GET /recipients</c> exposes as an ETag.
/// </summary>
public sealed class Instance
{
    /// <summary>The revision of a new database (spec §4.2).</summary>
    public const long InitialRecipientsRevision = 1;

    private Instance(InstanceId id, long recipientsRevisionNumber, DateTimeOffset createdAt)
    {
        Id = id;
        RecipientsRevisionNumber = recipientsRevisionNumber;
        CreatedAt = createdAt;
    }

    public InstanceId Id { get; private set; }

    /// <summary>Increases by one whenever a contact or group is created, renamed or deleted.</summary>
    public long RecipientsRevisionNumber { get; private set; }

    /// <summary>The revision as the API shows it: an opaque string (spec §4.2).</summary>
    public string RecipientsRevision => RecipientsRevisionNumber.ToString(CultureInfo.InvariantCulture);

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new installation, with a fresh random id.</summary>
    public static Instance Create(DateTimeOffset now) => new(InstanceId.New(), InitialRecipientsRevision, now);

    /// <summary>Records a change to the recipient list. Returns the new revision.</summary>
    public string BumpRecipientsRevision()
    {
        RecipientsRevisionNumber = checked(RecipientsRevisionNumber + 1);
        return RecipientsRevision;
    }
}
