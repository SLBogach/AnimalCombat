param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

$expectedClasses = @(
    'Battle.Contracts.Events.RngProvenance',
    'Battle.Core.Random.GameplayRng',
    'Battle.Core.Random.Pcg32Stream',
    'Battle.Core.Random.SplitMix64'
)

$coverageFiles = @(
    Get-ChildItem `
        -LiteralPath $ResultsDirectory `
        -Recurse `
        -Filter 'coverage.cobertura.xml' `
        -File
)

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under '$ResultsDirectory'."
}

$classes = @(
    foreach ($coverageFile in $coverageFiles) {
        [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName -Raw
        $coverage.coverage.packages.package.classes.class
    }
)

$failures = @(
    foreach ($expectedClass in $expectedClasses) {
        $matches = @($classes | Where-Object { $_.name -eq $expectedClass })

        if ($matches.Count -ne 1) {
            "Expected one coverage entry for $expectedClass, found $($matches.Count)."
            continue
        }

        $branchRate = [decimal]::Parse(
            $matches[0].'branch-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
        $lineRate = [decimal]::Parse(
            $matches[0].'line-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)

        if ($branchRate -ne 1) {
            "$expectedClass branch coverage is $($branchRate * 100)%, expected 100%."
        }

        if ($lineRate -ne 1) {
            "$expectedClass line coverage is $($lineRate * 100)%, expected 100%."
        }
    }
)

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Output 'WP-03 line and branch coverage: 100%.'
