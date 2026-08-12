$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameplayUnityTestSupport.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class WindowsCommandLineParser
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string[] Parse(string commandLine)
    {
        int argumentCount;
        var argumentsPointer = CommandLineToArgvW(commandLine, out argumentCount);
        if (argumentsPointer == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var arguments = new string[argumentCount];
            for (var index = 0; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argumentsPointer, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer);
            }

            return arguments;
        }
        finally
        {
            LocalFree(argumentsPointer);
        }
    }
}
'@

$expectedArguments = @(
  'path with spaces',
  'prefix\"quote edge\'
)
$commandLine = 'argv-probe.exe ' + (ConvertTo-WindowsCommandLine -Arguments $expectedArguments)
$actualArguments = [WindowsCommandLineParser]::Parse($commandLine)

if ($actualArguments.Count -ne $expectedArguments.Count + 1) {
  throw "Expected $($expectedArguments.Count + 1) argv entries but received $($actualArguments.Count): $commandLine"
}

for ($index = 0; $index -lt $expectedArguments.Count; $index++) {
  if ($actualArguments[$index + 1] -cne $expectedArguments[$index]) {
    throw "Argument $index was not preserved. Expected '$($expectedArguments[$index])', received '$($actualArguments[$index + 1])'."
  }
}

Write-Output "Windows command-line argument round-trip passed: $commandLine"
