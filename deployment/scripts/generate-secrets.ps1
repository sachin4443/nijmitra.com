<# Generates the two required production secrets. Store them as SYSTEM environment variables — never in Git. #>
$enc = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$jwt = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
Write-Host "NIJMITRA_Jwt__SigningKey=$jwt"
Write-Host "NIJMITRA_Security__DataEncryptionKey=$enc"
Write-Warning "Back up NIJMITRA_Security__DataEncryptionKey securely. Losing it makes encrypted diary entries, 2FA secrets and provider keys unreadable."
