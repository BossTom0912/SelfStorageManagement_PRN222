$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Web') @args
exit $LASTEXITCODE