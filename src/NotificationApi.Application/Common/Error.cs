using System.Diagnostics.CodeAnalysis;

namespace NotificationApi.Application.Common;

/// <summary>An expected failure (engineering_standards.md §2.3). <see cref="Code"/> is stable; <see cref="Message"/> is for people.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "C# only; 'Error' is the conventional name for a Result error.")]
public sealed record Error(string Code, string Message);
