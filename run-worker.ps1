$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Worker') -- @args
exit $LASTEXITCODE
