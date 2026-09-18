param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$output = Join-Path $root 'dist\Notch-win-x64'
Push-Location $root
try {
    & $dotnet publish src/Notch/Notch.csproj -c Release -r win-x64 --self-contained true --configfile NuGet.Config -p:RestorePackagesPath="$root\.tools\packages" -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    & $dotnet publish src/Notch.Backend/Notch.Backend.csproj -c Release -r win-x64 --self-contained true --configfile NuGet.Config -p:RestorePackagesPath="$root\.tools\packages" -o (Join-Path $output 'backend') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
    Write-Output "Portable application: $output. Copy the entire folder and launch Notch.exe."
} finally { Pop-Location }
