# AWS Credentials Update Script
# This script helps you quickly update your AWS temporary credentials

Write-Host "=== AWS Credentials Update Tool ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Go to AWS Console: https://console.aws.amazon.com/"
Write-Host "2. Click your username (top-right)"
Write-Host "3. Click 'Command line or programmatic access'"
Write-Host "4. Copy the credentials from Option 2 (PowerShell)"
Write-Host ""
Write-Host "Paste the three export lines below (press Enter after each):" -ForegroundColor Green
Write-Host ""

# Get Access Key
Write-Host "Enter AWS_ACCESS_KEY_ID: " -NoNewline -ForegroundColor Cyan
$accessKey = Read-Host

# Get Secret Key
Write-Host "Enter AWS_SECRET_ACCESS_KEY: " -NoNewline -ForegroundColor Cyan
$secretKey = Read-Host

# Get Session Token
Write-Host "Enter AWS_SESSION_TOKEN (long value): " -NoNewline -ForegroundColor Cyan
$sessionToken = Read-Host

# Validate inputs
$isValid = -not ([string]::IsNullOrWhiteSpace($accessKey) -or [string]::IsNullOrWhiteSpace($secretKey) -or [string]::IsNullOrWhiteSpace($sessionToken))

if (-not $isValid) {
    Write-Host ""
    Write-Host "ERROR: All three values are required!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Updating AWS credentials..." -ForegroundColor Yellow

# Update credentials file
$credentialsPath = "$env:USERPROFILE\.aws\credentials"
$credentialsContent = @"
[default]
aws_access_key_id = $accessKey
aws_secret_access_key = $secretKey
aws_session_token = $sessionToken
"@

# Backup existing credentials
if (Test-Path $credentialsPath) {
    Copy-Item $credentialsPath "$credentialsPath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Write-Host "Backed up existing credentials" -ForegroundColor Gray
}

# Write new credentials
$credentialsContent | Out-File -FilePath $credentialsPath -Encoding utf8 -Force

Write-Host "✓ Credentials updated successfully!" -ForegroundColor Green
Write-Host ""

# Test credentials
Write-Host "Testing credentials..." -ForegroundColor Yellow
try {
    $testResult = aws sts get-caller-identity 2>&1 | Out-String
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Credentials are valid!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Your AWS Identity:" -ForegroundColor Cyan
        $testResult | ConvertFrom-Json | ConvertTo-Json
    } else {
        Write-Host "✗ Credentials test failed!" -ForegroundColor Red
        Write-Host $testResult
        exit 1
    }
} catch {
    Write-Host "✗ Credentials test failed!" -ForegroundColor Red
    Write-Host $_.Exception.Message
    exit 1
}

Write-Host ""
Write-Host "=== Ready to use AWS CLI ===" -ForegroundColor Green
