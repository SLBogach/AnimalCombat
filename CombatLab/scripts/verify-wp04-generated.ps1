param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$combatLabRoot = Split-Path -Parent $PSScriptRoot
$temporaryParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ("combatlab-wp04-" + [Guid]::NewGuid().ToString("N"))
$temporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)

if (-not $temporaryRoot.StartsWith($temporaryParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a temporary directory outside the OS temp root."
}

New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    dotnet run `
        --project (Join-Path $combatLabRoot "src/CombatLab.Cli/CombatLab.Cli.csproj") `
        --configuration $Configuration `
        --no-build `
        --no-restore `
        -- export-config --output $temporaryRoot

    if ($LASTEXITCODE -ne 0) {
        throw "WP-04 export failed with exit code $LASTEXITCODE."
    }

    $generatedRoot = Join-Path $combatLabRoot "config/generated"
    foreach ($name in @(
        "combat.balance.v0.1.json",
        "combat.balance.v0.1.map.csv",
        "combat.balance.v0.1.validation.json"
    )) {
        $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $generatedRoot $name)).Hash
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $temporaryRoot $name)).Hash
        if ($expectedHash -cne $actualHash) {
            throw "Generated artifact is stale: $name"
        }
    }

    $expectedManifestPath = Join-Path $generatedRoot "combat.balance.v0.1.manifest.json"
    $actualManifestPath = Join-Path $temporaryRoot "combat.balance.v0.1.manifest.json"
    $expectedManifest = Get-Content -Raw -LiteralPath $expectedManifestPath | ConvertFrom-Json
    $actualManifest = Get-Content -Raw -LiteralPath $actualManifestPath | ConvertFrom-Json
    $expectedManifest.generated_utc = $null
    $actualManifest.generated_utc = $null
    $expectedNormalized = $expectedManifest | ConvertTo-Json -Depth 16 -Compress
    $actualNormalized = $actualManifest | ConvertTo-Json -Depth 16 -Compress
    if ($expectedNormalized -cne $actualNormalized) {
        throw "Generated artifact is stale: combat.balance.v0.1.manifest.json"
    }

    Push-Location $combatLabRoot
    try {
        git diff --exit-code -- schemas/balance/v0.1/combat.balance.schema.json
        if ($LASTEXITCODE -ne 0) {
            throw "Generated balance schema is stale."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
