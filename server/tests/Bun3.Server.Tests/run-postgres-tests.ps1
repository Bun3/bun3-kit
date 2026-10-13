param([string]$Image = 'postgres:18')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path $repoRoot "server/artifacts/test-results/postgres/$runId"
New-Item -ItemType Directory -Path $results -Force | Out-Null
$envFile = Join-Path $results 'postgres.env'
$containerId = $null
$previousConnection = $env:BUN3_POSTGRES_TEST_CONNECTION
$password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
try {
    $engineVersion = docker info --format '{{.ServerVersion}}'
    if ($LASTEXITCODE -ne 0 -or $engineVersion -notmatch '^\d+\.\d+') { throw 'Docker engine is unavailable. No PostgreSQL tests ran.' }
    [IO.File]::WriteAllLines($envFile, @('POSTGRES_USER=bun3_test', 'POSTGRES_DB=bun3_authority_tests', "POSTGRES_PASSWORD=$password"))
    $started = docker run --detach --label "bun3.authority-test=$runId" --publish '127.0.0.1::5432' --env-file $envFile --tmpfs '/var/lib/postgresql:rw' $Image
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated PostgreSQL container.' }
    $containerId = ($started | Select-Object -Last 1).Trim()
    if ($containerId -notmatch '^[a-f0-9]{64}$') { throw 'Docker did not return a valid container ID.' }
    $portMapping = docker port $containerId '5432/tcp'
    if ($LASTEXITCODE -ne 0 -or $portMapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Test database is not bound exclusively to loopback.' }
    $port = [int]$Matches[1]
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        docker exec $containerId pg_isready -U bun3_test -d bun3_authority_tests *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'PostgreSQL readiness timed out. No tests ran.' }
    $imageId = docker inspect --format '{{.Image}}' $containerId
    $digest = docker image inspect --format '{{json .RepoDigests}}' $imageId
    $version = docker exec $containerId psql -U bun3_test -d bun3_authority_tests -Atc 'SELECT version()'
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the test server version.' }
    @{ ContainerId = $containerId; RunId = $runId; Image = $Image; ImageId = $imageId; Digest = $digest; Version = $version } |
        ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $results 'environment.json')
    Write-Output "PostgreSQL test server: $version"
    Write-Output "Image digest: $digest"
    $env:BUN3_POSTGRES_TEST_CONNECTION = "Host=127.0.0.1;Port=$port;Database=bun3_authority_tests;Username=bun3_test;Password=$password;Pooling=false"
    dotnet test (Join-Path $repoRoot 'server/tests/Bun3.Server.Tests') -c Release --filter 'TestCategory=PostgreSQL' --logger 'trx;LogFileName=postgres.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL integration tests failed.' }
    [xml]$report = Get-Content -LiteralPath (Join-Path $results 'postgres.trx')
    $counts = $report.TestRun.ResultSummary.Counters
    if ([int]$counts.executed -eq 0 -or [int]$counts.notExecuted -ne 0 -or [int]$counts.failed -ne 0) {
        throw 'PostgreSQL tests were skipped, failed, or not discovered.'
    }
    Write-Output "Verified $($counts.passed) PostgreSQL tests without failures or skips. Results: $results"
}
finally {
    $env:BUN3_POSTGRES_TEST_CONNECTION = $previousConnection
    if ($containerId -match '^[a-f0-9]{64}$') {
        $label = docker inspect --format '{{index .Config.Labels "bun3.authority-test"}}' $containerId
        if ($LASTEXITCODE -eq 0 -and $label -eq $runId) {
            docker rm --force $containerId | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove the owned test container $containerId." }
        }
        else { Write-Warning 'Container identity could not be verified; automatic cleanup was skipped.' }
    }
    if (Test-Path -LiteralPath $envFile) { Remove-Item -LiteralPath $envFile }
}
