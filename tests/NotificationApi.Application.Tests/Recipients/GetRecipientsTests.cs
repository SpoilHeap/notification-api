namespace NotificationApi.Application.Tests.Recipients;

public sealed class GetRecipientsTests
{
    private readonly RecipientsHarness _h = new();

    [Fact]
    public async Task AC40_Lists_every_contact_and_group_contacts_first_then_by_handle()
    {
        await _h.GroupAsync("family", "Family");
        await _h.ContactAsync("bob", "Bob", "+353861234567");
        await _h.GroupAsync("alarm", "Alarm");
        await _h.ContactAsync("alice", "Alice");

        var list = await _h.GetRecipients.HandleAsync(TestContext.Current.CancellationToken);

        list.Recipients.Select(r => (r.Handle, r.Name, r.Type)).ShouldBe(
        [
            ("contact:alice", "Alice", "contact"),
            ("contact:bob", "Bob", "contact"),
            ("group:alarm", "Alarm", "group"),
            ("group:family", "Family", "group"),
        ]);
        list.Revision.ShouldBe("5");
    }
}
