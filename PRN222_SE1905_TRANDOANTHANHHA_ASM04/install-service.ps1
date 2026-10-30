param([switch]$Start)
$ErrorActionPreference = 'Stop'
$serviceName = 'PRN222_TRANDOANTHANHHA_ASM04'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in PowerShell as Administrator to register the Windows service.'
}
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Service $serviceName already exists. Inspect its executable path before changing it."
}

$publishPath = Join-Path $PSScriptRoot 'publish\worker'
dotnet publish (Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Worker') -c Release -r win-x64 --self-contained false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
$executable = Join-Path $publishPath 'SelfStorageManagementSystem.Worker.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Published executable is missing.' }
if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}
New-Service -Name $serviceName -DisplayName 'PRN222 Thanh Ha ASM04 Worker' -BinaryPathName ('"' + $executable + '"') -StartupType Manual -Description 'ASM04 worker: heartbeat every 5 seconds; optional reservation maintenance.'
if ($Start) { Start-Service -Name $serviceName }
Get-Service -Name $serviceName
