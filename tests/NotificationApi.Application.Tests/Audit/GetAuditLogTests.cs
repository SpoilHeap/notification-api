using NotificationApi.Application.Audit;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests.Audit;

public sealed class GetAuditLogTests
{
    private readonly RecipientsHarness _h = new();

    [Fact]
    public async Task An_empty_log_has_one_empty_page()
    {
        var page = await _h.GetAuditLog.HandleAsync(1, TestContext.Current.CancellationToken);

        page.Entries.ShouldBeEmpty();
        (page.Page, page.PageCount, page.Total).ShouldBe((1, 1, 0));
    }

    [Fact]
    public async Task AC82_Entries_are_newest_first_and_paged()
    {
        for (var i = 0; i < GetAuditLog.PageSize + 5; i++)
        {
            _h.Trail.Record(RecipientsHarness.Actor, AuditAction.Update, AuditEntityType.Contact, "contact:alice", $"change {i}");
        }

        var first = await _h.GetAuditLog.HandleAsync(1, TestContext.Current.CancellationToken);
        var second = await _h.GetAuditLog.HandleAsync(2, TestContext.Current.CancellationToken);
        var beyond = await _h.GetAuditLog.HandleAsync(99, TestContext.Current.CancellationToken);
        var before = await _h.GetAuditLog.HandleAsync(-3, TestContext.Current.CancellationToken);

        first.Entries.Count.ShouldBe(GetAuditLog.PageSize);
        first.Entries[0].Summary.ShouldBe($"change {GetAuditLog.PageSize + 4}");
        first.PageCount.ShouldBe(2);
        first.Total.ShouldBe(GetAuditLog.PageSize + 5);
        second.Entries.Count.ShouldBe(5);
        second.Entries[^1].Summary.ShouldBe("change 0");
        beyond.Page.ShouldBe(2);
        before.Page.ShouldBe(1);
        first.Entries[0].HaUserDisplayName.ShouldBe("Alice Admin");
        first.Entries[0].OccurredAt.ShouldBe(_h.Time.GetUtcNow());
    }
}
