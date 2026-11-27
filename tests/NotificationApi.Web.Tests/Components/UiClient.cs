using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NotificationApi.Web.Tests.Components;

/// <summary>
/// Drives the Blazor SSR forms as a browser behind Ingress does: GET the page, take the named form's hidden fields
/// (antiforgery token and handler), add the visible fields, POST. Redirects are not followed, so PRG can be checked.
/// </summary>
internal sealed class UiClient(HttpClient client) : IDisposable
{
    public static UiClient For(WebApplicationFactory<Program> factory) =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    public async Task<string> GetAsync(string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(expected, path);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Submits the form named <paramref name="formName"/> on the page at <paramref name="path"/>. Dispose the response.</summary>
    public async Task<HttpResponseMessage> PostAsync(string path, string formName, IEnumerable<KeyValuePair<string, string>>? fields = null, bool antiforgery = true)
    {
        var form = IntegrationPageTests.ParseForm(await GetAsync(path), formName);
        if (!antiforgery)
        {
            form.Remove("__RequestVerificationToken");
        }

        var content = form.Concat(fields ?? []).ToList();
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(content);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Posts fields already taken from a page (e.g. a stale form). Dispose the response.</summary>
    public async Task<HttpResponseMessage> PostFieldsAsync(string path, IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var request = NotificationApiFactory.IngressRequest(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(fields);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Submits a form that must succeed: checks the 303 and returns the page it redirects to.</summary>
    public async Task<string> SubmitAsync(string path, string formName, params (string Name, string Value)[] fields)
    {
        using var response = await PostAsync(path, formName, fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.SeeOther, $"{formName} on {path} didn't redirect: {ErrorsIn(body)}");
        var location = response.Headers.Location.ShouldNotBeNull().OriginalString;
        location.ShouldStartWith("/");
        return await GetAsync(location);
    }

    /// <summary>Submits a form that must fail validation: checks the 200 (no redirect) and returns the page.</summary>
    public async Task<string> SubmitInvalidAsync(string path, string formName, params (string Name, string Value)[] fields)
    {
        using var response = await PostAsync(path, formName, fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    public Task<string> CreateContactAsync(string handle, string name, string? sms = null) =>
        CreateContactCoreAsync(handle, name, sms);

    public Task<string> CreateGroupAsync(string handle, string name, params string[] members) =>
        CreateGroupCoreAsync(handle, name, members);

    public void Dispose() => client.Dispose();

    private async Task<string> CreateContactCoreAsync(string handle, string name, string? sms)
    {
        var page = await SubmitAsync("/contacts", "create-contact", ("NewContact.Handle", handle), ("NewContact.Name", name));
        if (sms is not null)
        {
            page = await SubmitAsync($"/contacts/{handle}", "set-sms", ("Sms.Number", sms));
        }

        return page;
    }

    private async Task<string> CreateGroupCoreAsync(string handle, string name, string[] members)
    {
        var page = await SubmitAsync("/groups", "create-group", ("NewGroup.Handle", handle), ("NewGroup.Name", name));
        foreach (var member in members)
        {
            page = await SubmitAsync($"/groups/{handle}", "add-member", ("AddMember.Member", member));
        }

        return page;
    }

    private static string ErrorsIn(string html) =>
        string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(html, "class=\"field-error\"[^>]*>(.*?)</div></div>").Select(m => m.Groups[1].Value));
}
