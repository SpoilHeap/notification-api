using System.Text.RegularExpressions;

namespace NotificationApi.Web.Tests.Components;

/// <summary>
/// Every CSS class the Razor components use is defined in wwwroot/app.css, so markup can't silently fall back to
/// browser defaults (as the routing grid and buttons once did).
/// </summary>
public sealed partial class StylesheetTests
{
    /// <summary>Classes added by the framework rather than written in markup, which app.css doesn't have to style.</summary>
    private static readonly HashSet<string> FrameworkClasses =
    [
        "active", // NavLink, on the link for the current page (styled, but listed in case the selector changes)
        "valid", "invalid", "modified", // Blazor forms' field CSS classes (EditForm/InputBase), not used today
    ];

    [Fact]
    public void Every_class_used_in_razor_markup_is_defined_in_app_css()
    {
        var root = Path.Combine(RepoRoot(), "src", "NotificationApi.Web");
        var defined = DefinedClasses(File.ReadAllText(Path.Combine(root, "wwwroot", "app.css")));

        var used = Directory.EnumerateFiles(Path.Combine(root, "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => UsedClasses(File.ReadAllText(file)).Select(name => (File: Path.GetFileName(file), Class: name)))
            .ToList();

        used.ShouldNotBeEmpty();
        var missing = used.Where(u => !defined.Contains(u.Class) && !FrameworkClasses.Contains(u.Class))
            .Select(u => $"{u.File}: .{u.Class}")
            .Distinct()
            .ToList();
        missing.ShouldBeEmpty($"Classes used in Razor markup but not defined in app.css:\n{string.Join('\n', missing)}");
    }

    [Theory]
    [InlineData("<p class=\"notice success\">", new[] { "notice", "success" })]
    [InlineData("<span class=\"badge @StateBadge(x.State)\">", new[] { "badge" })]
    [InlineData("<p class=\"notice @(ok ? \"success\" : \"error\")\">", new[] { "notice", "success", "error" })]
    [InlineData("<a href=\"x\">", new string[0])]
    public void Class_names_are_read_from_static_and_inline_razor_values(string markup, string[] expected) =>
        UsedClasses(markup).ShouldBe(expected, ignoreOrder: true);

    [Fact]
    public void Class_names_are_read_from_css_selectors_but_not_from_comments_or_urls() =>
        DefinedClasses("/* .ghost */ a.one:hover, .two > .three-x { background: url(\"data:x.svg\"); width: 1.5rem; }")
            .ShouldBe(["one", "two", "three-x"], ignoreOrder: true);

    /// <summary>
    /// The class names in each <c>class="…"</c> attribute: the static words, plus the string literals of inline
    /// <c>@(…)</c> expressions. Names returned by a method call (<c>@StateBadge(…)</c>) can't be seen here.
    /// </summary>
    private static IEnumerable<string> UsedClasses(string razor)
    {
        foreach (Match attribute in ClassAttribute().Matches(razor))
        {
            var value = attribute.Groups["value"].Value;
            foreach (Match literal in StringLiteral().Matches(value))
            {
                foreach (var name in literal.Groups["text"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return name;
                }
            }

            var withoutCode = RazorExpression().Replace(value, " ");
            foreach (var name in withoutCode.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                yield return name;
            }
        }
    }

    private static HashSet<string> DefinedClasses(string css)
    {
        var withoutNoise = Comment().Replace(css, " ");
        withoutNoise = CssString().Replace(withoutNoise, "\"\"");
        withoutNoise = Declarations().Replace(withoutNoise, "{}"); // only selectors remain (plus @media preludes)
        return [.. CssClassSelector().Matches(withoutNoise).Select(m => m.Groups["name"].Value)];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotificationApi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root (NotificationApi.slnx) not found.");
    }

    // class="…" where the value may hold @(…) expressions with quoted strings (one level of nested parentheses).
    [GeneratedRegex("""\bclass="(?<value>(?:@\((?:[^()]|\([^()]*\))*\)|[^"])*)" """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex ClassAttribute();

    [GeneratedRegex("""@\((?:[^()]|\([^()]*\))*\)|@[\w.]+(?:\((?:[^()]|\([^()]*\))*\))?""")]
    private static partial Regex RazorExpression();

    [GeneratedRegex("""(?<=@\((?:[^()]|\([^()]*\))*)"(?<text>[\w\s-]*)" """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("\"[^\"]*\"|'[^']*'")]
    private static partial Regex CssString();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Declarations();

    [GeneratedRegex(@"\.(?<name>-?[A-Za-z_][\w-]*)")]
    private static partial Regex CssClassSelector();
}
