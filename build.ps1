param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path "$framework\csc.exe")) { $framework = $framework.Replace('Framework64','Framework') }
$references = @('PresentationCore','PresentationFramework','WindowsBase') | ForEach-Object { "/reference:$framework\WPF\$_.dll" }
$references += "/reference:$framework\netstandard.dll"
$references += "/reference:$framework\System.Data.Common.dll"
$references += @('Npgsql','Microsoft.Bcl.AsyncInterfaces','System.Threading.Tasks.Extensions') | ForEach-Object { "/reference:$PSScriptRoot\bin\drivers\$_.dll" }
$sources = (Get-ChildItem "$PSScriptRoot\src\*.cs").FullName
$resources = @("/resource:$PSScriptRoot\src\Catalog.sql,Catalog.sql", "/resource:$PSScriptRoot\src\Window.xaml,Window.xaml", "/resource:$PSScriptRoot\src\UiText.tsv,UiText.tsv")
$outDir = Join-Path $PSScriptRoot 'bin'
$resources += "/resource:$PSScriptRoot\src\PostgresCatalog.sql,PostgresCatalog.sql"
$null = New-Item -Force -ItemType Directory $outDir
$driverDir = Join-Path $outDir 'drivers'
$null = New-Item -Force -ItemType Directory $driverDir
$driverFiles = foreach ($line in Get-Content -LiteralPath "$PSScriptRoot\src\PostgresPackages.lock") {
    $id, $version, $asset, $sha = $line.Split(' ')
    $dll = Join-Path $driverDir ([IO.Path]::GetFileName($asset))
    if (!(Test-Path -LiteralPath $dll) -or (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $sha) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $packageId = $id.ToLowerInvariant()
        $archive = Join-Path ([IO.Path]::GetTempPath()) ("DBCompare-" + [Guid]::NewGuid() + ".nupkg")
        $pending = "$dll.$([Guid]::NewGuid()).tmp"
        try {
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$packageId/$version/$packageId.$version.nupkg" -UseBasicParsing -OutFile $archive
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($asset), $pending)
            } finally { $zip.Dispose() }
            if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash -ne $sha) { throw "Unexpected DLL hash for $id $version" }
            Move-Item -LiteralPath $pending -Destination $dll -Force
        } finally {
            foreach ($file in $archive,$pending) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } }
        }
    }
    $dll
}
& "$framework\csc.exe" /nologo /optimize+ /target:winexe /codepage:65001 /utf8output /reference:System.Data.dll /reference:System.Xaml.dll @references @resources "/out:$outDir\DBCompare.exe" @sources
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Output "$outDir\DBCompare.exe"
if ($Test) {
    $testExe = Join-Path $outDir ("DBCompareTests-" + [Guid]::NewGuid() + ".exe")
    try {
        Copy-Item -LiteralPath "$outDir\DBCompare.exe.config" -Destination "$testExe.config"
        $testSources = (Get-ChildItem "$PSScriptRoot\tests\*.cs").FullName
        & "$framework\csc.exe" /nologo /target:exe /main:DBCompare.Tests /codepage:65001 /reference:System.Data.dll /reference:System.Xaml.dll @references @resources "/out:$testExe" @sources @testSources
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
        & $testExe
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    } finally {
        if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
        if (Test-Path -LiteralPath "$testExe.config") { Remove-Item -LiteralPath "$testExe.config" }
    }
}
if ($Package) {
    $dist = Join-Path $PSScriptRoot 'dist'
    $null = New-Item -Force -ItemType Directory $dist
    $bundle = Join-Path $dist 'DBCompare-windows.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pending = "$bundle.$([Guid]::NewGuid()).tmp"
    try {
        $zip = [IO.Compression.ZipFile]::Open($pending, 'Create')
        try {
            $files = @("$outDir\DBCompare.exe", "$outDir\DBCompare.exe.config", "$driverDir\ThirdPartyNotices.txt") + $driverFiles
            foreach ($file in $files) {
                $entry = $file.Substring($outDir.Length + 1).Replace('\','/')
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $entry)
            }
        } finally { $zip.Dispose() }
        Move-Item -LiteralPath $pending -Destination $bundle -Force
    } finally { if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending } }
    Write-Output $bundle
}
