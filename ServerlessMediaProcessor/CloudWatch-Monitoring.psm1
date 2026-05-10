# CloudWatch Log and Metric Query Scripts
# Helper functions for monitoring the Serverless Media Processor

#############################################
# CLOUDWATCH LOGS FUNCTIONS
#############################################

function Get-RecentErrors {
    <#
    .SYNOPSIS
    Get recent errors from all Lambda functions
    .PARAMETER Since
    Time range (e.g., "10m", "1h", "2d")
    .EXAMPLE
    Get-RecentErrors -Since "30m"
    #>
    param(
        [string]$Since = "1h"
    )
    
    Write-Host "=== Recent Errors from All Lambdas ===" -ForegroundColor Yellow
    aws logs tail /aws/lambda/MediaProcessor-ImageUpload-JSavic `
        /aws/lambda/MediaProcessor-ProcessingHandler-JSavic `
        /aws/lambda/MediaProcessor-StatusQuery-JSavic `
        --since $Since --filter-pattern "ERROR"
}

function Get-UploadLogs {
    <#
    .SYNOPSIS
    Get logs from Upload Lambda
    .PARAMETER Since
    Time range (e.g., "10m", "1h")
    .PARAMETER Follow
    Continuously stream logs
    .EXAMPLE
    Get-UploadLogs -Since "30m"
    Get-UploadLogs -Follow
    #>
    param(
        [string]$Since = "10m",
        [switch]$Follow
    )
    
    if ($Follow) {
        aws logs tail /aws/lambda/MediaProcessor-ImageUpload-JSavic --follow
    } else {
        aws logs tail /aws/lambda/MediaProcessor-ImageUpload-JSavic --since $Since
    }
}

function Get-ProcessingLogs {
    <#
    .SYNOPSIS
    Get logs from Processing Lambda
    .PARAMETER Since
    Time range
    .PARAMETER Follow
    Continuously stream logs
    .EXAMPLE
    Get-ProcessingLogs -Since "1h"
    #>
    param(
        [string]$Since = "10m",
        [switch]$Follow
    )
    
    if ($Follow) {
        aws logs tail /aws/lambda/MediaProcessor-ProcessingHandler-JSavic --follow
    } else {
        aws logs tail /aws/lambda/MediaProcessor-ProcessingHandler-JSavic --since $Since
    }
}

function Get-StatusQueryLogs {
    <#
    .SYNOPSIS
    Get logs from Status Query Lambda
    .PARAMETER Since
    Time range
    .EXAMPLE
    Get-StatusQueryLogs -Since "30m"
    #>
    param(
        [string]$Since = "10m",
        [switch]$Follow
    )
    
    if ($Follow) {
        aws logs tail /aws/lambda/MediaProcessor-StatusQuery-JSavic --follow
    } else {
        aws logs tail /aws/lambda/MediaProcessor-StatusQuery-JSavic --since $Since
    }
}

function Search-LogsForJobId {
    <#
    .SYNOPSIS
    Search logs for a specific Job ID across all Lambdas
    .PARAMETER JobId
    The Job ID to search for
    .EXAMPLE
    Search-LogsForJobId -JobId "abc123-..."
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$JobId
    )
    
    Write-Host "=== Searching for Job ID: $JobId ===" -ForegroundColor Yellow
    aws logs tail /aws/lambda/MediaProcessor-ImageUpload-JSavic `
        /aws/lambda/MediaProcessor-ProcessingHandler-JSavic `
        /aws/lambda/MediaProcessor-StatusQuery-JSavic `
        --since 24h --filter-pattern $JobId
}

#############################################
# CLOUDWATCH METRICS FUNCTIONS
#############################################

