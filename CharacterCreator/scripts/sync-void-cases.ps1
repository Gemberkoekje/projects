<#
.SYNOPSIS
    Copies VOID's shared skill rule cases into the Character Creator's specs (DESIGN.md §3.5).

.DESCRIPTION
    tests/data/skill-rules-cases.json in VOID holds the rules both engines must agree on. This copies
    it to CharacterCreator.test/Rules/skill-rules-cases.json and records, next to it, which VOID commit
    it came from and the SHA-256 of its text with LF line endings. VoidCases_specs fails when the copy
    no longer matches that hash, so the copy is only ever changed by running this script.

.PARAMETER VoidRepo
    A VOID checkout at the commit to copy from.

.EXAMPLE
    ./scripts/sync-void-cases.ps1 -VoidRepo C:\git\gitlabs\void-charactercreator
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $VoidRepo
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $VoidRepo 'tests/data/skill-rules-cases.json'
if (-not (Test-Path $source)) {
    throw "No shared cases at $source; is this a VOID checkout with them?"
}

$commit = (git -C $VoidRepo rev-parse HEAD).Trim()
$dirty = git -C $VoidRepo status --porcelain -- tests/data/skill-rules-cases.json
if ($dirty) {
    throw "tests/data/skill-rules-cases.json has uncommitted changes in $VoidRepo; commit them first, so the copy names a real commit."
}

$text = [System.IO.File]::ReadAllText($source) -replace "`r`n", "`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
$hash = ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''

$target = Join-Path $PSScriptRoot '../CharacterCreator.test/Rules'
New-Item -ItemType Directory -Force $target | Out-Null
[System.IO.File]::WriteAllText((Join-Path $target 'skill-rules-cases.json'), $text, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    (Join-Path $target 'skill-rules-cases.source'),
    "repository=vortexadventures/void`nfile=tests/data/skill-rules-cases.json`ncommit=$commit`nsha256=$hash`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Copied the shared cases from VOID $commit (sha256 $hash)."
