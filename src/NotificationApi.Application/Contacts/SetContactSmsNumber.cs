using Microsoft.Extensions.Logging;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Contacts;

/// <summary>
/// Adds or changes a contact's SMS number, validated and stored in E.164 (AC52). It's verified on entry, because an
/// admin typed it. Endpoints aren't part of <c>/recipients</c>, so the revision doesn't change.
/// </summary>
public sealed class SetContactSmsNumber(
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    AuditTrail audit,
    TimeProvider time,
    ILogger<SetContactSmsNumber> logger)
{
    /// <returns>The stored number. Its <c>ToString</c> is masked.</returns>
    public async Task<Result<PhoneNumber>> HandleAsync(AuditActor actor, string contactName, string? number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await contacts.FindAsync(contactName, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contact = found.Value;
        if (!PhoneNumber.TryParse(number, out var phone))
        {
            var target = contact.Handle.ToString();
            RecipientLog.Rejected(logger, target, [RecipientFields.PhoneNumber]);
            return RecipientErrors.Field(
                RecipientFields.PhoneNumber,
                string.IsNullOrWhiteSpace(number)
                    ? "The phone number is required."
                    : $"Enter the number in international form: '+', the country code, then the number ({PhoneNumber.MinDigits}-{PhoneNumber.MaxDigits} digits), e.g. +44 7700 900123.");
        }

        var previous = contact.SmsNumber;
        if (!contact.SetSmsNumber(phone, time.GetUtcNow()))
        {
            return phone;
        }

        var summary = previous is null ? $"SMS number set to {phone.Masked}" : $"SMS number changed from {previous.Masked} to {phone.Masked}";
        audit.Record(actor, previous is null ? AuditAction.Create : AuditAction.Update, AuditEntityType.Endpoint, contact.Handle.ToString(), summary);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var masked = phone.Masked;
        RecipientLog.SmsNumberSet(logger, actor.HaUserId, contact.Handle, masked);
        return phone;
    }
}
