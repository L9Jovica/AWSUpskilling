# API Reference

Complete API documentation for the Serverless Media Processor REST API.

---

## Base URL

```
https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod
```

**Components**:
- `xkl2fk7jbb` - API Gateway ID (unique to your deployment)
- `eu-west-1` - AWS region
- `prod` - Stage name (deployment environment)

---

## Endpoints

### 1. Upload Image

Upload an image for processing.

#### Request

```http
POST /upload
Content-Type: application/json
```

**Request Body**:
```json
{
  "fileName": "my-image.png",
  "fileType": "image/png",
  "fileData": "<base64-encoded-image-data>"
}
```

**Fields**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `fileName` | string | Yes | Original filename (used for S3 key and tracking) |
| `fileType` | string | Yes | MIME type (`image/jpeg`, `image/png`, `image/gif`) |
| `fileData` | string | Yes | Base64-encoded image binary data |

#### Response

**Success (200 OK)**:
```json
{
  "message": "Upload successful",
  "jobId": "abc-123-def-456-ghi-789",
  "s3Key": "jobs/abc-123-def-456-ghi-789/my-image.png"
}
```

**Response Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `message` | string | Success message |
| `jobId` | string | Unique job identifier (UUID/GUID) for status queries |
| `s3Key` | string | S3 object key where image is stored |

**Error Responses**:

**400 Bad Request** - Invalid input:
```json
{
  "error": "Failed to process upload",
  "message": "Missing required fields"
}
```

**500 Internal Server Error** - Server failure:
```json
{
  "error": "Failed to process upload",
  "message": "S3 upload failed"
}
```

#### Example Usage

**cURL**:
```bash
curl -X POST https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/upload \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "test-image.png",
    "fileType": "image/png",
    "fileData": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
  }'
```

**PowerShell**:
```powershell
$imageBytes = [System.IO.File]::ReadAllBytes("path/to/image.png")
$base64 = [Convert]::ToBase64String($imageBytes)

$body = @{
    fileName = "test-image.png"
    fileType = "image/png"
    fileData = $base64
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/upload" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"

Write-Output "Job ID: $($response.jobId)"
```

**Insomnia/Postman**:
1. Method: `POST`
2. URL: `https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/upload`
3. Headers: `Content-Type: application/json`
4. Body (JSON):
   ```json
   {
     "fileName": "test.png",
     "fileType": "image/png",
     "fileData": "<paste-base64-here>"
   }
   ```

#### Processing Timeline

After a successful upload:

```
Time     Status        Description
──────────────────────────────────────────────────────────
0s       Pending       Image uploaded, job created
2-5s     Processing    EventBridge triggered processing Lambda
35-40s   Completed     Processing finished, results available
```

---

### 2. Query Job Status

Get the current status and details of a processing job.

#### Request

```http
GET /status/{jobId}
```

**Path Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `jobId` | string | Yes | Job ID returned from upload endpoint |

**Example URL**:
```
https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/status/abc-123-def-456-ghi-789
```

#### Response

Responses vary based on job status:

### Response: Pending

Job is queued, not yet started processing.

**200 OK**:
```json
{
  "jobId": "abc-123-def-456-ghi-789",
  "status": "Pending",
  "fileName": "my-image.png",
  "fileType": "image/png",
  "fileSize": 12345,
  "uploadedAt": "2026-05-10T10:00:00.123Z",
  "message": "Job is queued for processing"
}
```

### Response: Processing

Job is currently being processed.

**200 OK**:
```json
{
  "jobId": "abc-123-def-456-ghi-789",
  "status": "Processing",
  "fileName": "my-image.png",
  "fileType": "image/png",
  "fileSize": 12345,
  "uploadedAt": "2026-05-10T10:00:00.123Z",
  "processingStartedAt": "2026-05-10T10:00:05.456Z",
  "timeInProcessing": "10.5 seconds",
  "message": "Job is currently being processed"
}
```

### Response: Completed

Job successfully completed.

**200 OK**:
```json
{
  "jobId": "abc-123-def-456-ghi-789",
  "status": "Completed",
  "fileName": "my-image.png",
  "fileType": "image/png",
  "fileSize": 12345,
  "uploadedAt": "2026-05-10T10:00:00.123Z",
  "processingStartedAt": "2026-05-10T10:00:05.456Z",
  "completedAt": "2026-05-10T10:00:40.789Z",
  "processingDuration": "35.3 seconds",
  "outputFile": "jobs/abc-123-def-456-ghi-789/processed_my-image.png",
  "processedDimensions": "800x600",
  "message": "Job completed successfully"
}
```

