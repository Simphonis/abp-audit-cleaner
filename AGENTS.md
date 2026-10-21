# AGENTS.md

## Overview

Single-purpose ABP module library (NuGet package `Simphonis.Abp.AuditLogs`) that deletes old audit log entries: a background worker (`AuditLogsCleanerWorker`) runs a periodic cleanup, and `IAuditLogCleaner` is exposed for manual cleanup. The module deliberately does not register the worker: consuming applications opt in with `AddBackgroundWorkerAsync<AuditLogsCleanerWorker>()`. The worker's period comes from `AuditLogsCleanerWorkerOptions.Period` (standard .NET options, default `SimphonisAuditLogConsts.DefaultWorkerPeriodInMinutes`), while the retention period and max delete count are ABP settings. Two projects: `src/Simphonis.Abp.AuditLogs` (the shipped library) and `test/Simphonis.Abp.AuditLogs.Tests`. Requires the .NET 10 SDK.

The solution file is the XML format `Simphonis.Abp.AuditLogs.slnx` — there is no `.sln`.

## Commands

CI order (see `.github/workflows/build-and-publish.yml`):

```
dotnet restore Simphonis.Abp.AuditLogs.slnx
dotnet build Simphonis.Abp.AuditLogs.slnx -c Release --no-restore
dotnet test --solution Simphonis.Abp.AuditLogs.slnx -c Release --no-build
```

Single test (standard `--filter` syntax works; `dotnet test` uses the Microsoft.Testing.Platform runner configured in `global.json`):

```
dotnet test --solution Simphonis.Abp.AuditLogs.slnx -c Release --no-build --filter "FullyQualifiedName~Should_Clean_AuditLogs"
```

There is no separate lint/format/typecheck tooling. Style rules (naming, file-scoped namespaces, `var` discouraged, braces on new lines) are enforced as compiler warnings via `.editorconfig` during `dotnet build` — treat build warnings as style failures.

## Versioning and release

- All versions derive from `AbpVersion` in `common.props`, which pins both the ABP package references and the library version. Bump versions in `common.props`, not in the `.csproj` files.
- Local/dev builds get a `-dev` package suffix; CI overrides it with `-p:PackageVersion` on tag builds.
- Publishing is tag-driven: pushing `vX.Y.Z` (or `vX.Y.Z-suffix` for prerelease) publishes to nuget.org via Trusted Publishing and creates the GitHub Release.
- Do not rename `.github/workflows/build-and-publish.yml`: the nuget.org Trusted Publishing policy references this file name.

## Dependencies and CI policy

- Dependabot covers GitHub Actions only. NuGet package updates are deliberately manual (security alerts only) — do not add a NuGet dependabot entry.
- GitHub Actions are pinned by commit SHA with a version comment; keep that format when editing workflows.

## Code conventions

- Both projects are woven by ConfigureAwait.Fody (`FodyWeavers.xml`): never add manual `ConfigureAwait(false)` calls; CA2007 is disabled.
- Tests are pure unit tests using NSubstitute + Shouldly + xunit.v3: no database, no ABP IoC bootstrapping — construct the class under test directly with mocks. Pass `TestContext.Current.CancellationToken` instead of `CancellationToken.None` (xunit.v3 idiom used throughout).
- The test project reuses the library's root namespace `Simphonis.Abp.AuditLogs`; `InternalsVisibleTo` for the test assembly is already declared in `src/Simphonis.Abp.AuditLogs/Properties/AssemblyInfo.cs`.
- New ABP settings need: a constant in `SimphonisAuditLogSettings`, a default in `SimphonisAuditLogConsts`, a `SettingDefinition` in `SimphonisAuditLogSettingDefinitionProvider`, and localized strings in both `Localization/fr.json` and `Localization/en.json` (default culture is fr-FR).
