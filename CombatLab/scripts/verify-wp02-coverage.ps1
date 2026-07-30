param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

$expectedClasses = @(
    'Battle.Core.Math.FixedMath',
    'Battle.Core.Math.FixedMath/CanonicalModifierComparer',
    'Battle.Core.Math.Modifier',
    'Battle.Core.Outcome.TimeoutHealthComparer'
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

        if ($branchRate -ne 1) {
            "$expectedClass branch coverage is $($branchRate * 100)%, expected 100%."
        }
    }
)

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Output 'WP-02 branch coverage: 100%.'
