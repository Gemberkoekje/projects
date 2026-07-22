# MRE: BLAZOR101 duplicate scoped-css after upgrading DotNetProjectFile.Analyzers 1.15.0 → 1.15.1

Minimal reproducible example for the spurious **BLAZOR101 – "More than one scoped
css files were found for the razor component"** error that appears after bumping
`DotNetProjectFile.Analyzers` from `1.15.0` to `1.15.1` (also present in `1.15.2`).

## TL;DR — it is not Linux, it is SonarQube

The build only fails when **`SonarQubeIntegration=true`** is set. That property is set
automatically by the SonarScanner for .NET (`dotnet-sonarscanner begin … / end`) that
wraps the build **on the build server**. It is *not* set on a normal dev-machine build,
which is why the error only shows up in CI. It reproduces on Windows just as well as on
Linux once the flag is set.

| # | Analyzer | `SonarQubeIntegration` | Result |
|---|----------|------------------------|--------|
| A | 1.15.0   | (unset)                | ✅ build succeeds |
| B | 1.15.0   | `true`                 | ✅ build succeeds |
| C | 1.15.1   | (unset)                | ✅ build succeeds |
| D | 1.15.1   | `true`                 | ❌ **BLAZOR101** |

Verified identical on **Windows** (`dotnet 10.0.302`) and **Linux**
(`mcr.microsoft.com/dotnet/sdk:10.0`, x86_64) — only row D fails, on both.

## How to reproduce

Requires the .NET SDK (tested with 10.0.302).

```bash
# passes
dotnet build -p:AnalyzerVersion=1.15.1

# fails with BLAZOR101
dotnet build -p:AnalyzerVersion=1.15.1 -p:SonarQubeIntegration=true

# same project, old analyzer, still passes even with the flag
dotnet build -p:AnalyzerVersion=1.15.0 -p:SonarQubeIntegration=true
```

Or run the whole 2×2 matrix:

```bash
pwsh ./repro.ps1
```

The project is a tiny Razor Class Library (`Microsoft.NET.Sdk.Razor`) with a single
component and its scoped stylesheet, mirroring the reporter's paths:

```
Features/Components/CompareConditionTreeView.razor
Features/Components/CompareConditionTreeView.razor.css
```

### Where the analyzer has to be referenced

BLAZOR101 is emitted by the **Razor SDK while building the component's own project**, so
the analyzer's MSBuild targets have to be imported *into that project*. This MRE therefore
puts the `DotNetProjectFile.Analyzers` `PackageReference` directly on the Razor project.

A standalone `.net.csproj` that only references the analyzer (the usual "lint the project
files" pattern) will **not** reproduce this on its own — it is a plain `Microsoft.NET.Sdk`
project with no scoped css and no Razor SDK. The trigger requires the analyzer to reach the
Blazor project, e.g. a repo-wide `Directory.Build.props`/`Directory.Packages.props` that
adds the package to every project (including the Blazor one), plus `SonarQubeIntegration=true`.

## Root cause

**BLAZOR101 is not raised by this analyzer.** It is raised by the .NET Razor SDK
(`Microsoft.NET.Sdk.StaticWebAssets.ScopedCss.targets`). The analyzer only *perturbs the
MSBuild items* that the Razor SDK then reads.

1. The Razor SDK discovers scoped-css files by scanning **both** `@(None)` **and**
   `@(Content)` (`ResolveScopedCssInputs` target):

   ```xml
   <DiscoverDefaultScopedCssItems Content="@(None);@(Content)" SupportsScopedCshtmlCss="true">
     <Output TaskParameter="DiscoveredScopedCssInputs" ItemName="_DiscoveredScopedCssInputs" />
   </DiscoverDefaultScopedCssItems>
   ```

   A `*.razor.css` file is a `@(None)` item by default, so normally it is found **once**.

2. In **1.15.1** the SonarQube integration in
   `build/DotNetProjectFile.Analyzers.targets` was rewritten from a fixed list of
   project-file extensions to a blanket sweep of `@(None)` (and `@(AdditionalFiles)`)
   into `@(Content)`:

   ```xml
   <!-- 1.15.0: only project-file extensions were added -->
   <Content Include="**/*.??proj" ... />
   <Content Include="**/*.props"  ... />
   <Content Include="**/*.targets" ... />
   <Content Include="**/*.resx"   ... />
   <Content Include="**/*.slnx"   ... />

   <!-- 1.15.1: EVERY None / AdditionalFiles item becomes Content -->
   <Content Include="@(None);@(AdditionalFiles)" Exclude="@(Content)" ... />
   ```

   `*.razor.css` is a `@(None)` item, so it is now **also** added to `@(Content)`.

3. The `Exclude="@(Content)"` guard does not help: the Razor SDK only moves scoped-css
   into `@(Content)` during the **execution** phase (`ResolveScopedCssInputs`, which runs
   `<Content Remove/Include="@(ScopedCssInput)">`). The analyzer's `<ItemGroup>` is a
   static **evaluation**-phase item, so at that point the scoped-css is not in
   `@(Content)` yet — the guard cannot see it and the file is duplicated.

4. Discovery then scans `@(None);@(Content)` and finds the file **twice** →
   `ScopedCssInput` contains two identical entries → BLAZOR101.

### Verified item counts (execution phase, `-t:ResolveScopedCssInputs`)

| build | `@(None)` `.razor.css` | `@(Content)` `.razor.css` | `ScopedCssInput` |
|-------|:-:|:-:|:-:|
| 1.15.0 + Sonar | 1 | 1 | **1** (ok) |
| 1.15.1 + Sonar | 1 | **2** | **2** (BLAZOR101) |

## Change that introduced it

Diff of `src/DotNetProjectFile.Analyzers/build/DotNetProjectFile.Analyzers.targets`
between tags `v1.15.0` and `v1.15.1`. The `<ItemGroup>` guarded by
`Condition="'$(SonarQubeIntegration)' == 'true'"` is the only functional change between
the two releases.

## Possible fixes (for the maintainer)

Any of these breaks the duplication:

- Exclude scoped-css (and any Razor-SDK-owned items) from the sweep, e.g. add
  `*.razor.css;*.cshtml.css` (and `@(ScopedCssInput)` / `@(RazorComponent)`) to the
  `Exclude`.
- Move the `<Content>` injection into a `Target` that runs **after** the Razor SDK has
  resolved scoped-css (so `Exclude="@(Content)"` sees the SDK's own copy), rather than a
  static evaluation-phase `<ItemGroup>`.
- Restrict the SonarQube sweep to the file types Sonar actually needs (project files,
  config, resx …) as 1.15.0 did, instead of all of `@(None)`.
```
