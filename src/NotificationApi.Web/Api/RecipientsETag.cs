using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace NotificationApi.Web.Api;

/// <summary>
/// The recipients revision as a strong ETag (<c>"17"</c>), and <c>If-None-Match</c> matching for the 304 answer
/// (spec §4.2, AC43). <c>If-None-Match</c> uses the weak comparison of RFC 9110 §13.1.2, so <c>W/"17"</c> also matches.
/// </summary>
internal static class RecipientsETag
{
    public static string For(string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        return $"\"{revision}\"";
    }

    /// <summary>True when any tag in the header (or <c>*</c>) matches the current revision: answer 304.</summary>
    public static bool IsNotModified(StringValues ifNoneMatch, string revision)
    {
        if (StringValues.IsNullOrEmpty(ifNoneMatch) || !EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags))
        {
            return false;
        }

        var current = new EntityTagHeaderValue(For(revision));
        return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
    }
}
