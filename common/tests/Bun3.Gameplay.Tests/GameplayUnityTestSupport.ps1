function ConvertTo-WindowsCommandLine {
  param(
    [Parameter(Mandatory = $true)]
    [string[]]$Arguments
  )

  $quotedArguments = New-Object 'System.Collections.Generic.List[string]'
  foreach ($argument in $Arguments) {
    $quotedArgument = New-Object System.Text.StringBuilder
    $null = $quotedArgument.Append([char]34)
    $backslashCount = 0

    foreach ($character in $argument.ToCharArray()) {
      if ($character -eq [char]92) {
        $backslashCount++
      } elseif ($character -eq [char]34) {
        $null = $quotedArgument.Append([char]92, (2 * $backslashCount + 1))
        $null = $quotedArgument.Append([char]34)
        $backslashCount = 0
      } else {
        $null = $quotedArgument.Append([char]92, $backslashCount)
        $null = $quotedArgument.Append($character)
        $backslashCount = 0
      }
    }

    $null = $quotedArgument.Append([char]92, (2 * $backslashCount))
    $null = $quotedArgument.Append([char]34)
    $quotedArguments.Add($quotedArgument.ToString())
  }

  return [string]::Join(' ', $quotedArguments.ToArray())
}
