# 0011. No NuGet lock files

- Status: Accepted (supersedes the lock-file rule in engineering_standards.md §1 and §8)
- Date: 2026-11-26

## Context

The scaffold used `RestorePackagesWithLockFile` and restored with `--locked-mode` in CI and in the Docker build.

The first CI run failed with `NU1004` in both jobs. The .NET SDK adds implicit package references versioned to **its own runtime patch**, e.g. `Microsoft.AspNetCore.App.Internal.Assets` (Blazor's framework assets). The lock files were generated with SDK 10.0.400 (runtime 10.0.11). The GitHub runner and the `mcr.microsoft.com/dotnet/sdk:10.0` image had SDK 10.0.404 (runtime 10.0.15). So the lock files are only valid for one exact SDK patch, and Microsoft ships SDK patches roughly monthly, including security fixes.

## Decision

Don't use NuGet lock files. Deterministic package versions come from **Central Package Management** (`Directory.Packages.props`) with `CentralPackageTransitivePinningEnabled`. The SDK floats within the pinned feature band (`global.json`: `10.0.400`, `rollForward: latestFeature`), so SDK security patches are picked up automatically.

## Consequences

- No SDK-patch coupling: local machines, CI and the Docker build can run different 10.0.4xx patches.
- Direct and centrally pinned transitive versions are still fixed. Unpinned transitive versions resolve to the lowest matching version, as NuGet always does.
- We lose lock-file content-hash verification. NuGet still verifies package signatures from nuget.org, and NuGetAudit (warnings are errors) plus the CI vulnerable-package check still run.
- Dependabot PRs need no lock-file regeneration.

## Alternatives considered

- **Pin the SDK exactly** in `global.json` and the Docker image tag, and keep locked mode. Rejected: every monthly SDK patch would need a coordinated bump of global.json, the image tag, every developer's SDK and the lock files. Dependabot PRs would fail CI until regenerated.
- **Keep lock files but restore without locked mode.** Rejected: the lock files would be rewritten on every SDK patch and verify nothing.