### Response: Failed

Job failed during processing.

**200 OK** (Yes, 200 - the query itself succeeded):
```json
{
  "jobId": "abc-123-def-456-ghi-789",
  "status": "Failed",
  "fileName": "my-image.png",
  "fileType": "image/png",
  "fileSize": 12345,
  "uploadedAt": "2026-05-10T10:00:00.123Z",
  "processingStartedAt": "2026-05-10T10:00:05.456Z",
  "failedAt": "2026-05-10T10:00:10.999Z",
  "error": "Failed to download image from S3: Access Denied",
  "message": "Job processing failed"
}
```

**Error Responses**:

**400 Bad Request** - Invalid Job ID format:
```json
{
  "error": "Invalid jobId format",
  "statusCode": 400
}
```

**404 Not Found** - Job doesn't exist:
```json
{
  "error": "Job not found: abc-123-def-456-ghi-789",
  "statusCode": 404
}
```

**500 Internal Server Error** - Server failure:
```json
{
  "error": "Internal server error while querying job status",
  "statusCode": 500
}
```

#### Example Usage

**cURL**:
```bash
curl -X GET https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/status/abc-123-def-456-ghi-789
```

**PowerShell**:
```powershell
$jobId = "abc-123-def-456-ghi-789"
$response = Invoke-RestMethod -Uri "https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/status/$jobId" `
    -Method Get

Write-Output "Status: $($response.status)"
Write-Output "Message: $($response.message)"
```

**Insomnia/Postman**:
1. Method: `GET`
2. URL: `https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/status/abc-123-def-456-ghi-789`
3. No headers or body required

---

## Common Workflows

### Workflow 1: Upload and Wait for Completion

```powershell
# 1. Upload image
$uploadResponse = Invoke-RestMethod -Uri "$baseUrl/upload" `
    -Method Post -Body $uploadBody -ContentType "application/json"
$jobId = $uploadResponse.jobId
Write-Output "Uploaded! Job ID: $jobId"

# 2. Poll status every 5 seconds
do {
    Start-Sleep -Seconds 5
    $status = Invoke-RestMethod -Uri "$baseUrl/status/$jobId" -Method Get
    Write-Output "Status: $($status.status)"
} while ($status.status -in @("Pending", "Processing"))

# 3. Check final status
if ($status.status -eq "Completed") {
    Write-Output "✅ Processing completed!"
    Write-Output "Processed image: $($status.outputFile)"
    Write-Output "Dimensions: $($status.processedDimensions)"
} else {
    Write-Output "❌ Processing failed: $($status.error)"
}
```

### Workflow 2: Immediate Status Check

Useful for checking status immediately after upload (will show "Pending"):

```powershell
# Upload
$uploadResponse = Invoke-RestMethod -Uri "$baseUrl/upload" ...
$jobId = $uploadResponse.jobId

# Immediate check (will be "Pending")
$status = Invoke-RestMethod -Uri "$baseUrl/status/$jobId" -Method Get
# Output: { "status": "Pending", "message": "Job is queued for processing" }
```

### Workflow 3: Check After Processing Time

Wait for expected processing time before checking:

```powershell
# Upload
$uploadResponse = Invoke-RestMethod -Uri "$baseUrl/upload" ...
$jobId = $uploadResponse.jobId

# Wait for typical processing time (40 seconds)
Write-Output "Waiting 40 seconds for processing..."
Start-Sleep -Seconds 40

# Check status (should be "Completed")
$status = Invoke-RestMethod -Uri "$baseUrl/status/$jobId" -Method Get
```

---

## Response Field Reference

### Common Fields

Fields present in all status responses:

| Field | Type | Description | Example |
|-------|------|-------------|---------|
| `jobId` | string | Unique job identifier | `"abc-123-def-456-ghi-789"` |
| `status` | string | Current status | `"Pending"`, `"Processing"`, `"Completed"`, `"Failed"` |
| `fileName` | string | Original filename | `"my-image.png"` |
| `fileType` | string | MIME type | `"image/png"` |
| `fileSize` | number | File size in bytes | `12345` |
| `uploadedAt` | string | ISO 8601 timestamp | `"2026-05-10T10:00:00.123Z"` |
| `message` | string | Human-readable status message | `"Job completed successfully"` |

