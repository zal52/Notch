param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$notchRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $notchRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnetCommand = $localDotnet
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
} else {
    $dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_HOME = Join-Path $notchRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $notchRoot
try {
    if (-not $NoBuild) {
        & $dotnetCommand restore src/Notch/Notch.csproj --configfile NuGet.Config --packages .tools/packages --nologo
        if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
        & $dotnetCommand build src/Notch/Notch.csproj -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    $appPath = Join-Path $notchRoot 'src\Notch\bin\Release\net10.0-windows\Notch.exe'
    if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build Notch before using -NoBuild.' }
    Start-Process -FilePath $appPath -WorkingDirectory $notchRoot
} finally {
    Pop-Location
}
