param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path "$framework\csc.exe")) { $framework = $framework.Replace('Framework64','Framework') }
$references = @('PresentationCore','PresentationFramework','WindowsBase') | ForEach-Object { "/reference:$framework\WPF\$_.dll" }
$sources = (Get-ChildItem "$PSScriptRoot\src\*.cs").FullName
$resources = @("/resource:$PSScriptRoot\src\Catalog.sql,Catalog.sql", "/resource:$PSScriptRoot\src\Window.xaml,Window.xaml", "/resource:$PSScriptRoot\src\UiText.tsv,UiText.tsv")
$outDir = Join-Path $PSScriptRoot 'bin'
$null = New-Item -Force -ItemType Directory $outDir
& "$framework\csc.exe" /nologo /optimize+ /target:winexe /codepage:65001 /utf8output /reference:System.Data.dll /reference:System.Xaml.dll @references @resources "/out:$outDir\DBCompare.exe" @sources
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Output "$outDir\DBCompare.exe"
if ($Test) {
    $testExe = Join-Path ([IO.Path]::GetTempPath()) ("DBCompareTests-" + [Guid]::NewGuid() + ".exe")
    try {
        & "$framework\csc.exe" /nologo /target:exe /main:DBCompare.Tests /codepage:65001 /reference:System.Data.dll /reference:System.Xaml.dll @references @resources "/out:$testExe" @sources "$PSScriptRoot\tests\Tests.cs"
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
        & $testExe
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    } finally {
        if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
    }
}
