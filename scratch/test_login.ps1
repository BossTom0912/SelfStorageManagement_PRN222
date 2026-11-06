[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
$loginUrl = 'https://127.0.0.1:7242/Account/Login'
$r = Invoke-WebRequest -Uri $loginUrl -SessionVariable sess -UseBasicParsing
$token = ''
if ($r.Content -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') {
    $token = $matches[1]
} elseif ($r.Content -match 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') {
    $token = $matches[1]
}
Write-Host "Token found: $([string]::IsNullOrEmpty($token) -eq $false)"

$body = @{
    UserName = 'admin@selfstorage.local'
    Password = 'SecretAdminPassword123!'
    __RequestVerificationToken = $token
}

try {
    $post = Invoke-WebRequest -Uri $loginUrl -WebSession $sess -Method Post -Body $body -MaximumRedirection 0 -UseBasicParsing
    Write-Host "Post StatusCode: $($post.StatusCode)"
    Write-Host "Post Location: $($post.Headers.Location)"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    Write-Host "WebException StatusCode: $([int]$resp.StatusCode)"
    Write-Host "WebException Location: $($resp.Headers['Location'])"
}
