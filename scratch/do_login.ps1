$cookieFile = 'D:\FPT\PRN222\Project\Final_Project_Web\scratch\cookies.txt'
if (Test-Path $cookieFile) { Remove-Item $cookieFile }

$resp = curl.exe -k -s -c $cookieFile "https://127.0.0.1:7242/Account/Login"
$token = ''
if ($resp -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') {
    $token = $matches[1]
}
Write-Host "Token extracted: $([string]::IsNullOrEmpty($token) -eq $false)"

$body = "UserName=administrator@example.test&Password=TestPass123!&__RequestVerificationToken=" + [uri]::EscapeDataString($token)
$postOut = curl.exe -k -i -s -b $cookieFile -c $cookieFile -d "$body" "https://127.0.0.1:7242/Account/Login"
$postOut | Select-String "HTTP/|Location:|Set-Cookie:"
