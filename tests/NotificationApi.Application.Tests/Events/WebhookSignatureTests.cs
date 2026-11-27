using System.Text;
using NotificationApi.Application.Events;

namespace NotificationApi.Application.Tests.Events;

/// <summary>Known-answer tests. The first vector is also in spec §4.4, so the HA integration tests against the same one.</summary>
public sealed class WebhookSignatureTests
{
    public const string SpecSecret = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="; // bytes 0x00..0x1f
    public const long SpecTimestamp = 1795796120; // 2026-11-27T16:15:20Z
    public const string SpecBody = """{"id":"evt_019ac63f8e407c3a9d1b2e4f5a6b7c8d","type":"webhook.test","occurred_at":"2026-11-27T16:15:20Z","data":{}}""";
    public const string SpecSignature = "sha256=c2fcb3fbbe01d66932b4d9a193e368ef70bf42739a894e6dd48b98d5d50911ff";

    [Fact]
    public void AC37_Matches_the_spec_test_vector() =>
        WebhookSignature.Compute(Convert.FromBase64String(SpecSecret), SpecTimestamp, Encoding.UTF8.GetBytes(SpecBody))
            .ShouldBe(SpecSignature);

    [Fact]
    public void The_key_is_the_decoded_secret_not_its_base64_text() =>
        WebhookSignature.Compute(Encoding.ASCII.GetBytes(SpecSecret), SpecTimestamp, Encoding.UTF8.GetBytes(SpecBody))
            .ShouldNotBe(SpecSignature);

    [Fact]
    public void Matches_a_second_independently_computed_vector()
    {
        // Computed with Python's hmac module: 48 bytes of 0xff, so a longer-than-minimum key too.
        var body = """{"id":"evt_0123456789abcdef0123456789abcdef","type":"recipients.changed","occurred_at":"2023-11-14T22:13:20Z","data":{"revision":"18"}}""";

        WebhookSignature.Compute(Convert.FromBase64String(new string('/', 64)), 1700000000, Encoding.UTF8.GetBytes(body))
            .ShouldBe("sha256=e51bdce035930fadfe3318de7ed6016ac8867aefffe02cbd946f079aad79c709");
    }

    [Fact]
    public void Matches_the_vector_from_the_HA_integration()
    {
        // Cross-repo check: notification-api-ha's Python verifier (tests/test_receiver.py) uses this vector.
        // A mismatch means the two implementations disagree on the signed bytes or the key.
        const string body = """{"id":"evt_test_0001","type":"webhook.test","occurred_at":"2026-11-27T16:15:20Z","data":{}}""";

        WebhookSignature.Compute(Convert.FromBase64String(SpecSecret), SpecTimestamp, Encoding.UTF8.GetBytes(body))
            .ShouldBe("sha256=51e8fd165211ea54ae0a0993391d96791f82a347df714ce5f82d3a627b971b94");
    }

    [Fact]
    public void Changes_with_the_timestamp_and_the_body()
    {
        var key = Convert.FromBase64String(SpecSecret);
        var body = Encoding.UTF8.GetBytes(SpecBody);
        var signature = WebhookSignature.Compute(key, SpecTimestamp, body);

        WebhookSignature.Compute(key, SpecTimestamp + 1, body).ShouldNotBe(signature);
        WebhookSignature.Compute(key, SpecTimestamp, [.. body, (byte)' ']).ShouldNotBe(signature);
    }

    [Fact]
    public void Is_sha256_prefixed_lower_case_hex() =>
        WebhookSignature.Compute(new byte[32], 1, [])
            .ShouldMatch("^sha256=[0-9a-f]{64}$");

    [Fact]
    public void Timestamps_are_plain_decimal_seconds() =>
        WebhookSignature.FormatTimestamp(SpecTimestamp).ShouldBe("1795796120");
}
