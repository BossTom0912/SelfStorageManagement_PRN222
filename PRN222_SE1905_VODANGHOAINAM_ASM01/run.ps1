$ErrorActionPreference = 'Stop'
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet run --project (Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Web') @args
exit $LASTEXITCODE
