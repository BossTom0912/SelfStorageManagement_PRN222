<#
.SYNOPSIS
    Initializes a new SelfStorage web database for WebDemo, WebTest, or WebE2eTest.
.DESCRIPTION
    Strictly verifies target database against allowlist:
      - SelfStoragePRN222_WebDemo
      - SelfStoragePRN222_WebTest
      - SelfStoragePRN222_WebE2eTest
    Stops immediately if the database already exists. Contains no deletion or DROP statements.
.PARAMETER DatabaseName
    Target database name (Default: SelfStoragePRN222_WebDemo).
.PARAMETER ServerInstance
    SQL Server instance name (Default: (localdb)\mssqllocaldb).
#>
[CmdletBinding()]
param(
    [ValidateSet("SelfStoragePRN222_WebDemo", "SelfStoragePRN222_WebTest", "SelfStoragePRN222_WebE2eTest")]
    [string]$DatabaseName = "SelfStoragePRN222_WebDemo",

    [string]$ServerInstance = "(localdb)\mssqllocaldb"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlScript = Join-Path $scriptDir "$($DatabaseName)_Bootstrap.sql"

if (-not (Test-Path $sqlScript)) {
    throw "SQL bootstrap script not found for $DatabaseName at $sqlScript"
}

Write-Host "Checking if database [$DatabaseName] already exists on server [$ServerInstance]..." -ForegroundColor Cyan

# Test connection and check DB existence
$connectionString = "Server=$ServerInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
try {
    $conn = New-Object Microsoft.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
}
catch {
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
        $conn.Open()
    }
    catch {
        Write-Warning "Could not connect to SQL Server [$ServerInstance] to verify database state. Error: $($_.Exception.Message)"
        Write-Host "You can execute [$sqlScript] manually in SQL Server Management Studio (SSMS)." -ForegroundColor Yellow
        return
    }
}

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT DB_ID(@dbName);"
$param = $cmd.Parameters.Add("@dbName", [System.Data.SqlDbType]::NVarChar, 128)
$param.Value = $DatabaseName
$result = $cmd.ExecuteScalar()

if ($result -ne [DBNull]::Value -and $null -ne $result) {
    $conn.Close()
    throw "ERROR: Database [$DatabaseName] already exists on [$ServerInstance]. Bootstrap stopped immediately to preserve data."
}

$conn.Close()
Write-Host "Database [$DatabaseName] does not exist. Ready to initialize using $sqlScript." -ForegroundColor Green
Write-Host "To execute in SSMS: Open $sqlScript and press F5 (Execute)." -ForegroundColor Cyan
