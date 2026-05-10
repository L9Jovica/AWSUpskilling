# AWS SSO Configuration Helper
# This script helps you set up AWS SSO configuration

Write-Host "=== AWS SSO Configuration Setup ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "You need the following information from your AWS administrator:" -ForegroundColor Yellow
Write-Host "1. SSO Start URL (e.g., https://d-xxxxxxxxxx.awsapps.com/start)"
Write-Host "2. SSO Region (e.g., us-east-1, eu-west-1)"
Write-Host "3. Account ID (you have: 765891906457)"
Write-Host "4. Role Name (e.g., AdministratorAccess, PowerUserAccess)"
Write-Host ""

# Get SSO Start URL
Write-Host "Enter SSO Start URL: " -NoNewline -ForegroundColor Cyan
$ssoStartUrl = Read-Host

# Get SSO Region
Write-Host "Enter SSO Region [default: eu-west-1]: " -NoNewline -ForegroundColor Cyan
$ssoRegion = Read-Host
if ([string]::IsNullOrWhiteSpace($ssoRegion)) {
    $ssoRegion = "eu-west-1"
}

# Get Account ID
Write-Host "Enter Account ID [default: 765891906457]: " -NoNewline -ForegroundColor Cyan
$accountId = Read-Host
if ([string]::IsNullOrWhiteSpace($accountId)) {
    $accountId = "765891906457"
}

# Get Role Name
Write-Host "Enter Role Name: " -NoNewline -ForegroundColor Cyan
$roleName = Read-Host

# Get Region
Write-Host "Enter Default Region [default: eu-west-1]: " -NoNewline -ForegroundColor Cyan
$defaultRegion = Read-Host
if ([string]::IsNullOrWhiteSpace($defaultRegion)) {
    $defaultRegion = "eu-west-1"
}

# Validate required inputs
if ([string]::IsNullOrWhiteSpace($ssoStartUrl) -or [string]::IsNullOrWhiteSpace($roleName)) {
    Write-Host ""
    Write-Host "ERROR: SSO Start URL and Role Name are required!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Creating SSO configuration..." -ForegroundColor Yellow

# Create config content
$configPath = "$env:USERPROFILE\.aws\config"
$configContent = @"
[default]
sso_start_url = $ssoStartUrl
sso_region = $ssoRegion
sso_account_id = $accountId
sso_role_name = $roleName
region = $defaultRegion
output = json
"@

# Backup existing config
if (Test-Path $configPath) {
    Copy-Item $configPath "$configPath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Write-Host "Backed up existing config" -ForegroundColor Gray
}

# Write new config
$configContent | Out-File -FilePath $configPath -Encoding utf8 -Force

Write-Host "✓ SSO configuration created successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Configuration saved to: $configPath" -ForegroundColor Gray
Write-Host ""

# Show next steps
Write-Host "=== Next Steps ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Run: aws sso login" -ForegroundColor Yellow
Write-Host "   This will open a browser for authentication"
Write-Host ""
Write-Host "2. After successful login, test with:" -ForegroundColor Yellow
Write-Host "   aws sts get-caller-identity" -ForegroundColor White
Write-Host ""
Write-Host "3. SSO credentials will auto-refresh!" -ForegroundColor Green
Write-Host ""
