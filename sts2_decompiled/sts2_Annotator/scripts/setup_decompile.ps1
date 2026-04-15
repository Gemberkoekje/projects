<#
Sets up the sts2 decompiled project from the game DLL.

Steps:
  1) Installs ilspycmd globally if not already present
  2) Decompiles sls2.dll (or the specified DLL) to sts2/
  3) Replaces the generated project file with patched settings
  4) Adds all required NuGet packages

Usage (from workspace root):
  pwsh ./sts2_Annotator/scripts/setup_decompile.ps1
  pwsh ./sts2_Annotator/scripts/setup_decompile.ps1 -DllPath "C:\path\to\sts2.dll"
#>

param(
    [string]$DllPath = "sts2.dll"
)

$ErrorActionPreference = "Stop"

# Resolve workspace root (two levels up from sts2_Annotator/scripts/)
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

# 1. Ensure ilspycmd is available
if (-not (dotnet tool list --global | Select-String "ilspycmd")) {
    Write-Host "Installing ilspycmd globally..."
    dotnet tool install --global ilspycmd
}
else {
    Write-Host "ilspycmd is already installed."
}

# 2. Decompile
Write-Host "Decompiling $DllPath to sts2/..."
ilspycmd $DllPath -p -o sts2

# 3. Replace the generated project file with patched settings
Write-Host "Patching sts2/sts2.csproj..."
Set-Content -Path "sts2\sts2.csproj" -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>sts2</AssemblyName>
    <GenerateAssemblyInfo>False</GenerateAssemblyInfo>
    <TargetFramework>net9.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <AllowUnsafeBlocks>True</AllowUnsafeBlocks>
    <CheckForOverflowUnderflow>False</CheckForOverflowUnderflow>
  </PropertyGroup>
  <PropertyGroup />
  <ItemGroup>
    <Compile Remove="sts2_Annotator\**" />
    <EmbeddedResource Remove="sts2_Annotator\**" />
    <None Remove="sts2_Annotator\**" />
    <Compile Remove="sts2_Viewer\**" />
    <EmbeddedResource Remove="sts2_Viewer\**" />
    <None Remove="sts2_Viewer\**" />
  </ItemGroup>
  <ItemGroup>
    <None Include=".github\copilot-instructions.md" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="GodotSharp">
      <HintPath>GodotSharp.dll</HintPath>
    </Reference>
    <Reference Include="Steamworks.NET">
      <HintPath>Steamworks.NET.dll</HintPath>
    </Reference>
    <Reference Include="System.IO.Hashing">
      <HintPath>System.IO.Hashing.dll</HintPath>
    </Reference>
    <Reference Include="Sentry">
      <HintPath>Sentry.dll</HintPath>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>0Harmony.dll</HintPath>
    </Reference>
    <Reference Include="SmartFormat">
      <HintPath>SmartFormat.dll</HintPath>
    </Reference>
    <Reference Include="Vortice.DXGI">
      <HintPath>Vortice.DXGI.dll</HintPath>
    </Reference>
    <Reference Include="SharpGen.Runtime">
      <HintPath>SharpGen.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
'@

# 4. Add NuGet packages (from sts2.csproj PackageReference inventory)
Write-Host "Adding NuGet packages..."
dotnet add sts2\sts2.csproj package GodotSharp --version 4.6.2
dotnet add sts2\sts2.csproj package Godot.NET.Sdk --version 4.6.2
dotnet add sts2\sts2.csproj package Lib.Harmony --version 2.4.2
dotnet add sts2\sts2.csproj package Sentry --version 6.3.1
dotnet add sts2\sts2.csproj package SmartFormat --version 3.6.1
dotnet add sts2\sts2.csproj package Steamworks.NET --version 2024.8.0
dotnet add sts2\sts2.csproj package System.IO.Hashing --version 10.0.5
dotnet add sts2\sts2.csproj package Vortice.DXGI --version 3.8.3

Write-Host "Restoring packages..."
dotnet restore sts2\sts2.csproj

Write-Host ""
Write-Host "Done. Decompiled project is ready at sts2/."