function Get-LambdaInvocationCount {
    <#
    .SYNOPSIS
    Get invocation count for all Lambdas in the last hour
    .EXAMPLE
    Get-LambdaInvocationCount
    #>
    
    Write-Host "`n=== Lambda Invocation Counts (Last Hour) ===" -ForegroundColor Cyan
    
    $functions = @(
        "MediaProcessor-ImageUpload-JSavic",
        "MediaProcessor-ProcessingHandler-JSavic",
        "MediaProcessor-StatusQuery-JSavic"
    )
    
    $endTime = Get-Date
    $startTime = $endTime.AddHours(-1)
    
    foreach ($func in $functions) {
        $stats = aws cloudwatch get-metric-statistics `
            --namespace AWS/Lambda `
            --metric-name Invocations `
            --dimensions Name=FunctionName,Value=$func `
            --start-time $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
            --end-time $endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
            --period 3600 `
            --statistics Sum `
            --output json | ConvertFrom-Json
        
        $count = if ($stats.Datapoints.Count -gt 0) { $stats.Datapoints[0].Sum } else { 0 }
        Write-Host "$func : $count invocations" -ForegroundColor White
    }
}

function Get-LambdaErrors {
    <#
    .SYNOPSIS
    Get error counts for all Lambdas in the last hour
    .EXAMPLE
    Get-LambdaErrors
    #>
    
    Write-Host "`n=== Lambda Error Counts (Last Hour) ===" -ForegroundColor Cyan
    
    $functions = @(
        "MediaProcessor-ImageUpload-JSavic",
        "MediaProcessor-ProcessingHandler-JSavic",
        "MediaProcessor-StatusQuery-JSavic"
    )
    
    $endTime = Get-Date
    $startTime = $endTime.AddHours(-1)
    
    foreach ($func in $functions) {
        $stats = aws cloudwatch get-metric-statistics `
            --namespace AWS/Lambda `
            --metric-name Errors `
            --dimensions Name=FunctionName,Value=$func `
            --start-time $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
            --end-time $endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
            --period 3600 `
            --statistics Sum `
            --output json | ConvertFrom-Json
        
        $count = if ($stats.Datapoints.Count -gt 0) { $stats.Datapoints[0].Sum } else { 0 }
        $color = if ($count -gt 0) { "Red" } else { "Green" }
        Write-Host "$func : $count errors" -ForegroundColor $color
    }
}

function Get-ProcessingDuration {
    <#
    .SYNOPSIS
    Get average and max duration for Processing Lambda
    .EXAMPLE
    Get-ProcessingDuration
    #>
    
    Write-Host "`n=== Processing Lambda Duration Stats (Last Hour) ===" -ForegroundColor Cyan
    
    $endTime = Get-Date
    $startTime = $endTime.AddHours(-1)
    
    $stats = aws cloudwatch get-metric-statistics `
        --namespace AWS/Lambda `
        --metric-name Duration `
        --dimensions Name=FunctionName,Value=MediaProcessor-ProcessingHandler-JSavic `
        --start-time $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
        --end-time $endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
        --period 3600 `
        --statistics Average,Maximum `
        --output json | ConvertFrom-Json
    
    if ($stats.Datapoints.Count -gt 0) {
        $avgMs = [math]::Round($stats.Datapoints[0].Average, 0)
        $maxMs = [math]::Round($stats.Datapoints[0].Maximum, 0)
        $avgSec = [math]::Round($avgMs / 1000, 1)
        $maxSec = [math]::Round($maxMs / 1000, 1)
        
        Write-Host "Average Duration: $avgSec seconds ($avgMs ms)" -ForegroundColor White
        Write-Host "Maximum Duration: $maxSec seconds ($maxMs ms)" -ForegroundColor White
        
        if ($avgMs > 45000) {
            Write-Host "WARNING: Average duration exceeds 45 second threshold!" -ForegroundColor Red
        }
    } else {
        Write-Host "No data available" -ForegroundColor Yellow
    }
}

#############################################
# CLOUDWATCH ALARMS FUNCTIONS
#############################################

function Get-AlarmStatus {
    <#
    .SYNOPSIS
    Get current status of all CloudWatch alarms
    .EXAMPLE
    Get-AlarmStatus
    #>
    
    Write-Host "`n=== CloudWatch Alarm Status ===" -ForegroundColor Cyan
    
    aws cloudwatch describe-alarms `
        --alarm-names "MediaProcessor-UploadErrors-JSavic" `
                      "MediaProcessor-ProcessingErrors-JSavic" `
                      "MediaProcessor-SlowProcessing-JSavic" `
                      "MediaProcessor-APIErrors-JSavic" `
        --query "MetricAlarms[*].{Name:AlarmName,State:StateValue,Reason:StateReason}" `
        --output table
}

