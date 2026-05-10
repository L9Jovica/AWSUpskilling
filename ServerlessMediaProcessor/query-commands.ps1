# AWS Query Commands - Quick Reference
# Serverless Media Processor

# ============================================
# DYNAMODB QUERIES
# ============================================

# Get specific job by ID
function Get-Job {
    param([string]$JobId)
    aws dynamodb get-item `
        --table-name MediaProcessingJobs-JSavic `
        --key "{`"JobId`":{`"S`":`"$JobId`"}}" | ConvertFrom-Json | ConvertTo-Json -Depth 10
}

# Get all jobs
function Get-AllJobs {
    aws dynamodb scan --table-name MediaProcessingJobs-JSavic | ConvertFrom-Json | ConvertTo-Json -Depth 10
}

# Get jobs by status
function Get-JobsByStatus {
    param([string]$Status) # Pending, Processing, Completed, Failed
    aws dynamodb scan `
        --table-name MediaProcessingJobs-JSavic `
        --filter-expression "Status = :status" `
        --expression-attribute-values "{`":status`":{`"S`":`"$Status`"}}" | ConvertFrom-Json | ConvertTo-Json -Depth 10
}

# Count jobs
function Get-JobCount {
    aws dynamodb scan --table-name MediaProcessingJobs-JSavic --select COUNT
}

# ============================================
# CLOUDWATCH LOGS
# ============================================

# View Upload Lambda logs (last N minutes)
function Get-UploadLogs {
    param([int]$Minutes = 10)
    aws logs tail /aws/lambda/MediaProcessor-UploadHandler-JSavic --since "$($Minutes)m"
}

# View Processing Lambda logs (last N minutes)
function Get-ProcessingLogs {
    param([int]$Minutes = 10)
    aws logs tail /aws/lambda/MediaProcessor-ProcessingHandler-JSavic --since "$($Minutes)m"
}

# Follow Upload Lambda logs in real-time
function Watch-UploadLogs {
    aws logs tail /aws/lambda/MediaProcessor-UploadHandler-JSavic --follow
}

# Follow Processing Lambda logs in real-time
function Watch-ProcessingLogs {
    aws logs tail /aws/lambda/MediaProcessor-ProcessingHandler-JSavic --follow
}

# Search logs for specific job ID
function Find-JobInLogs {
    param(
        [string]$JobId,
        [string]$Lambda = "Processing" # "Upload" or "Processing"
    )
    
    $logGroup = if ($Lambda -eq "Upload") {
        "/aws/lambda/MediaProcessor-UploadHandler-JSavic"
    } else {
        "/aws/lambda/MediaProcessor-ProcessingHandler-JSavic"
    }
    
    aws logs tail $logGroup --since 1h | Select-String $JobId -Context 5
}

# Search for errors in logs
function Find-Errors {
    param([string]$Lambda = "Processing") # "Upload" or "Processing"
    
    $logGroup = if ($Lambda -eq "Upload") {
        "/aws/lambda/MediaProcessor-UploadHandler-JSavic"
    } else {
        "/aws/lambda/MediaProcessor-ProcessingHandler-JSavic"
    }
    
    aws logs tail $logGroup --since 1h | Select-String -Pattern "ERROR|Error|fail|exception" -Context 3
}

# ============================================
# S3 QUERIES
# ============================================

# List uploaded images
function Get-UploadedImages {
    aws s3 ls s3://media-processor-input-jsavic-765891906457/jobs/ --recursive --human-readable
}

# List processed images
function Get-ProcessedImages {
    aws s3 ls s3://media-processor-output-jsavic-765891906457/processed/ --recursive --human-readable
}

# Get image for specific job
function Get-JobImages {
    param([string]$JobId)
    Write-Host "Input image:" -ForegroundColor Green
    aws s3 ls s3://media-processor-input-jsavic-765891906457/jobs/$JobId/ --human-readable
    Write-Host "`nOutput image:" -ForegroundColor Green
    aws s3 ls s3://media-processor-output-jsavic-765891906457/processed/$JobId/ --human-readable
}

# ============================================
# COMBINED QUERIES
# ============================================

# Get complete job details (DynamoDB + S3 + Logs)
function Get-JobDetails {
    param([string]$JobId)
    
    Write-Host "=== Job Details for $JobId ===" -ForegroundColor Cyan
    
    Write-Host "`n1. DynamoDB Metadata:" -ForegroundColor Yellow
    Get-Job -JobId $JobId
    
    Write-Host "`n2. S3 Files:" -ForegroundColor Yellow
    Get-JobImages -JobId $JobId
    
    Write-Host "`n3. Processing Logs:" -ForegroundColor Yellow
    Find-JobInLogs -JobId $JobId -Lambda "Processing"
}

# ============================================
# USAGE EXAMPLES
# ============================================

# Source this file:
# . .\query-commands.ps1

# Then use commands like:
# Get-AllJobs
# Get-Job -JobId "547d3693-e14c-4319-87a2-b23006f8c926"
# Get-JobsByStatus -Status "Completed"
# Get-UploadLogs -Minutes 30
# Watch-ProcessingLogs
# Find-JobInLogs -JobId "547d3693" -Lambda "Processing"
# Get-JobDetails -JobId "547d3693-e14c-4319-87a2-b23006f8c926"
