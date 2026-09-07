param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$combatLabRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$temporaryParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = [System.IO.Path]::GetFullPath((Join-Path `
    $temporaryParent `
    ("combatlab-wp09-targets-" + [Guid]::NewGuid().ToString("N"))))
if (-not $temporaryRoot.StartsWith($temporaryParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a temporary directory outside the OS temp root."
}

$probeProject = Join-Path $combatLabRoot "tools/Wp06.TargetProbe/Wp06.TargetProbe.csproj"
$assemblies = @("Battle.Contracts", "Battle.Core", "Battle.Config", "Battle.Replay")
$targets = @("netstandard2.1", "net10.0")
$currentScenarios = @(
    @{ Name = "wait"; Fixture = "fixtures/replay/v0.1/wait-equal-l1.engine-0.4.0.json" },
    @{ Name = "decision"; Fixture = "fixtures/replay/v0.1/decision-weighted-l1.engine-0.4.0.json" },
    @{ Name = "resolution-basic"; Fixture = "fixtures/replay/v0.1/resolution-basic-l1.engine-0.4.0.json" },
    @{ Name = "resolution-double-ko"; Fixture = "fixtures/replay/v0.1/resolution-double-ko-l1.engine-0.4.0.json" },
    @{ Name = "resolution-wall-grab"; Fixture = "fixtures/replay/v0.1/resolution-wall-grab-l1.engine-0.4.0.json" }
)
$historicalFixtures = @(
    @{ Path = "fixtures/replay/v0.1/wait-equal-l1.engine-0.1.0.json"; Sha256 = "4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292" },
    @{ Path = "fixtures/replay/v0.1/wait-equal-l1.engine-0.2.0.json"; Sha256 = "ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409" },
    @{ Path = "fixtures/replay/v0.1/approach-band-l3.engine-0.2.0.json"; Sha256 = "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873" },
    @{ Path = "fixtures/replay/v0.1/wait-equal-l1.engine-0.3.0.json"; Sha256 = "8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd" },
    @{ Path = "fixtures/replay/v0.1/decision-weighted-l1.engine-0.3.0.json"; Sha256 = "1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0" }
)
$results = @{}

function Test-ByteArrayEqual {
    param([byte[]]$Left, [byte[]]$Right)
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    foreach ($historical in $historicalFixtures) {
        $path = Join-Path $combatLabRoot $historical.Path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing historical replay fixture '$path'."
        }
        $actualHash = Get-Sha256Hex -Path $path
        if (-not [System.StringComparer]::Ordinal.Equals($actualHash, $historical.Sha256)) {
            throw "Historical fixture '$path' changed: expected $($historical.Sha256), actual $actualHash."
        }
    }

    $fixtureBytes = @{}
    foreach ($scenario in $currentScenarios) {
        $path = Join-Path $combatLabRoot $scenario.Fixture
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing WP-09 replay fixture '$path'."
        }
        $fixtureBytes[$scenario.Name] = [System.IO.File]::ReadAllBytes($path)
        $results[$scenario.Name] = @{}
    }

    dotnet restore $probeProject --locked-mode --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw "WP-09 target probe restore failed with exit code $LASTEXITCODE." }

    foreach ($target in $targets) {
        foreach ($assembly in $assemblies) {
            $assemblyPath = Join-Path $combatLabRoot "src/$assembly/bin/$Configuration/$target/$assembly.dll"
            if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
                throw "Missing $target assembly '$assemblyPath'. Build CombatLab.sln first."
            }
        }

        $outputDirectory = Join-Path $temporaryRoot $target
        dotnet build $probeProject --configuration $Configuration --no-restore --disable-build-servers `
            --output $outputDirectory --property:CombatTarget=$target /nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw "WP-09 $target probe build failed with exit code $LASTEXITCODE." }

        $probeAssembly = Join-Path $outputDirectory "Wp06.TargetProbe.dll"
        foreach ($scenario in $currentScenarios) {
            $resultPath = Join-Path $temporaryRoot ($target + "." + $scenario.Name + ".replay.json")
            & dotnet $probeAssembly $combatLabRoot $target $scenario.Name $resultPath | Out-Null
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
                throw "WP-09 $target/$($scenario.Name) probe failed."
            }
            $actualBytes = [System.IO.File]::ReadAllBytes($resultPath)
            $results[$scenario.Name][$target] = $actualBytes
            if (-not (Test-ByteArrayEqual $actualBytes $fixtureBytes[$scenario.Name])) {
                throw "WP-09 $target/$($scenario.Name) replay differs from its pinned Engine 0.4.0 fixture."
            }
        }
    }

    foreach ($scenario in $currentScenarios) {
        if (-not (Test-ByteArrayEqual `
                $results[$scenario.Name]["netstandard2.1"] `
                $results[$scenario.Name]["net10.0"])) {
            throw "WP-09 target determinism failed for $($scenario.Name)."
        }
    }

    Write-Output "WP-09 target determinism: five Engine 0.4.0 fixtures match across netstandard2.1/net10.0; historical hashes are immutable."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
