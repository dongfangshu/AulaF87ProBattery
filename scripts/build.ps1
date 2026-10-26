param(
    [string]$PackagesPath,
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    $restoreOptions = @()
    if ($PackagesPath) { $restoreOptions += @('--packages', $PackagesPath) }
    if ($Source) { $restoreOptions += @('--source', $Source) }

    dotnet restore tests\AulaBattery.Tests @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build tests\AulaBattery.Tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet tests\AulaBattery.Tests\bin\Release\net10.0-windows\AulaBattery.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    dotnet publish src\AulaBattery.Windows -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
        -o dist\AulaBattery @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath README.md -Destination dist\AulaBattery\README.md
    New-Item -ItemType Directory -Force -Path dist\AulaBattery\docs | Out-Null
    Copy-Item -LiteralPath docs\PROTOCOL.md -Destination dist\AulaBattery\docs\PROTOCOL.md
    Write-Host 'Ready: dist\AulaBattery\AulaBattery.exe'
}
finally {
    Pop-Location
}
