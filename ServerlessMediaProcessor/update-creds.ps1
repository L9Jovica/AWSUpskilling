# AWS Credentials Quick Update
# Simple script to update expired AWS temporary credentials

Write-Host "`n=== AWS Credentials Update ===" -ForegroundColor Cyan
Write-Host "`nGet your credentials from AWS Console:" -ForegroundColor Yellow
Write-Host "1. https://console.aws.amazon.com/"
Write-Host "2. Click username (top-right) > 'Command line or programmatic access'"
Write-Host "3. Copy values from Option 2 (Windows PowerShell)`n"

# Get credentials
Write-Host "Enter AWS_ACCESS_KEY_ID: " -NoNewline -ForegroundColor Green
$key = Read-Host

Write-Host "Enter AWS_SECRET_ACCESS_KEY: " -NoNewline -ForegroundColor Green
$secret = Read-Host

Write-Host "Enter AWS_SESSION_TOKEN: " -NoNewline -ForegroundColor Green
$token = Read-Host

# Validate
if (!$key -or !$secret -or !$token) {
    Write-Host "`nERROR: All three values are required!" -ForegroundColor Red
    exit 1
}

# Save credentials
Write-Host "`nUpdating credentials..." -ForegroundColor Yellow
$path = "$env:USERPROFILE\.aws\credentials"

# Backup if exists
if (Test-Path $path) {
    Copy-Item $path "$path.backup" -Force
}

# Write new credentials
@"
[default]
aws_access_key_id = $key
aws_secret_access_key = $secret
aws_session_token = $token
"@ | Out-File $path -Encoding utf8 -Force

Write-Host "✓ Credentials saved!" -ForegroundColor Green

# Test
Write-Host "`nTesting credentials..." -ForegroundColor Yellow
$result = aws sts get-caller-identity 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Success! Credentials are working!" -ForegroundColor Green
    Write-Host "`nYour AWS Identity:" -ForegroundColor Cyan
    $result
} else {
    Write-Host "✗ Test failed!" -ForegroundColor Red
    Write-Host $result
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
