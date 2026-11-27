using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class RecipientRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static Contact NewContact(string name) => Contact.Create(Handle.Contact(name), DisplayName.Create(name.ToUpperInvariant()), Now);

    private static Group NewGroup(string name) => Group.Create(Handle.Group(name), DisplayName.Create(name.ToUpperInvariant()), Now);

    [Fact]
    public async Task Contacts_round_trip_with_their_endpoints_and_routing()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);
        alice.SetRouting(Priority.High, [ChannelType.Sms]);
        alice.SetRouting(Priority.Normal, [ChannelType.Sms]);

        await using (var db = database.CreateContext())
        {
            var repository = new ContactRepository(db);
            (await repository.ExistsAsync(alice.Handle, _ct)).ShouldBeFalse();
            repository.Add(alice);
            await new EfUnitOfWork(db).SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new ContactRepository(db);
            var loaded = (await repository.FindByHandleAsync(Handle.Contact("alice"), _ct)).ShouldNotBeNull();
            loaded.Id.ShouldBe(alice.Id);
            loaded.Name.Value.ShouldBe("ALICE");
            loaded.CreatedAt.ShouldBe(Now);
            loaded.SmsNumber.ShouldBe(PhoneNumber.Parse("+353861234567"));
            loaded.EndpointFor(ChannelType.Sms)!.VerifiedAt.ShouldBe(Now);
            loaded.Routing.ShouldBe(alice.Routing);
            (await repository.ExistsAsync(Handle.Contact("alice"), _ct)).ShouldBeTrue();
            (await repository.FindByHandleAsync(Handle.Contact("bob"), _ct)).ShouldBeNull();
            (await repository.ListAsync(_ct)).ShouldHaveSingleItem();

            // Changes to the endpoint and routing are tracked.
            loaded.SetSmsNumber(PhoneNumber.Parse("+353879999999"), Now.AddDays(1));
            loaded.SetRouting(Priority.Normal, []);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new ContactRepository(db).FindByHandleAsync(Handle.Contact("alice"), _ct))!;
            loaded.SmsNumber!.Value.ShouldBe("+353879999999");
            loaded.Routing.ChannelsFor(Priority.Normal).ShouldBeEmpty();
            loaded.Routing.ChannelsFor(Priority.High).ShouldBe([ChannelType.Sms]);
            loaded.RemoveEndpoint(ChannelType.Sms);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new ContactRepository(db).FindByHandleAsync(Handle.Contact("alice"), _ct))!;
            loaded.Endpoints.ShouldBeEmpty();
            loaded.Routing.ShouldBe(ContactRouting.Empty);
            (await ScalarAsync(db, "SELECT Handle AS Value FROM Contacts")).ShouldBe("contact:alice");
            (await ScalarAsync(db, "SELECT Routing AS Value FROM Contacts")).ShouldBe(string.Empty);
        }
    }

    [Fact]
    public async Task The_handle_and_routing_are_stored_as_readable_text()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);
        alice.SetRouting(Priority.Critical, [ChannelType.Sms]);
        alice.SetRouting(Priority.Low, [ChannelType.Sms]);

        await using var db = database.CreateContext();
        db.Contacts.Add(alice);
        await db.SaveChangesAsync(_ct);

        (await ScalarAsync(db, "SELECT Handle AS Value FROM Contacts")).ShouldBe("contact:alice");
        (await ScalarAsync(db, "SELECT Routing AS Value FROM Contacts")).ShouldBe("low=sms;critical=sms");
        (await ScalarAsync(db, "SELECT Channel AS Value FROM ContactEndpoints")).ShouldBe("Sms");
    }

    [Fact]
    public async Task AC57_Handles_are_unique_per_type()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        await using (var db = database.CreateContext())
        {
            db.Contacts.Add(NewContact("family"));
            db.Groups.Add(NewGroup("family")); // contact:family and group:family may coexist
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            db.Contacts.Add(NewContact("family"));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(_ct));
        }

        await using (var db = database.CreateContext())
        {
            db.Groups.Add(NewGroup("family"));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(_ct));
        }
    }

    [Fact]
    public async Task Groups_round_trip_with_contact_and_group_members()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        family.AddMember(alice);
        family.AddMember(kids, new GroupGraph([family, kids]));

        await using (var db = database.CreateContext())
        {
            db.Contacts.Add(alice);
            var repository = new GroupRepository(db);
            repository.Add(kids);
            repository.Add(family);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var repository = new GroupRepository(db);
            var loaded = (await repository.FindByHandleAsync(Handle.Group("family"), _ct)).ShouldNotBeNull();
            loaded.Contains(alice.Id).ShouldBeTrue();
            loaded.Contains(kids.Id).ShouldBeTrue();
            loaded.Members.Count.ShouldBe(2);
            (await repository.ExistsAsync(Handle.Group("kids"), _ct)).ShouldBeTrue();
            (await repository.ExistsAsync(Handle.Group("school"), _ct)).ShouldBeFalse();
            (await repository.ListAsync(_ct)).Count.ShouldBe(2);

            loaded.RemoveMember(kids.Id);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var loaded = (await new GroupRepository(db).FindByHandleAsync(Handle.Group("family"), _ct))!;
            loaded.Members.ShouldHaveSingleItem().MemberContactId.ShouldBe(alice.Id);
        }
    }

    [Fact]
    public async Task AC56_Deleting_a_contact_or_group_also_deletes_its_memberships()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        family.AddMember(alice);
        family.AddMember(kids, new GroupGraph([family, kids]));
        kids.AddMember(alice);
        await using (var db = database.CreateContext())
        {
            db.Contacts.Add(alice);
            db.Groups.AddRange(kids, family);
            await db.SaveChangesAsync(_ct);
        }

        // The use cases remove memberships themselves; the foreign keys cascade even if one is missed.
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Contacts", _ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Groups WHERE Handle = 'group:kids'", _ct);
        }

        await using (var db = database.CreateContext())
        {
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM GroupMembers").SingleAsync(_ct)).ShouldBe(0);
            (await new GroupRepository(db).FindByHandleAsync(Handle.Group("family"), _ct))!.Members.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Removing_through_the_repositories_deletes_the_rows()
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        alice.SetSmsNumber(PhoneNumber.Parse("+353861234567"), Now);
        var family = NewGroup("family");
        family.AddMember(alice);
        await using (var db = database.CreateContext())
        {
            db.Contacts.Add(alice);
            db.Groups.Add(family);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            var contacts = new ContactRepository(db);
            var groups = new GroupRepository(db);
            var loadedFamily = (await groups.FindByHandleAsync(family.Handle, _ct))!;
            var loadedAlice = (await contacts.FindByHandleAsync(alice.Handle, _ct))!;
            loadedFamily.RemoveMember(loadedAlice.Id);
            contacts.Remove(loadedAlice);
            groups.Remove(loadedFamily);
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = database.CreateContext())
        {
            (await db.Database.SqlQueryRaw<int>(
                "SELECT (SELECT COUNT(*) FROM Contacts) + (SELECT COUNT(*) FROM ContactEndpoints) + (SELECT COUNT(*) FROM Groups) + (SELECT COUNT(*) FROM GroupMembers) AS Value")
                .SingleAsync(_ct)).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData("INSERT INTO GroupMembers (GroupId, MemberContactId, MemberGroupId) VALUES ({0}, {1}, NULL)", true)] // same contact twice
    [InlineData("INSERT INTO GroupMembers (GroupId, MemberContactId, MemberGroupId) VALUES ({0}, NULL, NULL)", false)] // no member
    [InlineData("INSERT INTO GroupMembers (GroupId, MemberContactId, MemberGroupId) VALUES ({0}, {1}, {0})", false)] // two members in one row
    public async Task The_database_rejects_invalid_membership_rows(string sql, bool addFirst)
    {
        await using var database = await SqliteTestDatabase.CreateMigratedAsync(_ct);
        var alice = NewContact("alice");
        var family = NewGroup("family");
        if (addFirst)
        {
            family.AddMember(alice);
        }

        await using var db = database.CreateContext();
        db.Contacts.Add(alice);
        db.Groups.Add(family);
        await db.SaveChangesAsync(_ct);

        // Guids are stored as upper-case text by default.
        var groupId = family.Id.Value.ToString().ToUpperInvariant();
        var contactId = alice.Id.Value.ToString().ToUpperInvariant();
        await Should.ThrowAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql, [groupId, contactId], _ct));
    }

    private static Task<string> ScalarAsync(NotificationDbContext db, string sql) => db.Database.SqlQueryRaw<string>(sql).SingleAsync();
}