### Status-Specific Fields

#### Processing Status

| Field | Type | Description |
|-------|------|-------------|
| `processingStartedAt` | string | When processing began (ISO 8601) |
| `timeInProcessing` | string | Current duration | `"10.5 seconds"` |

#### Completed Status

| Field | Type | Description |
|-------|------|-------------|
| `processingStartedAt` | string | When processing began |
| `completedAt` | string | When processing finished |
| `processingDuration` | string | Total processing time |
| `outputFile` | string | S3 key of processed image |
| `processedDimensions` | string | Image dimensions | `"800x600"` |

#### Failed Status

| Field | Type | Description |
|-------|------|-------------|
| `processingStartedAt` | string | When processing began |
| `failedAt` | string | When processing failed |
| `error` | string | Detailed error message |

---

## Error Codes

### HTTP Status Codes

| Code | Meaning | When It Occurs |
|------|---------|----------------|
| 200 | Success | Request processed successfully |
| 400 | Bad Request | Invalid input (missing fields, invalid format) |
| 404 | Not Found | Job ID doesn't exist in database |
| 500 | Internal Server Error | Lambda error, S3 failure, DynamoDB failure |
| 502 | Bad Gateway | API Gateway can't reach Lambda |
| 503 | Service Unavailable | Lambda throttled or AWS service outage |
| 504 | Gateway Timeout | Lambda took longer than 29 seconds |

### Application Error Messages

#### Upload Endpoint Errors

| Error Message | Cause | Solution |
|---------------|-------|----------|
| "Missing required fields" | Request body missing fileName, fileType, or fileData | Include all required fields |
| "Invalid file type" | Unsupported MIME type | Use image/jpeg, image/png, or image/gif |
| "Failed to decode base64" | Invalid base64 encoding | Ensure fileData is valid base64 |
| "S3 upload failed" | S3 service error or IAM permission issue | Check CloudWatch Logs, verify IAM permissions |
| "Failed to write to DynamoDB" | DynamoDB service error | Check CloudWatch Logs |

#### Status Query Endpoint Errors

| Error Message | Cause | Solution |
|---------------|-------|----------|
| "Missing jobId in request path" | URL doesn't include jobId parameter | Use /status/{jobId} format |
| "Invalid jobId format" | JobId too short or invalid characters | Use jobId returned from upload endpoint |
| "Job not found: {jobId}" | JobId doesn't exist in database | Verify jobId is correct |
| "Internal server error" | Lambda or DynamoDB failure | Check CloudWatch Logs |

---

## Rate Limits

### API Gateway Limits

- **Requests per second**: 10,000 (default account limit)
- **Burst**: 5,000 requests
- **Throttling**: Returns 429 Too Many Requests

### Lambda Limits

- **Concurrent executions**: 1,000 (default account limit)
- **Throttling**: Returns 502 Bad Gateway from API Gateway

### Best Practices

1. **Polling**: Wait at least 5 seconds between status checks
2. **Retry Logic**: Implement exponential backoff for 5XX errors
3. **Timeouts**: Set client timeout to 30+ seconds for upload

---

## CORS Configuration

Cross-Origin Resource Sharing is enabled for all origins.

**Allowed Headers**:
- `Content-Type`
- `Authorization` (for future authentication)

**Allowed Methods**:
- `GET`
- `POST`
- `OPTIONS` (preflight)

**Allowed Origins**:
- `*` (all origins - for learning only, not production-ready)

**Example Preflight**:
```http
OPTIONS /upload
Access-Control-Request-Method: POST
Access-Control-Request-Headers: Content-Type

Response:
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: POST, OPTIONS
Access-Control-Allow-Headers: Content-Type
```

---

## Authentication

**Current Status**: ❌ No authentication implemented

This is a learning project. In production, you should add:

1. **API Keys**: Basic authentication via `x-api-key` header
2. **AWS IAM**: Request signing with AWS credentials
3. **Cognito**: User pools for JWT-based auth
4. **OAuth 2.0**: Third-party authentication

---

## Versioning

**Current Version**: v1 (implicit)

