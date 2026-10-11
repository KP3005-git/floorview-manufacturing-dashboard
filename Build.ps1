$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler is missing. Install .NET Framework developer tools to build this project.'
}
$output = Join-Path $PSScriptRoot 'FloorviewDemo.exe'
& $compiler /nologo /target:winexe /optimize+ /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:$output (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $output"
