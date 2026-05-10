# Status Query Lambda - Implementation Summary

## 🎯 What We Built

We've completed the **Status Query Lambda** component! This is the third Lambda in your serverless media processor pipeline.

###  Complete Pipeline Flow (Now 3/3 Lambdas Done!)

```
User uploads image
       ↓
[1] POST /upload → Upload Lambda → S3 (input) + DynamoDB
       ↓
EventBridge detects upload
       ↓
[2] Processing Lambda → Process image (30-40s) → S3 (output) + Update DynamoDB
       ↓
User checks status
       ↓
[3] GET /status/{jobId} → Status Query Lambda → Read DynamoDB → Return status ✅ NEW!
```

---

## 📁 Files Created/Modified

### New File:
- **`LambdaHandlers/Handlers/StatusQueryHandler.cs`** (330 lines)
  - Complete Lambda handler for status queries
  - Reads from DynamoDB
  - Returns user-friendly JSON responses
  - Handles all job statuses: Pending, Processing, Completed, Failed

### Modified Files:
- **`Infrastructure/InfrastructureStack.cs`**
  - Added `StatusQueryFunction` property
  - Created `CreateStatusQueryLambda()` method (250+ lines with detailed comments)
  - Added GET /status/{jobId} route to existing REST API
  - Configured DynamoDB read permissions

---

## 🔍 How Status Query Lambda Works

### 1. **API Gateway Route**
```
GET /status/{jobId}
```
- User provides Job ID in URL path
- Example: `GET https://api-url.com/prod/status/abc-123-def-456`

### 2. **Lambda Processing**
1. Extract `jobId` from path parameters
2. Validate `jobId` format
3. Query DynamoDB table using `jobId` as primary key
4. Parse DynamoDB item into `ProcessingMetadata` object
5. Build user-friendly response based on status
6. Return JSON with status details

### 3. **Response Examples**

#### Pending Status:
```json
{
  "jobId": "abc-123",
  "status": "Pending",
  "message": "Job is queued for processing",
  "fileName": "image.jpg",
  "fileType": "image/jpeg",
  "fileSize": 524288,
  "uploadedAt": "2026-05-07T10:00:00Z"
}
```

#### Processing Status:
```json
{
  "jobId": "abc-123",
  "status": "Processing",
  "message": "Job is currently being processed",
  "fileName": "image.jpg",
  "processingStartedAt": "2026-05-07T10:00:05Z",
  "timeInProcessing": "15.3 seconds"
}
```

#### Completed Status:
```json
{
  "jobId": "abc-123",
  "status": "Completed",
  "message": "Job completed successfully",
  "fileName": "image.jpg",
  "uploadedAt": "2026-05-07T10:00:00Z",
  "completedAt": "2026-05-07T10:00:40Z",
  "outputFile": "processed/abc-123/image_processed.jpg",
  "processedDimensions": "800x600",
  "processingDuration": "35.2 seconds"
}
```

#### Failed Status:
```json
{
  "jobId": "abc-123",
  "status": "Failed",
  "message": "Job processing failed",
  "error": "Invalid image format",
  "failedAt": "2026-05-07T10:00:10Z"
}
```

---

## 🔧 Technical Details

### Lambda Configuration
- **Runtime:** .NET 8 on Amazon Linux 2023
- **Memory:** 256 MB (lightweight, only queries DynamoDB)
- **Timeout:** 10 seconds
- **Handler:** `LambdaHandlers::LambdaHandlers.Handlers.StatusQueryHandler::HandleStatusQueryAsync`

### IAM Permissions
- **DynamoDB:** `GetItem` (read single item by job ID)
- **CloudWatch Logs:** Write logs for debugging

### Environment Variables
- `DYNAMODB_TABLE_NAME`: Name of the jobs table

---

## 💡 Key Learning Points

### 1. **API Gateway Path Parameters**
```csharp
// In Lambda, access path variables from request:
string jobId = request.PathParameters["jobId"];
```

In CDK:
```csharp
var statusJobResource = statusResource.AddResource("{jobId}");
statusJobResource.AddMethod("GET", integration);
```

### 2. **DynamoDB GetItem**
```csharp
var request = new GetItemRequest
{
    TableName = _tableName,
    Key = new Dictionary<string, AttributeValue>
    {
        { "JobId", new AttributeValue { S = jobId } }
    }
};
var response = await _dynamoDbClient.GetItemAsync(request);
```

- **Fast:** Single-item lookups by primary key are very efficient
- **Cost:** ~$0.00000025 per request (covered by free tier)

### 3. **Parsing DynamoDB Items**
DynamoDB stores items as attribute-value pairs:
```csharp
// String: item["JobId"].S
// Number: item["FileSize"].N (parse to long)
// Enum: Parse string to enum type
```

### 4. **User-Friendly Responses**
Instead of raw DynamoDB data, we build responses that:
- Use camelCase (JavaScript-friendly)
- Include helpful messages
- Calculate durations
- Format timestamps (ISO 8601)

---

## 📊 Cost Analysis

Assuming 1,000 jobs/month with 5 status checks per job (5,000 requests):

### Lambda Invocations:
- **Requests:** 5,000 × $0.0000002 = **$0.001**
- **Duration:** 256 MB × 50ms × 5,000 = **$0.00052**
- **Total Lambda:** ~$0.002

### API Gateway:
- **Requests:** 5,000 × $0.0000035 = **$0.0175**

### DynamoDB Reads:
- **GetItem:** 5,000 × $0.00000025 = **$0.00125**
- (Covered by free tier: 25 read units/sec)

### **Total Cost: ~$0.02/month (2 cents)**

---

## 🚀 Next Steps - Deployment

### Ready to Deploy!

Your Status Query Lambda is ready to deploy with CDK. Here's what will happen:

1. **CDK will:**
   - Create Status Query Lambda function
   - Grant DynamoDB read permissions
   - Add GET /status/{jobId} route to existing API
   - Output the status query URL

2. **You'll be able to:**
   - Upload images via POST /upload
   - Check status via GET /status/{jobId}
   - See real-time processing progress

---

## 🧪 Testing Plan

After deployment, we'll test:

1. **Upload an image** → Get Job ID
2. **Immediately check status** → Should show "Pending"
3. **Wait 5 seconds, check again** → Should show "Processing"
4. **Wait 40 seconds, check again** → Should show "Completed" with results
5. **Check invalid Job ID** → Should return 404 error

---

## ✅ Progress Summary

**Completed (3/3 Core Lambdas):**
- ✅ Upload Lambda + POST /upload
- ✅ Processing Lambda + EventBridge
- ✅ Status Query Lambda + GET /status/{jobId}

**Remaining Tasks:**
- ⏭️ Deploy Status Query Lambda (CDK deploy)
- CloudWatch monitoring & dashboards
- Unit tests
- Documentation

---

## 📝 Deployment Commands (When AWS credentials are ready)

```powershell
# Navigate to Infrastructure directory
cd Infrastructure

# Preview changes
cdk diff

# Deploy Status Query Lambda
cdk deploy

# Test the new endpoint
# URL will be output by CDK deployment
```

---

**You now have a complete serverless API with:**
- Image upload
- Background processing
- Status tracking

All three core components are coded and ready to deploy! 🎉

When you're ready to deploy, just refresh your AWS credentials and run `cdk deploy`.
