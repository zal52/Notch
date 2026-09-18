param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
# The desktop now owns its local backend and chooses a free loopback port.
Remove-Item Env:NOTCH_BACKEND_URL -ErrorAction SilentlyContinue
& (Join-Path $PSScriptRoot 'run.ps1') -NoBuild:$NoBuild
