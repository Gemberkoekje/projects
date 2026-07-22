#!/usr/bin/env pwsh
# Runs the 2x2 matrix (analyzer version x SonarQubeIntegration) and prints the result.
# Expected: only 1.15.1 + SonarQubeIntegration=true fails with BLAZOR101.

$combos = @(
    @{ Ver = '1.15.0'; Sonar = 'false' },
    @{ Ver = '1.15.0'; Sonar = 'true'  },
    @{ Ver = '1.15.1'; Sonar = 'false' },
    @{ Ver = '1.15.1'; Sonar = 'true'  }
)

foreach ($c in $combos) {
    Remove-Item -Recurse -Force obj, bin -ErrorAction SilentlyContinue
    dotnet build -p:AnalyzerVersion=$($c.Ver) -p:SonarQubeIntegration=$($c.Sonar) --nologo -v q *> $null
    $status = if ($LASTEXITCODE -eq 0) { 'BUILD OK' } else { 'BUILD FAILED (BLAZOR101)' }
    '{0,-8} SonarQubeIntegration={1,-5} => {2}' -f $c.Ver, $c.Sonar, $status
}