The API is currently unversioned. In production, version your API:

```
/v1/upload
/v1/status/{jobId}
```

This allows backwards-compatible changes while supporting legacy clients.

---

## Support & Troubleshooting

### Common Issues

**Issue**: "Missing Authentication Token"
- **Cause**: Incorrect URL (missing `/prod/` stage or wrong path)
- **Fix**: Verify URL includes stage: `.../prod/upload`

**Issue**: Image upload returns 400
- **Cause**: Invalid base64 or missing fields
- **Fix**: Validate base64 encoding, ensure all fields present

**Issue**: Status always shows "Pending"
- **Cause**: Processing Lambda not triggered (EventBridge issue)
- **Fix**: Check EventBridge rule, verify S3 event notifications enabled

**Issue**: 504 Gateway Timeout
- **Cause**: Lambda took longer than 29 seconds
- **Fix**: Upload Lambda shouldn't timeout; check CloudWatch Logs

### Debugging with CloudWatch

```powershell
# Import monitoring module
Import-Module .\CloudWatch-Monitoring.psm1

# Check recent errors
Get-RecentErrors -Since "30m"

# Search for specific job
Search-LogsForJobId -JobId "your-job-id"

# View Lambda metrics
Get-LambdaErrors
```

---

## Examples Collection

### Complete End-to-End PowerShell Script

```powershell
# Configuration
$baseUrl = "https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod"
$imagePath = "path/to/your/image.png"

# 1. Read and encode image
Write-Host "Reading image..." -ForegroundColor Cyan
$imageBytes = [System.IO.File]::ReadAllBytes($imagePath)
$base64 = [Convert]::ToBase64String($imageBytes)

# 2. Upload
Write-Host "Uploading..." -ForegroundColor Cyan
$uploadBody = @{
    fileName = [System.IO.Path]::GetFileName($imagePath)
    fileType = "image/png"
    fileData = $base64
} | ConvertTo-Json

try {
    $uploadResponse = Invoke-RestMethod -Uri "$baseUrl/upload" `
        -Method Post -Body $uploadBody -ContentType "application/json"
    
    $jobId = $uploadResponse.jobId
    Write-Host "✅ Upload successful! Job ID: $jobId" -ForegroundColor Green
    
    # 3. Poll status
    Write-Host "`nPolling status..." -ForegroundColor Cyan
    $attempts = 0
    $maxAttempts = 12  # 60 seconds (5s intervals)
    
    do {
        Start-Sleep -Seconds 5
        $attempts++
        
        $status = Invoke-RestMethod -Uri "$baseUrl/status/$jobId" -Method Get
        Write-Host "[$attempts/$maxAttempts] Status: $($status.status) - $($status.message)" -ForegroundColor Yellow
        
        if ($status.status -eq "Completed") {
            Write-Host "`n✅ Processing completed!" -ForegroundColor Green
            Write-Host "Output: $($status.outputFile)" -ForegroundColor White
            Write-Host "Dimensions: $($status.processedDimensions)" -ForegroundColor White
            Write-Host "Duration: $($status.processingDuration)" -ForegroundColor White
            break
        } elseif ($status.status -eq "Failed") {
            Write-Host "`n❌ Processing failed!" -ForegroundColor Red
            Write-Host "Error: $($status.error)" -ForegroundColor Red
            break
        }
    } while ($attempts -lt $maxAttempts)
    
    if ($attempts -ge $maxAttempts) {
        Write-Host "`n⚠️  Max attempts reached. Job may still be processing." -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}
```

---

## API Testing Checklist

- [ ] Upload valid PNG image
- [ ] Upload valid JPEG image
- [ ] Upload with missing fileName → 400 error
- [ ] Upload with invalid base64 → 400 error
- [ ] Query status immediately after upload → "Pending"
- [ ] Query status during processing → "Processing" with time elapsed
- [ ] Query status after 40 seconds → "Completed" with results
- [ ] Query non-existent job ID → 404 error
- [ ] Query invalid job ID format → 400 error
- [ ] Verify CORS headers present
- [ ] Check CloudWatch Logs for each request

---

For implementation details, see [ARCHITECTURE.md](./ARCHITECTURE.md).
For deployment instructions, see [DEPLOYMENT-WORKFLOW.md](./DEPLOYMENT-WORKFLOW.md).
