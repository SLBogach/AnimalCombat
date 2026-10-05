param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$combatLabRoot = Split-Path -Parent $PSScriptRoot
$cliWp10 = Join-Path $combatLabRoot "src/CombatLab.Cli/bin/$Configuration/net10.0/CombatLab.Cli.dll"
$sourceWp10 = Join-Path $combatLabRoot "config/source/Combat_Balance_Workbook_v0.1.xlsx"
$workbookWp10 = Join-Path $combatLabRoot "config/source/Combat_Balance_Workbook_v0.2.xlsx"
$tempParentWp10 = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRootWp10 = [IO.Path]::GetFullPath((Join-Path $tempParentWp10 ("combatlab-wp10-" + [Guid]::NewGuid().ToString("N"))))
$requiredPrefixWp10 = $tempParentWp10.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempRootWp10.StartsWith($requiredPrefixWp10, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing a temporary path outside the OS temp directory."
}
New-Item -ItemType Directory -Path $tempRootWp10 | Out-Null

try {
    if ((Get-FileHash -LiteralPath $sourceWp10 -Algorithm SHA256).Hash.ToLowerInvariant() -cne "bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2") {
        throw "Historical v0.1 source workbook changed."
    }
    # New processes, no source/schema/generated writes in the working tree.
    foreach ($iterationWp10 in 1..2) {
        $migratedWp10 = Join-Path $tempRootWp10 "migrated-$iterationWp10.xlsx"
        dotnet $cliWp10 migrate-config --workbook $sourceWp10 --output $migratedWp10
        if ($LASTEXITCODE -ne 0) { throw "Native XLSX migration failed: $LASTEXITCODE" }
        if ((Get-FileHash -LiteralPath $workbookWp10 -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath $migratedWp10 -Algorithm SHA256).Hash) {
            throw "Native migration does not reproduce committed v0.2 workbook."
        }
        $exportWp10 = Join-Path $tempRootWp10 "export-$iterationWp10"
        $schemaWp10 = Join-Path $exportWp10 "combat.balance.schema.json"
        dotnet $cliWp10 export-config --workbook $migratedWp10 --output $exportWp10 --schema-output $schemaWp10
        if ($LASTEXITCODE -ne 0) { throw "WP-10 export failed: $LASTEXITCODE" }
        foreach ($nameWp10 in @("combat.balance.v0.2.json", "combat.balance.v0.2.map.csv", "combat.balance.v0.2.validation.json")) {
            if ((Get-FileHash -LiteralPath (Join-Path $combatLabRoot "config/generated/$nameWp10") -Algorithm SHA256).Hash -cne
                (Get-FileHash -LiteralPath (Join-Path $exportWp10 $nameWp10) -Algorithm SHA256).Hash) {
                throw "Generated v0.2 artifact is stale: $nameWp10"
            }
        }
        if ((Get-FileHash -LiteralPath (Join-Path $combatLabRoot "schemas/balance/v0.2/combat.balance.schema.json") -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $schemaWp10 -Algorithm SHA256).Hash) { throw "Generated v0.2 schema is stale." }
        $expectedManifestWp10 = Get-Content -LiteralPath (Join-Path $combatLabRoot "config/generated/combat.balance.v0.2.manifest.json") -Raw | ConvertFrom-Json
        $actualManifestWp10 = Get-Content -LiteralPath (Join-Path $exportWp10 "combat.balance.v0.2.manifest.json") -Raw | ConvertFrom-Json
        $expectedManifestWp10.generated_utc = $null
        $actualManifestWp10.generated_utc = $null
        if (($expectedManifestWp10 | ConvertTo-Json -Depth 16 -Compress) -cne ($actualManifestWp10 | ConvertTo-Json -Depth 16 -Compress)) {
            throw "Generated v0.2 manifest differs beyond generated_utc."
        }
        $validationWp10 = Get-Content -LiteralPath (Join-Path $exportWp10 "combat.balance.v0.2.validation.json") -Raw | ConvertFrom-Json
        if ($validationWp10.error_count -ne 0 -or $validationWp10.warning_count -ne 0) { throw "v0.2 requires 0 errors / 0 warnings." }
        dotnet $cliWp10 validate-config --config (Join-Path $exportWp10 "combat.balance.v0.2.json") --manifest (Join-Path $exportWp10 "combat.balance.v0.2.manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "WP-10 config/manifest validation failed: $LASTEXITCODE" }
    }
    Write-Output "WP-10 native migration / generated v0.2 reproducibility gate passed (two fresh processes)."
}
finally {
    $cleanupWp10 = [IO.Path]::GetFullPath($tempRootWp10)
    if (-not $cleanupWp10.StartsWith($requiredPrefixWp10, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup target." }
    if (Test-Path -LiteralPath $cleanupWp10) { Remove-Item -LiteralPath $cleanupWp10 -Recurse -Force }
}
