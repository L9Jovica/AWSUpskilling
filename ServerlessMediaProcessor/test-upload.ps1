# ==============================================================================
# Test Script for Serverless Media Processor Upload API
# ==============================================================================
# This script tests the image upload endpoint by:
# 1. Creating a small test image (1x1 pixel PNG)
# 2. Converting it to Base64
# 3. Sending it to the API Gateway endpoint
# 4. Displaying the response
# ==============================================================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Media Processor Upload API" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Your API endpoint (from CDK deployment output)
$apiUrl = "https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/upload"

Write-Host "API Endpoint: $apiUrl" -ForegroundColor Yellow
Write-Host ""

# ==============================================================================
# STEP 1: CREATE A TEST IMAGE
# ==============================================================================
Write-Host "[1/4] Creating test image..." -ForegroundColor Green

# This is a base64-encoded 1x1 pixel red PNG image
# In a real scenario, you would read an actual image file
$base64Image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="

# Alternative: If you want to use an actual image file, uncomment this:
# $imagePath = "C:\path\to\your\image.jpg"
# if (Test-Path $imagePath) {
#     $imageBytes = [System.IO.File]::ReadAllBytes($imagePath)
#     $base64Image = [Convert]::ToBase64String($imageBytes)
#     Write-Host "  ✓ Loaded image from: $imagePath" -ForegroundColor Green
# } else {
#     Write-Host "  ! Image file not found, using default 1x1 test image" -ForegroundColor Yellow
# }

Write-Host "  ✓ Test image created (Base64 length: $($base64Image.Length) characters)" -ForegroundColor Green
Write-Host ""

# ==============================================================================
# STEP 2: PREPARE THE REQUEST BODY
# ==============================================================================
Write-Host "[2/4] Preparing request body..." -ForegroundColor Green

# The request body must match what your Lambda expects:
# {
#   "fileName": "string",
#   "contentType": "string",
#   "imageData": "base64_string"
# }
$requestBody = @{
    fileName = "test-image-$(Get-Date -Format 'yyyyMMdd-HHmmss').png"
    contentType = "image/png"
    imageData = $base64Image
} | ConvertTo-Json

Write-Host "  ✓ Request body prepared" -ForegroundColor Green
Write-Host "    - File name: test-image-$(Get-Date -Format 'yyyyMMdd-HHmmss').png" -ForegroundColor Gray
Write-Host "    - Content type: image/png" -ForegroundColor Gray
Write-Host "    - Image data size: $($base64Image.Length) bytes (Base64)" -ForegroundColor Gray
Write-Host ""

# ==============================================================================
# STEP 3: SEND THE REQUEST
# ==============================================================================
Write-Host "[3/4] Sending POST request to API..." -ForegroundColor Green

try {
    $response = Invoke-RestMethod `
        -Method POST `
        -Uri $apiUrl `
        -Body $requestBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "  ✓ Request successful!" -ForegroundColor Green
    Write-Host ""
    
    # ==============================================================================
    # STEP 4: DISPLAY THE RESPONSE
    # ==============================================================================
    Write-Host "[4/4] Response from Lambda:" -ForegroundColor Green
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "Job ID:  $($response.jobId)" -ForegroundColor White
    Write-Host "Message: $($response.message)" -ForegroundColor White
    Write-Host "Status:  $($response.status)" -ForegroundColor White
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Host "✓ SUCCESS! Your image was uploaded to S3 and metadata saved to DynamoDB" -ForegroundColor Green
    Write-Host ""
    Write-Host "What happened behind the scenes:" -ForegroundColor Yellow
    Write-Host "  1. API Gateway received your HTTP POST request" -ForegroundColor Gray
    Write-Host "  2. API Gateway invoked the Lambda function" -ForegroundColor Gray
    Write-Host "  3. Lambda parsed the JSON and decoded Base64 image" -ForegroundColor Gray
    Write-Host "  4. Lambda uploaded image to S3 bucket: media-processor-input-jsavic-765891906457" -ForegroundColor Gray
    Write-Host "  5. Lambda saved job metadata to DynamoDB table: MediaProcessingJobs-JSavic" -ForegroundColor Gray
    Write-Host "  6. Lambda returned success response to API Gateway" -ForegroundColor Gray
    Write-Host "  7. API Gateway sent HTTP 200 response back to you" -ForegroundColor Gray
    Write-Host ""
    
    # Display the full response JSON for debugging
    Write-Host "Full Response (JSON):" -ForegroundColor Yellow
    $response | ConvertTo-Json | Write-Host -ForegroundColor Gray
    
} catch {
    Write-Host "  ✗ Request failed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Yellow
    Write-Host "  Status Code: $($_.Exception.Response.StatusCode.Value__)" -ForegroundColor Red
    Write-Host "  Status Description: $($_.Exception.Response.StatusDescription)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Full Error:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    
    if ($_.ErrorDetails.Message) {
        Write-Host "Response Body:" -ForegroundColor Yellow
        Write-Host $_.ErrorDetails.Message -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