function Get-AlarmHistory {
    <#
    .SYNOPSIS
    Get alarm state change history
    .PARAMETER Since
    How far back to look (e.g., "1h", "1d")
    .EXAMPLE
    Get-AlarmHistory -Since "24h"
    #>
    param(
        [string]$Since = "24h"
    )
    
    Write-Host "`n=== Alarm History (Last $Since) ===" -ForegroundColor Cyan
    
    $endTime = Get-Date
    
    # Parse the Since parameter
    if ($Since -match "(\d+)([hd])") {
        $value = [int]$matches[1]
        $unit = $matches[2]
        
        $startTime = switch ($unit) {
            "h" { $endTime.AddHours(-$value) }
            "d" { $endTime.AddDays(-$value) }
        }
    } else {
        $startTime = $endTime.AddHours(-24)
    }
    
    aws cloudwatch describe-alarm-history `
        --start-date $startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
        --end-date $endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
        --history-item-type StateUpdate `
        --query "AlarmHistoryItems[*].{Alarm:AlarmName,Time:Timestamp,Summary:HistorySummary}" `
        --output table
}

#############################################
# DASHBOARD FUNCTIONS
#############################################

function Open-CloudWatchDashboard {
    <#
    .SYNOPSIS
    Open CloudWatch Dashboard in browser
    .EXAMPLE
    Open-CloudWatchDashboard
    #>
    
    $region = "eu-west-1"
    $dashboardName = "MediaProcessor-Dashboard-JSavic"
    $url = "https://$region.console.aws.amazon.com/cloudwatch/home?region=$region#dashboards:name=$dashboardName"
    
    Write-Host "Opening CloudWatch Dashboard..." -ForegroundColor Cyan
    Start-Process $url
}

#############################################
# COMBINED HEALTH CHECK
#############################################

function Get-SystemHealth {
    <#
    .SYNOPSIS
    Get overall system health (invocations, errors, alarms)
    .EXAMPLE
    Get-SystemHealth
    #>
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "   SYSTEM HEALTH REPORT" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    Get-LambdaInvocationCount
    Get-LambdaErrors
    Get-ProcessingDuration
    Get-AlarmStatus
}

#############################################
# EXPORT FUNCTIONS
#############################################

Export-ModuleMember -Function @(
    'Get-RecentErrors',
    'Get-UploadLogs',
    'Get-ProcessingLogs',
    'Get-StatusQueryLogs',
    'Search-LogsForJobId',
    'Get-LambdaInvocationCount',
    'Get-LambdaErrors',
    'Get-ProcessingDuration',
    'Get-AlarmStatus',
    'Get-AlarmHistory',
    'Open-CloudWatchDashboard',
    'Get-SystemHealth'
)

# Display available functions on module load
Write-Host "`nCloudWatch Monitoring Module Loaded!" -ForegroundColor Green
Write-Host "Available functions:" -ForegroundColor Cyan
Write-Host "  Logs:" -ForegroundColor Yellow
Write-Host "    - Get-RecentErrors" -ForegroundColor White
Write-Host "    - Get-UploadLogs, Get-ProcessingLogs, Get-StatusQueryLogs" -ForegroundColor White
Write-Host "    - Search-LogsForJobId" -ForegroundColor White
Write-Host "  Metrics:" -ForegroundColor Yellow
Write-Host "    - Get-LambdaInvocationCount, Get-LambdaErrors, Get-ProcessingDuration" -ForegroundColor White
Write-Host "  Alarms:" -ForegroundColor Yellow
Write-Host "    - Get-AlarmStatus, Get-AlarmHistory" -ForegroundColor White
Write-Host "  Dashboard:" -ForegroundColor Yellow
Write-Host "    - Open-CloudWatchDashboard" -ForegroundColor White
Write-Host "  Health:" -ForegroundColor Yellow
Write-Host "    - Get-SystemHealth" -ForegroundColor White
Write-Host ""
