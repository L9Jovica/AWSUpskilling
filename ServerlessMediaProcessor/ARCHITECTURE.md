# Architecture Documentation

## System Architecture Overview

This document provides a detailed explanation of the Serverless Media Processor architecture, AWS service interactions, and key design decisions.

---

## Table of Contents

1. [Architecture Diagram](#architecture-diagram)
2. [Component Details](#component-details)
3. [Data Flow](#data-flow)
4. [AWS Services Deep Dive](#aws-services-deep-dive)
5. [Security Model](#security-model)
6. [Scalability & Performance](#scalability--performance)
7. [Design Decisions](#design-decisions)

---

## Architecture Diagram

### Complete System Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                                  CLIENT                                     │
│                          (Browser, Insomnia, Postman)                      │
└──────────────────────────────────┬─────────────────────────────────────────┘
                                   │ HTTPS
                                   │
┌──────────────────────────────────▼─────────────────────────────────────────┐
│                            API GATEWAY (REST API)                           │
│  ┌──────────────────────┐              ┌──────────────────────┐           │
│  │  POST /prod/upload   │              │ GET /prod/status/:id │           │
│  │  - CORS enabled      │              │  - Path parameter    │           │
│  │  - Lambda proxy      │              │  - Lambda proxy      │           │
│  └──────────┬───────────┘              └──────────┬───────────┘           │
└─────────────┼──────────────────────────────────────┼──────────────────────┘
              │                                       │
              │ invoke                                │ invoke
              │                                       │
┌─────────────▼───────────┐              ┌──────────▼────────────┐
│   UPLOAD LAMBDA         │              │   STATUS QUERY        │
│   (Sync - 30s timeout)  │              │   LAMBDA              │
│                         │              │   (Sync - 10s timeout)│
│ 1. Validate request     │              │                       │
│ 2. Generate Job ID      │              │ 1. Extract Job ID     │
│ 3. Encode base64→binary │              │ 2. Query DynamoDB     │
│ 4. Upload to S3 input   │              │ 3. Format response    │
│ 5. Write DynamoDB       │              │ 4. Return status      │
│    (Status: Pending)    │              │                       │
└─────────┬───────────────┘              └──────────┬────────────┘
          │                                         │
          │ PutObject                               │ GetItem
          │                                         │
┌─────────▼───────────────┐              ┌─────────▼────────────┐
│   S3 INPUT BUCKET       │              │    DYNAMODB TABLE    │
│   (Raw images)          │              │  (Job metadata)      │
│                         │              │                      │
│ Structure:              │              │ Partition Key: JobId │
│ jobs/{jobId}/           │              │ Attributes:          │
│   ├── input.jpg         │              │   - Status           │
│   └── metadata.json     │              │   - UploadedAt       │
└─────────┬───────────────┘              │   - ProcessingStart  │
          │                              │   - CompletedAt      │
          │ S3:ObjectCreated:Put         │   - OriginalFileName │
          │                              │   - FileSize         │
┌─────────▼───────────────┐              │   - InputS3Key       │
│     EVENTBRIDGE         │              │   - OutputS3Key      │
│  (Event Router)         │              │   - Dimensions       │
│                         │              │   - ErrorMessage     │
│ Rule:                   │              └──────────────────────┘
│  Source: aws.s3         │
│  DetailType:            │
│   Object Created        │
│  Target:                │
│   Processing Lambda     │
└─────────┬───────────────┘
          │
          │ trigger (async)
          │
┌─────────▼───────────────┐
│  PROCESSING LAMBDA      │
│  (Async - 60s timeout)  │
│                         │
│ 1. Extract S3 event     │
│ 2. Update Status →      │────────┐
│    "Processing"         │        │ UpdateItem
│ 3. Download from S3     │        │
│ 4. Resize image (800px) │        │
│ 5. Artificial delay     │        │
│    (30-35s)            │        │
│ 6. Upload to S3 output  │        │
│ 7. Update Status →      │────────┤ UpdateItem
│    "Completed"          │        │
│    (or "Failed")        │        │
└─────────┬───────────────┘        │
          │                        │
          │ PutObject              │
          │                        │
┌─────────▼───────────────┐        │
│  S3 OUTPUT BUCKET       │        │
│  (Processed images)     │        │
│                         │        │
│ Structure:              │        │
│ jobs/{jobId}/           │        │
│   └── processed.jpg     │        │
└─────────────────────────┘        │
                                   │
            ┌──────────────────────┘
            │
            ▼
┌──────────────────────────────────────────────────────────────┐
│                       DYNAMODB TABLE                          │
│                    (Updated with results)                     │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│                         CLOUDWATCH                            │
│  ┌────────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│  │  LOGS          │  │  METRICS     │  │  ALARMS         │  │
│  │  /aws/lambda/  │  │  AWS/Lambda  │  │  - Upload Error │  │
│  │  - Upload      │  │  - Invoke    │  │  - Process Error│  │
│  │  - Processing  │  │  - Duration  │  │  - Slow Process │  │
│  │  - Status Query│  │  - Errors    │  │  - API Errors   │  │
│  └────────────────┘  └──────────────┘  └────────┬────────┘  │
│                                                   │           │
│  ┌──────────────────────────────────────────────▼────────┐  │
│  │              DASHBOARD (5 widgets)                     │  │
│  │  - Lambda invocations  - Lambda errors                │  │
│  │  - Processing duration - API Gateway metrics          │  │
│  │  - DynamoDB capacity                                   │  │
│  └────────────────────────────────────────────────────────┘  │
└────────────────────────────────────┬─────────────────────────┘
                                     │ Alarm triggers
                                     │
┌────────────────────────────────────▼─────────────────────────┐
│                       SNS TOPIC                               │
│             (MediaProcessor-Alarms-JSavic)                    │
│                                                               │
│  Email Subscription: j.savic@levi9.com                       │
└───────────────────────────────────────────────────────────────┘
```

---

## Component Details

### 1. API Gateway

**Purpose**: Provides HTTP endpoints for client applications

**Configuration**:
- **Type**: REST API (not HTTP API)
- **Stage**: prod
- **CORS**: Enabled for all origins (*)
- **Integration**: Lambda Proxy (AWS_PROXY)

**Endpoints**:

#### POST /upload
- **Method**: POST
- **Request**: JSON body with base64-encoded image
- **Integration**: Synchronous Lambda invocation
- **Timeout**: 29 seconds (API Gateway max)
- **Response**: Job ID and S3 key

#### GET /status/{jobId}
- **Method**: GET
- **Path Parameter**: jobId (required)
- **Integration**: Synchronous Lambda invocation
- **Timeout**: 29 seconds
- **Response**: Job status and metadata

**AWS Concepts**:
- **Lambda Proxy Integration**: Passes full HTTP request to Lambda, Lambda controls response
- **Stages**: Environment separation (dev, test, prod)
- **Resources**: URL path segments organized in tree structure
- **Methods**: HTTP verbs (GET, POST, OPTIONS)

---

### 2. Lambda Functions

#### Upload Lambda
- **Runtime**: .NET 8
- **Memory**: 256 MB
- **Timeout**: 30 seconds
- **Invocation**: Synchronous (API Gateway waits for response)
- **Concurrency**: Automatic scaling (AWS manages)

**Responsibilities**:
1. Request validation (file type, size, base64 format)
2. Job ID generation (GUID)
3. Base64 decode to binary
4. S3 upload with metadata
5. DynamoDB record creation

**Error Handling**:
- Invalid input → 400 Bad Request
- S3 upload failure → 500 Internal Server Error
- DynamoDB write failure → 500 Internal Server Error

#### Processing Lambda
- **Runtime**: .NET 8
- **Memory**: 512 MB (more memory for image processing)
- **Timeout**: 60 seconds
- **Invocation**: Asynchronous (EventBridge triggers, doesn't wait)
- **Concurrency**: Automatic scaling

**Responsibilities**:
1. S3 event parsing
2. DynamoDB status update (Pending → Processing)
3. Image download from S3
4. Image resizing (max 800px width)
5. Artificial delay (30-35s for demo)
6. Processed image upload to output bucket
7. DynamoDB status update (Processing → Completed/Failed)

**Error Handling**:
- S3 download failure → Status: Failed
- Image processing error → Status: Failed
- S3 upload failure → Status: Failed
- All errors logged to CloudWatch

#### Status Query Lambda
- **Runtime**: .NET 8
- **Memory**: 256 MB
- **Timeout**: 10 seconds
- **Invocation**: Synchronous (API Gateway)
- **Concurrency**: Automatic scaling

**Responsibilities**:
1. Job ID extraction from path parameter
2. Job ID validation
3. DynamoDB query (GetItem)
4. Response formatting based on status

**Response Format**:
- **Pending**: Job queued for processing
- **Processing**: Job currently being processed (with time elapsed)
- **Completed**: Success with results (dimensions, duration)
- **Failed**: Error with message

---

### 3. S3 Buckets

#### Input Bucket
- **Name**: `media-processor-input-jsavic-{account-id}`
- **Purpose**: Store original uploaded images
- **Lifecycle**: None (keep forever for demo)
- **Versioning**: Disabled
- **Encryption**: AES-256 (SSE-S3)
- **EventBridge Notifications**: Enabled

**Folder Structure**:
```
jobs/
  └── {jobId}/
      └── {originalFileName}
```

#### Output Bucket
- **Name**: `media-processor-output-jsavic-{account-id}`
- **Purpose**: Store processed images
- **Lifecycle**: None
- **Versioning**: Disabled
- **Encryption**: AES-256 (SSE-S3)

**Folder Structure**:
```
jobs/
  └── {jobId}/
      └── processed_{originalFileName}
```

**AWS Concepts**:
- **Event Notifications**: S3 can trigger EventBridge, SNS, SQS, or Lambda
- **Bucket Naming**: Must be globally unique, lowercase, no underscores
- **Object Keys**: Full path including folders (S3 doesn't have folders, just key prefixes)

---

### 4. DynamoDB Table

**Table Name**: `MediaProcessingJobs-JSavic`

**Schema**:
- **Partition Key**: JobId (String)
- **No Sort Key**: Simple key-value lookup
- **Billing Mode**: On-Demand (pay per request)

**Attributes**:
```typescript
{
  JobId: string,              // UUID (partition key)
  Status: string,             // "Pending" | "Processing" | "Completed" | "Failed"
  UploadedAt: string,         // ISO 8601 timestamp
  ProcessingStartedAt: string?, // ISO 8601 timestamp (optional)
  CompletedAt: string?,       // ISO 8601 timestamp (optional)
  OriginalFileName: string,   // User-provided filename
  FileSize: number,           // Bytes
  FileType: string,           // MIME type (image/jpeg, image/png)
  InputS3Key: string,         // S3 path to original
  OutputS3Key: string?,       // S3 path to processed (optional)
  ProcessedWidth: number?,    // Pixels (optional)
  ProcessedHeight: number?,   // Pixels (optional)
  ErrorMessage: string?       // Error details (optional)
}
```

**Access Patterns**:
1. **GetItem by JobId**: Status Query Lambda → O(1) lookup
2. **PutItem**: Upload Lambda → Create job record
3. **UpdateItem**: Processing Lambda → Update status/results

**AWS Concepts**:
- **On-Demand Billing**: No capacity planning, pay per request
- **Primary Key**: Must be unique, used for all queries
- **Attributes**: Schema-less, can vary per item
- **Strong Consistency**: Default for GetItem (reads latest write)

---

### 5. EventBridge

**Purpose**: Routes S3 events to Processing Lambda

**Event Rule**:
- **Name**: `MediaProcessor-S3-Upload-Rule-JSavic`
- **Event Pattern**:
  ```json
  {
    "source": ["aws.s3"],
    "detail-type": ["Object Created"],
    "detail": {
      "bucket": {
        "name": ["media-processor-input-jsavic-765891906457"]
      }
    }
  }
  ```
- **Target**: Processing Lambda (async invocation)

**Event Flow**:
```
S3 PutObject → EventBridge → Processing Lambda
     ↓
 Event: {
   "source": "aws.s3",
   "detail-type": "Object Created",
   "detail": {
     "bucket": { "name": "..." },
     "object": { "key": "jobs/abc/image.jpg", "size": 12345 }
   }
 }
```

**AWS Concepts**:
- **Event Bus**: Central router for events (default bus used)
- **Rules**: Pattern matching + targets
- **Asynchronous Invocation**: Fire-and-forget, Lambda processes independently

---

## Data Flow

### Complete Request Flow

#### 1. Upload Image Flow

```
Client                API Gateway        Upload Lambda          S3              DynamoDB
  │                        │                   │                │                  │
  │ POST /upload          │                   │                │                  │
  │ {fileName, fileData}  │                   │                │                  │
  ├──────────────────────>│                   │                │                  │
  │                        │ Invoke (sync)     │                │                  │
  │                        ├──────────────────>│                │                  │
  │                        │                   │ Validate       │                  │
  │                        │                   │ Generate JobID │                  │
  │                        │                   │                │                  │
  │                        │                   │ PutObject      │                  │
  │                        │                   ├───────────────>│                  │
  │                        │                   │     200 OK     │                  │
  │                        │                   │<───────────────┤                  │
  │                        │                   │                │                  │
  │                        │                   │ PutItem (Status: Pending)         │
  │                        │                   ├──────────────────────────────────>│
  │                        │                   │               200 OK              │
  │                        │                   │<──────────────────────────────────┤
  │                        │                   │                │                  │
  │                        │  200 OK           │                │                  │
  │                        │  {jobId, s3Key}   │                │                  │
  │                        │<──────────────────┤                │                  │
  │     200 OK            │                   │                │                  │
  │  {jobId, s3Key}       │                   │                │                  │
  │<──────────────────────┤                   │                │                  │
  │                        │                   │                │                  │
```

**Duration**: ~2-3 seconds

#### 2. Background Processing Flow

```
S3              EventBridge      Processing Lambda      S3 Output       DynamoDB
 │                   │                    │                  │              │
 │ ObjectCreated     │                    │                  │              │
 │ Event             │                    │                  │              │
 ├──────────────────>│                    │                  │              │
 │                   │ Trigger (async)    │                  │              │
 │                   ├───────────────────>│                  │              │
 │                   │                    │ UpdateItem       │              │
 │                   │                    │ (Status: Processing)            │
 │                   │                    ├─────────────────────────────────>│
 │                   │                    │                  │    200 OK    │
 │                   │                    │<─────────────────────────────────┤
 │                   │                    │                  │              │
 │                   │                    │ GetObject        │              │
 │                   │                    │ (download image) │              │
 │<──────────────────────────────────────┤                  │              │
 │                   │   200 OK           │                  │              │
 │───────────────────────────────────────>│                  │              │
 │                   │                    │                  │              │
 │                   │                    │ Resize image     │              │
 │                   │                    │ (30-35s delay)   │              │
 │                   │                    │                  │              │
 │                   │                    │      PutObject                  │
 │                   │                    ├─────────────────>│              │
 │                   │                    │      200 OK      │              │
 │                   │                    │<─────────────────┤              │
 │                   │                    │                  │              │
 │                   │                    │ UpdateItem       │              │
 │                   │                    │ (Status: Completed, results)    │
 │                   │                    ├─────────────────────────────────>│
 │                   │                    │                  │    200 OK    │
 │                   │                    │<─────────────────────────────────┤
 │                   │                    │                  │              │
```

**Duration**: ~35-40 seconds

#### 3. Status Query Flow

```
Client          API Gateway      Status Lambda       DynamoDB
  │                  │                  │                │
  │ GET /status/123  │                  │                │
  ├─────────────────>│                  │                │
  │                  │ Invoke (sync)    │                │
  │                  ├─────────────────>│                │
  │                  │                  │ GetItem        │
  │                  │                  │ (JobId: 123)   │
  │                  │                  ├───────────────>│
  │                  │                  │   200 OK       │
  │                  │                  │   {item data}  │
  │                  │                  │<───────────────┤
  │                  │                  │ Format response│
  │                  │  200 OK          │                │
  │                  │  {status, ...}   │                │
  │                  │<─────────────────┤                │
  │    200 OK        │                  │                │
  │  {status, ...}   │                  │                │
  │<─────────────────┤                  │                │
```

**Duration**: ~500ms - 2 seconds

---

## AWS Services Deep Dive

### Lambda Execution Model

**Cold Start vs. Warm Start**:

```
Cold Start (First Invocation):
┌─────────────────────────────────────────┐
│ 1. Provision execution environment      │  ~200-500ms
│    - Allocate compute resources         │
│    - Download Lambda code               │
│    - Initialize runtime (.NET 8)        │
├─────────────────────────────────────────┤
│ 2. Initialize handler                   │  ~100-300ms
│    - Run static constructors            │
│    - Create AWS SDK clients             │
│    - Load dependencies                  │
├─────────────────────────────────────────┤
│ 3. Execute handler method               │  Variable
│    - Your actual code runs              │
└─────────────────────────────────────────┘
Total: 300ms-800ms + execution time

Warm Start (Subsequent Invocations):
┌─────────────────────────────────────────┐
│ Execute handler method                  │  Variable
│ (reuses existing environment)           │
└─────────────────────────────────────────┘
Total: Just execution time
```

**Optimization Tips**:
- Keep handler lightweight
- Initialize SDK clients in constructor (reused across warm invocations)
- Use Lambda layers for common dependencies
- Consider provisioned concurrency for consistent performance

---

### IAM Security Model

**Trust vs. Permission Policies**:

```
┌──────────────────────────────────────────────────────────┐
│                    LAMBDA EXECUTION                       │
│                                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │  Trust Policy (Who can assume this role?)         │  │
│  │  {                                                 │  │
│  │    "Principal": { "Service": "lambda.amazonaws.com"}│ │
│  │    "Action": "sts:AssumeRole"                     │  │
│  │  }                                                 │  │
│  │  ✅ Lambda service can use this role              │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │  Permission Policy (What can this role do?)        │  │
│  │  {                                                 │  │
│  │    "Action": ["s3:PutObject", "dynamodb:PutItem"]│  │
│  │    "Resource": ["arn:aws:s3:::bucket/*", ...]    │  │
│  │  }                                                 │  │
│  │  ✅ Can write to S3 and DynamoDB                  │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

**Least Privilege Principle**:
- Upload Lambda: S3 write (input bucket only), DynamoDB write
- Processing Lambda: S3 read (input), S3 write (output), DynamoDB read/write
- Status Query Lambda: DynamoDB read only

---

## Security Model

### Network Security

- **No VPC**: Lambdas run in AWS-managed VPC (internet access by default)
- **HTTPS Only**: All API calls encrypted in transit
- **No Public IPs**: S3 and DynamoDB accessed via AWS internal network

### Authentication & Authorization

- **API Gateway**: No authentication (public endpoints for learning)
- **Lambda→AWS Services**: IAM roles
- **Temporary Credentials**: STS tokens with expiration

### Data Security

- **At Rest**:
  - S3: AES-256 encryption (SSE-S3)
  - DynamoDB: Encrypted by default
  - CloudWatch Logs: Encrypted

- **In Transit**:
  - Client→API Gateway: HTTPS (TLS 1.2+)
  - Lambda→AWS Services: HTTPS
  - Internal AWS: Encrypted

---

## Scalability & Performance

### Automatic Scaling

| Component | Scaling Strategy |
|-----------|------------------|
| Lambda | Automatic (up to account limit: 1000 concurrent) |
| API Gateway | Automatic (10,000 RPS default) |
| DynamoDB | On-demand (automatic) |
| S3 | Unlimited |
| EventBridge | Automatic |

### Performance Characteristics

| Operation | Typical Duration | Bottleneck |
|-----------|------------------|------------|
| Upload | 2-3 seconds | Base64 decode + S3 upload |
| Processing | 35-40 seconds | Artificial delay + image processing |
| Status Query | 500ms-2s | DynamoDB query |

### Cost at Scale

**10,000 images/month**:
- Lambda: ~$2
- API Gateway: ~$0.04
- S3: ~$1
- DynamoDB: ~$3
- CloudWatch: ~$5
- **Total: ~$11/month**

---

## Design Decisions

### Why These Patterns?

1. **Asynchronous Processing**:
   - Decouples upload from processing
   - User gets immediate response
   - Processing can take longer without API timeout

2. **EventBridge vs. Direct S3→Lambda**:
   - EventBridge provides centralized event routing
   - Easier to add more targets later
   - Better observability and filtering

3. **DynamoDB vs. RDS**:
   - No server management
   - Auto-scaling
   - Single-digit millisecond latency
   - Perfect for key-value lookups

4. **On-Demand DynamoDB**:
   - Learning project with unpredictable traffic
   - No capacity planning needed
   - Pay per request

5. **Separate S3 Buckets**:
   - Clear separation of concerns
   - Different lifecycle policies possible
   - Better security (different IAM policies)

### Trade-offs

| Decision | Pro | Con |
|----------|-----|-----|
| No authentication | Simple for learning | Not production-ready |
| 30s artificial delay | Demonstrates async processing | Wastes Lambda time |
| No VPC | Simpler setup, internet access | Can't access private resources |
| Public API | Easy testing | Security risk for production |

---

## Future Enhancements

Potential improvements for production:

1. **Authentication**: API keys, Cognito, or OAuth
2. **Validation**: Enhanced input validation, file type checking
3. **Virus Scanning**: Integrate with antivirus API
4. **Thumbnails**: Generate multiple sizes
5. **CDN**: CloudFront for serving processed images
6. **Dead Letter Queue**: Capture failed processing for retry
7. **Step Functions**: Orchestrate complex workflows
8. **Presigned URLs**: Direct S3 upload (bypass Lambda)

---

This architecture demonstrates real-world serverless patterns while remaining simple enough for learning. Each component can be understood independently while seeing how they work together as a system.
