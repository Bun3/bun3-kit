param(
  [Parameter(Mandatory = $true)][string]$ExpectedBackend,
  [string]$LogPath,
  [string]$ResultPath
)
$ErrorActionPreference = 'Stop'
if (-not $ResultPath -and -not $LogPath) { throw 'ResultPath or LogPath is required.' }
$lines = @()
if ($ResultPath) {
  [xml]$xml = Get-Content -Raw -Encoding UTF8 -LiteralPath $ResultPath
  if ([int]$xml.'test-run'.testcasecount -eq 0 `
    -or $xml.'test-run'.result -ne 'Passed' `
    -or [int]$xml.'test-run'.failed -ne 0) {
    throw "Tag tests failed or discovered zero tests: $ResultPath"
  }
  $lines += @($xml.SelectNodes('//output') | ForEach-Object { $_.InnerText -split "`r?`n" })
}
$lines += @(
  if ($LogPath) {
    Get-Content -Encoding UTF8 -LiteralPath $LogPath
  }
)
$pattern = 'TAGPERF backend=(\S+) N=(\d+) M=(\d+) D=(\d+) ' +
  'container=(TagContainer|TagCountContainer) kind=(ExactHit|ParentHit|Miss) ' +
  'new_p50_ticks=(\d+) new_p95_ticks=(\d+) new_p99_ticks=(\d+) ' +
  'legacy_p50_ticks=(\d+) legacy_p95_ticks=(\d+) legacy_p99_ticks=(\d+) alloc_count=(\d+)$'
$rows = @($lines | Where-Object { $_ -like 'TAGPERF *' } |
  ForEach-Object { [regex]::Match($_, $pattern) })
if ($rows.Count -ne 144 -or @($rows | Where-Object { -not $_.Success }).Count -ne 0) {
  throw "Expected 144 parseable TAGPERF rows, got $($rows.Count)."
}
$readSeen = @{}
$readKindCounts = @{ ExactHit = 0; ParentHit = 0; Miss = 0 }
foreach ($row in $rows) {
  if ($row.Groups[1].Value -ne $ExpectedBackend) {
    throw "Unexpected backend in TAGPERF row: $($row.Value)"
  }
  $n = [int]$row.Groups[2].Value; $m = [int]$row.Groups[3].Value
  $d = [int]$row.Groups[4].Value; $container = $row.Groups[5].Value
  $kind = $row.Groups[6].Value
  if ($n -notin @(5000, 50000) -or $m -notin @(8, 32, 64) `
    -or $d -notin @(1, 4, 8, 16)) { throw "Invalid TAGPERF identity: $($row.Value)" }
  $key = "$n|$m|$d|$container|$kind"
  if ($readSeen.ContainsKey($key)) { throw "Duplicate TAGPERF row: $key" }
  $readSeen[$key] = $true; $readKindCounts[$kind]++
  $new50 = [long]$row.Groups[7].Value; $new95 = [long]$row.Groups[8].Value
  $new99 = [long]$row.Groups[9].Value; $old50 = [long]$row.Groups[10].Value
  $old95 = [long]$row.Groups[11].Value; $old99 = [long]$row.Groups[12].Value
  $allocated = [long]$row.Groups[13].Value
  if ($new50 -gt $new95 -or $new95 -gt $new99 `
    -or $old50 -gt $old95 -or $old95 -gt $old99 `
    -or $new50 -gt $old50 -or $new95 -gt $old95 -or $new99 -gt $old99 `
    -or $allocated -ne 0) {
    throw "GameplayTag performance gate failed: $($row.Value)"
  }
}
if ($readKindCounts.ExactHit -ne 48 -or $readKindCounts.ParentHit -ne 48 `
  -or $readKindCounts.Miss -ne 48) { throw 'TAGPERF matrix is incomplete.' }
$mutationPattern = 'TAGMUT backend=(\S+) N=(\d+) M=(\d+) D=(\d+) ' +
  'container=(TagContainer|TagCountContainer) kind=(AddRemove|ReadWriteMixed) ' +
  'new_p50_ticks=(\d+) new_p95_ticks=(\d+) new_p99_ticks=(\d+) ' +
  'legacy_p50_ticks=(\d+) legacy_p95_ticks=(\d+) legacy_p99_ticks=(\d+) alloc_count=(\d+)$'
$mutationRows = @($lines | Where-Object { $_ -like 'TAGMUT *' } |
  ForEach-Object { [regex]::Match($_, $mutationPattern) })
if ($mutationRows.Count -ne 96 -or @($mutationRows | Where-Object { -not $_.Success }).Count -ne 0) {
  throw "Expected 96 parseable TAGMUT rows, got $($mutationRows.Count)."
}
$mutationSeen = @{}
$mutationKindCounts = @{ AddRemove = 0; ReadWriteMixed = 0 }
foreach ($row in $mutationRows) {
  if ($row.Groups[1].Value -ne $ExpectedBackend) {
    throw "Unexpected backend in TAGMUT row: $($row.Value)"
  }
  $n = [int]$row.Groups[2].Value; $m = [int]$row.Groups[3].Value
  $d = [int]$row.Groups[4].Value; $container = $row.Groups[5].Value
  $kind = $row.Groups[6].Value
  if ($n -notin @(5000, 50000) -or $m -notin @(8, 32, 64) `
    -or $d -notin @(1, 4, 8, 16)) { throw "Invalid TAGMUT identity: $($row.Value)" }
  $key = "$n|$m|$d|$container|$kind"
  if ($mutationSeen.ContainsKey($key)) { throw "Duplicate TAGMUT row: $key" }
  $mutationSeen[$key] = $true; $mutationKindCounts[$kind]++
  $new50 = [long]$row.Groups[7].Value; $new95 = [long]$row.Groups[8].Value
  $new99 = [long]$row.Groups[9].Value; $old50 = [long]$row.Groups[10].Value
  $old95 = [long]$row.Groups[11].Value; $old99 = [long]$row.Groups[12].Value
  $allocated = [long]$row.Groups[13].Value
  if ($new50 -gt $new95 -or $new95 -gt $new99 `
    -or $old50 -gt $old95 -or $old95 -gt $old99 `
    -or $allocated -ne 0 `
    -or ($kind -eq 'ReadWriteMixed' -and `
      ($new50 -gt $old50 -or $new95 -gt $old95 -or $new99 -gt $old99))) {
    throw "GameplayTag mutation performance gate failed: $($row.Value)"
  }
}
if ($mutationKindCounts.AddRemove -ne 48 -or $mutationKindCounts.ReadWriteMixed -ne 48) {
  throw 'TAGMUT matrix is incomplete.'
}
