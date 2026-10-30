param([switch]$OpenServices)
$ErrorActionPreference = 'Stop'
$serviceName = 'PRN222_TRANDOANTHANHHA_ASM04'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($service) {
    $service | Select-Object Name, DisplayName, State, StartMode, PathName
} else {
    Write-Host "Service $serviceName is NOT installed."
}
if ($OpenServices) { Start-Process services.msc }
