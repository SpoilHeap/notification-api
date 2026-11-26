# 0010. xUnit v3 on Microsoft.Testing.Platform, with Shouldly

- Status: Accepted
- Date: 2026-11-25

## Context

engineering_standards.md §4.1 picks xUnit v3 for tests and Shouldly for assertions. xUnit v3 test projects are executables and can run on either VSTest or **Microsoft.Testing.Platform** (MTP). .NET 10's `dotnet test` supports MTP natively when `global.json` opts in.

On MTP, the VSTest pieces don't apply: `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are VSTest adapters, and `coverlet.collector` is a VSTest data collector. The standards originally listed `coverlet.collector` for coverage.

For assertions, FluentAssertions v8 moved to a commercial licence.

## Decision

- **xUnit v3 on MTP.** `global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`. Test projects are `OutputType=Exe` (set in `tests/Directory.Build.props`) and reference `xunit.v3`. No `Microsoft.NET.Test.Sdk`, no `xunit.runner.visualstudio`.
- **Coverage with `Microsoft.Testing.Extensions.CodeCoverage`**, Cobertura output, rendered and gated with ReportGenerator. `coverlet.collector` is dropped.
- **Shouldly** for assertions (open source, no commercial licence). Not FluentAssertions.

## Alternatives considered

- **xUnit v3 on VSTest with `coverlet.collector`.** Works today, but VSTest is the legacy path. It needs extra adapter packages, and `coverlet.collector` only works as a VSTest data collector.
- **FluentAssertions.** v8 needs a paid licence for commercial use. v7 stays free but only gets critical fixes.

## Consequences

- `dotnet test` runs every test project on MTP. Coverage: `dotnet test --coverage --coverage-output-format cobertura`.
- IDE test runners must support MTP. Use a current version.
- MTP command-line options differ from VSTest's (e.g. no `--collect`). CI scripts use the MTP flags.
