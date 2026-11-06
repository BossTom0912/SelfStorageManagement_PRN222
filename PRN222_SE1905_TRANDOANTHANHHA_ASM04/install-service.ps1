param([switch]$Start, [switch]$UpdateExisting)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'service-name.ps1')
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in PowerShell as Administrator to register the Windows service.'
}
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and -not $UpdateExisting) {
    throw "Service $serviceName already exists. Use -UpdateExisting to deploy and update its executable path."
}
if ($existing -and $existing.Status -ne 'Stopped') {
    throw "Stop service $serviceName before updating its executable."
}

$publishPath = Join-Path $PSScriptRoot 'publish\worker'
dotnet publish (Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Worker') -c Release -r win-x64 --self-contained false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
$executable = Join-Path $publishPath 'SelfStorageManagementSystem.Worker.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Published executable is missing.' }
if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}
if ($existing) {
    $result = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Service -Filter "Name='$serviceName'") -MethodName Change -Arguments @{ PathName = ('"' + $executable + '"') }
    if ($result.ReturnValue -ne 0) { throw "Service update failed: $($result.ReturnValue)" }
} else {
    New-Service -Name $serviceName -DisplayName $serviceName -BinaryPathName ('"' + $executable + '"') -StartupType Manual -Description 'ASM04 worker: heartbeat every 5 seconds; optional reservation maintenance.'
}
if ($Start) { Start-Service -Name $serviceName }
Get-Service -Name $serviceName
