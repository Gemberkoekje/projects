<#
Lists Anthropic models available to the configured API key.
Search order for the key:
  1) Environment variable: ANTHROPIC_API_KEY
  2) sts2_Annotator/appconfig.json -> Llm.AnthropicApiKey
  3) dotnet user-secrets for the sts2_Annotator project (key name: Llm:AnthropicApiKey)

Usage:
  pwsh ./sts2_Annotator/scripts/list_anthropic_models.ps1
  # or from PowerShell (ExecutionPolicy may require Bypass):
  # powershell -ExecutionPolicy Bypass -File .\sts2_Annotator\scripts\list_anthropic_models.ps1
#>

$ErrorActionPreference = 'Stop'

function Get-AnthropicApiKey {
    # 1) environment
    if ($env:ANTHROPIC_API_KEY -and $env:ANTHROPIC_API_KEY.Trim().Length -gt 0) {
        return $env:ANTHROPIC_API_KEY.Trim()
    }

    # 2) appconfig.json next to the annotator project
    # Prefer $PSScriptRoot (set when the script is executed). Fallback to invocation path when available.
    if ($PSScriptRoot) {
        $scriptDir = $PSScriptRoot
    } elseif ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Path) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    } else {
        $scriptDir = (Get-Location).ProviderPath
    }

    $appConfigPath = Join-Path $scriptDir '..\appconfig.json'
    if (Test-Path $appConfigPath) { $appConfigPath = (Resolve-Path $appConfigPath).ProviderPath } else { $appConfigPath = $null }
    if ($appConfigPath) {
        try {
            $json = Get-Content -Raw -Path $appConfigPath | ConvertFrom-Json
            if ($json -and $json.Llm -and $json.Llm.AnthropicApiKey -and $json.Llm.AnthropicApiKey.Trim().Length -gt 0) {
                return $json.Llm.AnthropicApiKey.Trim()
            }
        } catch {
            # ignore and continue
        }
    }

    # 3) dotnet user-secrets (project-scoped). This requires dotnet SDK and user-secrets configured.
    $projPath = Join-Path $scriptDir '..\sts2_Annotator.csproj'
    if (Test-Path $projPath) {
        try {
            $output = dotnet user-secrets list --project $projPath 2>$null
            if ($LASTEXITCODE -eq 0 -and $output) {
                # output lines usually like: Llm:AnthropicApiKey   <value>
                foreach ($line in $output -split "`n") {
                    $trim = $line.Trim()
                    if ($trim -match "Llm:AnthropicApiKey\s+(.+)$") {
                        return $matches[1].Trim()
                    }
                    if ($trim -match "AnthropicApiKey\s+(.+)$") {
                        return $matches[1].Trim()
                    }
                }
            }
        } catch {
            # ignore
        }
    }

    return $null
}

$apiKey = Get-AnthropicApiKey
if (-not $apiKey) {
    Write-Error "Anthropic API key not found. Set ANTHROPIC_API_KEY, add Llm:AnthropicApiKey in sts2_Annotator/appconfig.json, or run 'dotnet user-secrets set \"Llm:AnthropicApiKey\" \"<key>\" --project .\sts2_Annotator\sts2_Annotator.csproj'"
    exit 2
}

$uri = 'https://api.anthropic.com/v1/models'
$headers = @{ 'x-api-key' = $apiKey }

try {
    Write-Host "Querying Anthropic models..."
    $resp = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -ErrorAction Stop
} catch {
    Write-Error "Request failed: $($_.Exception.Message)"
    if ($_.Exception.Response -ne $null) {
        try { $body = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream()).ReadToEnd(); Write-Host "Response body:"; Write-Host $body } catch { }
    }
    exit 3
}

# The API returns an object with `data` array where each item has `id` (model id) and metadata
if ($resp -and $resp.data) {
    $models = $resp.data
    Write-Host "Models available to this key:`n"
    $table = @()
    foreach ($m in $models) {
        $table += [PSCustomObject]@{
            Id = $m.id
            Description = ($m.description -as [string])
            Visibility = ($m.visibility -as [string])
        }
    }

    $table | Format-Table -AutoSize
    # Also write raw JSON to models.json for inspection
    $outPath = Join-Path $scriptDir 'anthropic_models.json'
    $resp | ConvertTo-Json -Depth 10 | Out-File -FilePath $outPath -Encoding UTF8
    Write-Host "\nFull response saved to: $outPath"
    exit 0
}

Write-Error "Unexpected response format from Anthropic API." 
exit 4
