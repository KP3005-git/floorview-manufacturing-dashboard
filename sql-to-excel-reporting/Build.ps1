$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$output = Join-Path $PSScriptRoot 'ReportExport.exe'
& $compiler /nologo /target:exe /optimize+ /r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:$output (Join-Path $PSScriptRoot 'ReportExport.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $output"
