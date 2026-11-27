using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace NotificationApi.Domain.Recipients;

/// <summary>
/// A phone number in E.164 form: <c>+</c> then 8 to 15 digits, the first not 0 (spec AC52). Input is normalised by
/// removing spaces, dashes and parentheses. <see cref="ToString"/> is <see cref="Masked"/>, so a number can't reach a
/// log or the audit log in full by accident (engineering_standards.md §5, AC74, AC82).
/// </summary>
public sealed partial record PhoneNumber
{
    public const int MinDigits = 8;
    public const int MaxDigits = 15;

    private PhoneNumber(string value) => Value = value;

    /// <summary>The full E.164 number, e.g. <c>+353861234567</c>. Only for the SMS provider and the stored endpoint.</summary>
    public string Value { get; }

    /// <summary>Safe to show and log: <c>+3538****567</c>.</summary>
    public string Masked => Mask(Value);

    public static bool TryParse([NotNullWhen(true)] string? input, [NotNullWhen(true)] out PhoneNumber? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalised = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!char.IsWhiteSpace(c) && c is not ('-' or '(' or ')'))
            {
                normalised.Append(c);
            }
        }

        var value = normalised.ToString();
        if (value.Length == 0 || value[0] != '+')
        {
            return false;
        }

        var digits = value.AsSpan(1);
        if (digits.Length is < MinDigits or > MaxDigits || digits.ContainsAnyExceptInRange('0', '9') || digits[0] == '0')
        {
            return false;
        }

        number = new PhoneNumber(value);
        return true;
    }

    public static PhoneNumber Parse(string input) =>
        TryParse(input, out var number)
            ? number
            : throw new FormatException($"Not an E.164 phone number: a '+' then {MinDigits}-{MaxDigits} digits.");

    /// <summary>Masks every E.164 number in free text (a defence in depth for audit summaries).</summary>
    public static string MaskAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FullNumber().Replace(text, m => Mask(m.Value));
    }

    public override string ToString() => Masked;

    /// <summary>Keeps up to 4 leading digits (country and area code) and up to 3 trailing digits.</summary>
    private static string Mask(string e164)
    {
        var digits = e164[1..];
        var prefix = Math.Min(4, digits.Length / 3);
        var suffix = Math.Min(3, digits.Length / 4);
        return $"+{digits[..prefix]}****{digits[^suffix..]}";
    }

    [GeneratedRegex(@"\+[0-9]{8,15}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex FullNumber();
}
