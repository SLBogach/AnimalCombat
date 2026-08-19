param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$combatLabRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$temporaryParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = Join-Path `
    $temporaryParent `
    ("combatlab-wp07-targets-" + [Guid]::NewGuid().ToString("N"))
$temporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)

if (-not $temporaryRoot.StartsWith(
        $temporaryParent,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a temporary directory outside the OS temp root."
}

$probeProject = Join-Path $combatLabRoot "tools/Wp06.TargetProbe/Wp06.TargetProbe.csproj"
$assemblies = @(
    "Battle.Contracts",
    "Battle.Core",
    "Battle.Config",
    "Battle.Replay"
)
$targets = @("netstandard2.1", "net10.0")
$results = @{}
$historicalFixtureSha256 = "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

function Test-ByteArrayEqual {
    param(
        [byte[]]$Left,
        [byte[]]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }

    return $true
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

try {
    dotnet restore $probeProject --locked-mode --disable-build-servers
    if ($LASTEXITCODE -ne 0) {
        throw "WP-07 target probe restore failed with exit code $LASTEXITCODE."
    }

    $fixturePath = Join-Path `
        $combatLabRoot `
        "fixtures/replay/v0.1/approach-band-l3.engine-0.2.0.json"
    if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
        throw "Missing pinned WP-07 replay fixture '$fixturePath'."
    }

    $fixtureHash = Get-Sha256Hex -Path $fixturePath
    if (-not [System.StringComparer]::Ordinal.Equals(
            $fixtureHash,
            $historicalFixtureSha256)) {
        throw "Historical WP-07 fixture changed: expected $historicalFixtureSha256, actual $fixtureHash."
    }

    foreach ($target in $targets) {
        foreach ($assembly in $assemblies) {
            $assemblyPath = Join-Path `
                $combatLabRoot `
                "src/$assembly/bin/$Configuration/$target/$assembly.dll"
            if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
                throw "Missing $target assembly '$assemblyPath'. Build CombatLab.sln first."
            }
        }
    }

    foreach ($target in $targets) {
        $outputDirectory = Join-Path $temporaryRoot $target
        dotnet build `
            $probeProject `
            --configuration $Configuration `
            --no-restore `
            --disable-build-servers `
            --output $outputDirectory `
            --property:CombatTarget=$target `
            /nodeReuse:false
        if ($LASTEXITCODE -ne 0) {
            throw "WP-07 $target probe build failed with exit code $LASTEXITCODE."
        }

        $probeAssembly = Join-Path $outputDirectory "Wp06.TargetProbe.dll"
        $resultPath = Join-Path $temporaryRoot ($target + ".replay.json")
        & dotnet $probeAssembly $combatLabRoot $target approach $resultPath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "WP-07 $target probe execution failed with exit code $LASTEXITCODE."
        }

        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw "WP-07 $target probe produced no canonical replay file."
        }

        $results[$target] = [System.IO.File]::ReadAllBytes($resultPath)
    }

    if (-not (Test-ByteArrayEqual `
            $results["netstandard2.1"] `
            $results["net10.0"])) {
        throw "WP-07 target determinism failed: netstandard2.1 and net10.0 replay bytes differ."
    }

    Write-Output `
        "WP-07 target determinism: current approach output matches across targets; historical battle.core/0.2.0 fixture hash is immutable."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
