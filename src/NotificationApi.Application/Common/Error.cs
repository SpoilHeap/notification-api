using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Application.Common;

/// <summary>An expected failure (engineering_standards.md §2.3). <see cref="Code"/> is stable; <see cref="Message"/> is for people.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "C# only; 'Error' is the conventional name for a Result error.")]
public sealed record Error(string Code, string Message)
{
    public const string ValidationCode = "validation";

    private static readonly IReadOnlyDictionary<string, string[]> NoFields = new Dictionary<string, string[]>();

    /// <summary>Field-level messages for a <see cref="ValidationCode"/> error, keyed by the request's JSON field name.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } = NoFields;

    /// <summary>Invalid input. Web maps it to a 400 <c>ValidationProblemDetails</c>.</summary>
    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new Error(ValidationCode, "The request is invalid.") { FieldErrors = fieldErrors };
    }
}
