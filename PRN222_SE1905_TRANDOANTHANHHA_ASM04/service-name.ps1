$workerOptionsPath = Join-Path $PSScriptRoot 'src\SelfStorageManagementSystem.Worker\WorkerOptions.cs'
$workerOptionsSource = Get-Content -LiteralPath $workerOptionsPath -Raw
if ($workerOptionsSource -notmatch 'public const string ServiceName\s*=\s*"([A-Za-z0-9_-]+)"') {
    throw 'Cannot resolve service name from WorkerOptions.cs.'
}
$serviceName = $Matches[1]
