$ErrorActionPreference = 'Stop'
$notchRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $notchRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $notchRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $notchRoot
try {
    & $dotnetCommand restore Notch.slnx --configfile NuGet.Config --packages .tools/packages --nologo
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    & $dotnetCommand test Notch.slnx -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally {
    Pop-Location
}
