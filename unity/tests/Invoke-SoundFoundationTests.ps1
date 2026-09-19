[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ProjectPath,
    [Parameter(Mandatory = $true)] [string] $UnityPath,
    [Parameter(Mandatory = $true)] [string] $ResultsDirectory,
    [ValidateRange(1, 120)] [int] $TimeoutMinutes = 15,
    [switch] $IncludeExtensions,
    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-NativeArgument([string] $Value) {
    if ($Value.Contains([string][char]0) -or $Value.Contains("`r") -or $Value.Contains("`n")) {
        throw 'Native arguments cannot contain NUL or line breaks.'
    }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Get-SafeFiles([string] $Root) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Validation paths cannot contain reparse points: $($directory.FullName)"
        }
        foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Validation paths cannot contain reparse points: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else { $item }
        }
    }
}

function Copy-ExtensionTests([string] $Project, $Manifest) {
    $assets = (Resolve-Path -LiteralPath (Join-Path $Project 'Assets')).ProviderPath
    $owned = [IO.Path]::GetFullPath((Join-Path $assets 'Bun3SoundFoundationValidation'))
    $assetsPrefix = $assets.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    if (-not $owned.StartsWith($assetsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The test-copy directory must be strictly inside the resolved project Assets directory.'
    }
    if (((Get-Item -LiteralPath $Project -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The validation project cannot be a reparse point.'
    }
    $markerName = '.bun3-sound-foundation-tests.json'
    $ownerId = 'Bun3.SoundFoundation.ExtensionTests.v1'
    if (Test-Path -LiteralPath $owned) {
        # Validate the entire existing tree before reading its ownership marker or deleting anything.
        $null = @(Get-SafeFiles $owned)
        $markerPath = Join-Path $owned $markerName
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            throw "Refusing to replace an unowned directory: $owned"
        }
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($marker.Owner -ne $ownerId -or $marker.Project -ne $Project) {
            throw "The existing test-copy ownership marker does not match this project: $owned"
        }
    }

    $packagesRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../Packages')).ProviderPath
    $copies = @()
    $assemblyNames = @()
    foreach ($package in @('com.bun3.unity.audio.dissonance.netcode', 'com.bun3.unity.audio.dissonance.steamaudio', 'com.bun3.unity.acoustics')) {
        $dependency = $Manifest.dependencies.PSObject.Properties[$package]
        if ($null -eq $dependency) { throw "Install the optional dependency before running extensions: $package" }
        if ($Manifest.testables -contains $package) {
            throw "Do not combine copied extension tests with a testables entry for $package. Use a validation host without that entry."
        }
        $sourcePackage = (Resolve-Path -LiteralPath (Join-Path $packagesRoot $package)).ProviderPath
        $dependencyValue = [string]$dependency.Value
        if (-not $dependencyValue.StartsWith('file:', [StringComparison]::Ordinal)) {
            throw "Extension test copies require a local file: dependency matching this checkout: $package"
        }
        $dependencyPath = $dependencyValue.Substring(5)
        if (-not [IO.Path]::IsPathRooted($dependencyPath)) { $dependencyPath = Join-Path (Join-Path $Project 'Packages') $dependencyPath }
        $resolvedDependency = (Resolve-Path -LiteralPath $dependencyPath).ProviderPath
        if (-not $resolvedDependency.Equals($sourcePackage, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The installed extension path must match the test source checkout: $package"
        }
        $source = (Resolve-Path -LiteralPath (Join-Path $sourcePackage 'Tests')).ProviderPath
        $files = @(Get-SafeFiles $source | Where-Object { $_.Extension -ne '.meta' })
        $definitions = @($files | Where-Object { $_.Extension -eq '.asmdef' })
        if ($definitions.Count -eq 0) { throw "No extension test assembly definitions found: $source" }
        foreach ($definition in $definitions) {
            $assembly = Get-Content -LiteralPath $definition.FullName -Raw | ConvertFrom-Json
            $assemblyNames += [string]$assembly.name
        }
        $copies += @{ Package = $package; Source = $source; Files = $files }
    }
    if ($null -eq $Manifest.dependencies.PSObject.Properties['com.unity.netcode.gameobjects']) {
        throw 'Install com.unity.netcode.gameobjects before preparing extension tests.'
    }
    $assetFiles = @(Get-SafeFiles $assets)
    $sdkNames = @('DissonanceVoip', 'Dissonance.Integrations.UnityNfgo', 'SteamAudioUnity')
    $foundSdkNames = @()
    $ownedPrefix = $owned + [IO.Path]::DirectorySeparatorChar
    foreach ($file in $assetFiles) {
        if ($file.Extension -ne '.asmdef' -or $file.FullName.StartsWith($ownedPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $name = [string]$definition.name
        if ($sdkNames -contains $name) { $foundSdkNames += $name }
        if ($assemblyNames -contains $name -or $assemblyNames -contains ($name -replace '\.ValidationHost$', '')) {
            throw "Another extension test assembly already exists at $($file.FullName). Use a fresh host or remove that copy yourself; the runner will not touch it."
        }
    }
    foreach ($sdk in $sdkNames) {
        if ($foundSdkNames -notcontains $sdk) { throw "The validation Assets must contain the installed SDK assembly: $sdk" }
    }
    $settings = Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings/ProjectSettings.asset') -Raw
    if ($settings -notmatch '(?m)^\s+Standalone:\s*[^\r\n]*\bSTEAMAUDIO_ENABLED\b') {
        throw 'Enable STEAMAUDIO_ENABLED for Standalone before running extension tests.'
    }

    # All preflight checks precede mutation. Only this marker-owned, checked Assets subtree is replaced.
    if (Test-Path -LiteralPath $owned) {
        if (-not [IO.Path]::GetFullPath($owned).StartsWith($assetsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to delete a test-copy path outside the resolved Assets directory.'
        }
        $null = @(Get-SafeFiles $owned)
        Remove-Item -LiteralPath $owned -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($owned) | Out-Null
    @{ Owner = $ownerId; Project = $Project } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $owned $markerName) -Encoding UTF8
    foreach ($copy in $copies) {
        foreach ($file in $copy.Files) {
            $relative = $file.FullName.Substring($copy.Source.Length).TrimStart([char[]]'\/')
            $destination = Join-Path (Join-Path $owned $copy.Package) $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            if ($file.Extension -eq '.asmdef') {
                $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                $definition.name = [string]$definition.name + '.ValidationHost'
                $destination = [IO.Path]::ChangeExtension($destination, '.ValidationHost.asmdef')
                [IO.File]::WriteAllText($destination, ($definition | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding $false))
            }
            else { Copy-Item -LiteralPath $file.FullName -Destination $destination }
        }
    }
    Write-Host "Prepared extension tests in $owned. Unity generates new metadata on import."
}

$project = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
$unity = (Resolve-Path -LiteralPath $UnityPath).ProviderPath
if (-not (Test-Path -LiteralPath $project -PathType Container)) { throw 'ProjectPath must be a directory.' }
if (-not (Test-Path -LiteralPath $unity -PathType Leaf)) { throw 'UnityPath must be an executable file.' }
$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($PrepareOnly -and -not $IncludeExtensions) { throw 'PrepareOnly requires IncludeExtensions.' }
foreach ($package in @('com.bun3.unity.audio', 'com.bun3.unity.sound-events',
    'com.bun3.unity.audio.dissonance', 'com.bun3.unity.audio.steamaudio')) {
    if ($null -eq $manifest.dependencies.PSObject.Properties[$package] -or $manifest.testables -notcontains $package) {
        throw "The validation project must reference $package and include it in testables."
    }
}
$settings = Get-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectSettings.asset') -Raw
$requiredSymbols = @('BUN3_DISSONANCE', 'BUN3_STEAMAUDIO', 'STEAMAUDIO_ENABLED')
if ($IncludeExtensions) { $requiredSymbols += 'BUN3_DISSONANCE_NFGO' }
foreach ($symbol in $requiredSymbols) {
    if ($settings -notmatch ('(?m)^\s+Standalone:\s*[^\r\n]*\b' + [regex]::Escape($symbol) + '\b')) {
        throw "Missing Standalone symbol $symbol. Import SDKs, then run Tools > Bun3 > Audio > Sync Installed Adapters before validation."
    }
}
if ($IncludeExtensions) { Copy-ExtensionTests $project $manifest }
if ($PrepareOnly) { return }

$results = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultsDirectory)
[System.IO.Directory]::CreateDirectory($results) | Out-Null
$runDirectory = Join-Path $results ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($runDirectory) | Out-Null

$runs = @(
    @{ Mode = 'EditMode'; Filter = 'Bun3.Unity.Audio.Dissonance.Tests;Bun3.Unity.Audio.SteamAudio.Tests'; Required = @('Bun3.Unity.Audio.Dissonance.Tests.', 'Bun3.Unity.Audio.SteamAudio.Tests.'); RequiredCounts = @{} },
    @{ Mode = 'PlayMode'; Filter = 'Bun3.Unity.Audio.Tests.ExternalAudioRegistryTests;Bun3.Unity.Audio.Tests.SoundSystemReentrancyTests;Bun3.Unity.Audio.Tests.SourceCreatedHookTests;Bun3.Unity.SoundEvents.Tests;Bun3.Unity.Audio.Dissonance.Tests;Bun3.Unity.Audio.SteamAudio.Tests'; Required = @('Bun3.Unity.Audio.Dissonance.Tests.', 'Bun3.Unity.Audio.SteamAudio.Tests.'); RequiredCounts = @{} }
)
if ($IncludeExtensions) {
    $runs[0].Filter += ';Bun3.Unity.Audio.Dissonance.Netcode.Tests;Bun3.Unity.Acoustics.Tests'
    $runs[0].Required += @('Bun3.Unity.Audio.Dissonance.Netcode.Tests.DissonanceNfgoBindingTests.',
        'Bun3.Unity.Acoustics.Tests.AcousticGridTests.', 'Bun3.Unity.Acoustics.Tests.AcousticGridQueryTests.')
    $runs[1].Filter += ';Bun3.Unity.Audio.Dissonance.Netcode.Tests;Bun3.Unity.Audio.Dissonance.SteamAudio.Tests;Bun3.Unity.Audio.Tests.SoundVoiceOutputTests'
    $runs[1].RequiredCounts['Bun3.Unity.Audio.Tests.SoundVoiceOutputTests.'] = 11
    $runs[1].Required += @('Bun3.Unity.Audio.Dissonance.Netcode.Tests.PlayMode.',
        'Bun3.Unity.Audio.Dissonance.SteamAudio.Tests.DissonancePathPlaybackTests.',
        'Bun3.Unity.Audio.Dissonance.SteamAudio.Tests.DissonanceSteamAudioPlaybackTests.')
}

foreach ($run in $runs) {
    $xmlPath = Join-Path $runDirectory ($run.Mode + '.xml')
    $logPath = Join-Path $runDirectory ($run.Mode + '.log')
    $arguments = @('-batchmode', '-nographics', '-projectPath', $project, '-runTests',
        '-testPlatform', $run.Mode, '-testFilter', $run.Filter,
        '-testResults', $xmlPath, '-logFile', $logPath)
    $commandLine = ($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
    Write-Host "Running $($run.Mode). Log: $logPath"
    $process = Start-Process -FilePath $unity -ArgumentList $commandLine -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutMinutes * 60000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "$($run.Mode) exceeded $TimeoutMinutes minutes. See $logPath"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "$($run.Mode) exited with code $($process.ExitCode). See $logPath" }
    if (-not (Test-Path -LiteralPath $xmlPath -PathType Leaf)) { throw "$($run.Mode) produced no test report. See $logPath" }
    [xml] $report = Get-Content -LiteralPath $xmlPath -Raw
    $summary = $report.'test-run'
    if ($null -eq $summary -or [int]$summary.total -le 0 -or
        [int]$summary.failed -ne 0 -or [int]$summary.passed -ne [int]$summary.total -or
        $summary.result -ne 'Passed') {
        throw "$($run.Mode) did not pass every selected test. See $xmlPath"
    }
    foreach ($prefix in $run.Required) {
        $found = @($report.SelectNodes('//test-case') | Where-Object { ([string]$_.fullname).StartsWith($prefix, [StringComparison]::Ordinal) })
        if ($found.Count -eq 0) { throw "Expected extension tests were not discovered: $prefix See $xmlPath" }
    }
    foreach ($entry in $run.RequiredCounts.GetEnumerator()) {
        $found = @($report.SelectNodes('//test-case') | Where-Object { ([string]$_.fullname).StartsWith([string]$entry.Key, [StringComparison]::Ordinal) })
        if ($found.Count -lt $entry.Value) {
            throw "Expected at least $($entry.Value) tests for $($entry.Key), discovered $($found.Count). See $xmlPath"
        }
    }
    Write-Host "$($run.Mode): $($summary.passed)/$($summary.total) passed."
}
Write-Host "Sound foundation reports: $runDirectory"
