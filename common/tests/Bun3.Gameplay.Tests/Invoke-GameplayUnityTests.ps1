param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Import', 'EditMode', 'Player')]
  [string]$Mode,
  [string]$TestFilter,
  [switch]$AllEditMode,
  [ValidateSet('Mono', 'IL2CPP')]
  [string]$Backend = 'Mono'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameplayUnityTestSupport.ps1')
$unityEditor = 'E:\Unitys\6000.3.14f1\Editor\Unity.exe'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$unityProject = (Resolve-Path (Join-Path $repoRoot 'unity')).Path
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$logPath = Join-Path $env:TEMP "bun3-gameplay-$($Mode.ToLowerInvariant())-$stamp.log"

function Invoke-Unity([string[]]$Arguments) {
  $startInfo = New-Object System.Diagnostics.ProcessStartInfo
  $startInfo.FileName = $unityEditor
  $startInfo.Arguments = ConvertTo-WindowsCommandLine -Arguments $Arguments
  $startInfo.UseShellExecute = $false
  $unityProcess = [System.Diagnostics.Process]::Start($startInfo)
  $unityProcess.WaitForExit()
  $unityExit = $unityProcess.ExitCode
  & git -C $repoRoot status --short
  if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect repository state after Unity.' }
  if ($unityExit -ne 0) { throw "Unity $Mode failed with exit code $unityExit." }
  if (Select-String -LiteralPath $logPath -Pattern 'warning CS|error CS|Compilation failed' -Quiet) {
    throw "Unity $Mode emitted a C# diagnostic: $logPath"
  }
}

if ($Mode -eq 'Import') {
  Invoke-Unity @('-batchmode', '-quit', '-projectPath', $unityProject, '-logFile', $logPath)
  return
}

$resultPath = Join-Path $env:TEMP "bun3-gameplay-$($Mode.ToLowerInvariant())-$stamp.xml"
if ($Mode -eq 'EditMode') {
  $arguments = @(
    '-batchmode', '-projectPath', $unityProject,
    '-runTests', '-testPlatform', 'EditMode',
    '-testResults', $resultPath, '-logFile', $logPath)
  if (-not $AllEditMode) { $arguments += @('-assemblyNames', 'Bun3.Gameplay.Unity.Tests') }
  if ($TestFilter) { $arguments += @('-testFilter', $TestFilter) }
  Invoke-Unity $arguments
} else {
  $backendLower = $Backend.ToLowerInvariant()
  $settingsPath = Join-Path $repoRoot `
    "common/src/com.bun3.gameplay/Tests/Runtime/TagTests.$Backend.json"
  $buildDirectory = Join-Path $env:TEMP "bun3-tags-$backendLower-$stamp"
  $null = New-Item -ItemType Directory -Path $buildDirectory
  $buildPath = Join-Path $buildDirectory 'Bun3TagsTests.exe'
  Invoke-Unity @(
    '-batchmode', '-projectPath', $unityProject,
    '-runTests', '-testPlatform', 'StandaloneWindows64',
    '-assemblyNames', 'Bun3.Gameplay.Runtime.Tests',
    '-testSettingsFile', (Resolve-Path $settingsPath).Path,
    '-playerHeartbeatTimeout', '7200',
    '-buildPlayerPath', $buildPath,
    '-testResults', $resultPath, '-logFile', $logPath)
}

if (-not (Test-Path -LiteralPath $resultPath)) { throw "Unity result XML missing: $resultPath" }
[xml]$results = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath
if ([int]$results.'test-run'.testcasecount -eq 0 `
  -or [int]$results.'test-run'.failed -ne 0 `
  -or [int]$results.'test-run'.inconclusive -ne 0) {
  throw "Unity $Mode failed or discovered zero tests: $resultPath"
}
# 배치모드에서 검증할 수 없는 테스트(시스템 클립보드 등)는 Assert.Ignore로 빠진다. 실패는
# 위에서 이미 막았으므로 여기서는 무엇이 빠졌는지 드러내기만 한다 — 조용한 skip 증식 방지.
$ignored = $results.SelectNodes("//test-case[@result='Skipped']")
if ($ignored.Count -ne 0) {
  Write-Host "Ignored $($ignored.Count) test(s):"
  foreach ($case in $ignored) {
    Write-Host "  - $($case.fullname)"
  }
}
if (-not (Select-String -LiteralPath $logPath -Pattern 'Run completed' -Quiet)) {
  throw "Unity Test Runner did not report completion: $logPath"
}
if ($Mode -eq 'Player') {
  & (Join-Path $PSScriptRoot 'Assert-TagPerformance.ps1') `
    -ResultPath $resultPath -ExpectedBackend $Backend
}
[pscustomobject]@{ ResultPath = $resultPath; LogPath = $logPath }
