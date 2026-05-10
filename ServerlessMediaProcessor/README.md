# Serverless Media Processor

A production-grade serverless application built on AWS for learning cloud architecture, serverless computing, and Infrastructure as Code (IaC).

## 📋 Project Overview

This project implements a complete serverless image processing pipeline using AWS services. It demonstrates real-world cloud architecture patterns including event-driven processing, RESTful APIs, monitoring, and observability.

### What It Does

1. **Upload**: Users upload images via REST API
2. **Process**: Images are automatically processed (resized) in the background
3. **Track**: Users can query processing status and results in real-time
4. **Monitor**: Complete observability with CloudWatch dashboards and alarms

---

## 🏗️ Architecture

### High-Level Architecture Diagram

```
┌─────────────┐
│   Client    │
│  (Insomnia) │
└──────┬──────┘
       │ HTTPS
       ▼
┌─────────────────────────────────────────────────────────────┐
│                     API Gateway                             │
│  POST /upload          GET /status/{jobId}                 │
└────┬────────────────────────────────┬────────────────────────┘
     │                                 │
     ▼                                 ▼
┌──────────────────┐          ┌──────────────────┐
│ Upload Lambda    │          │ Status Lambda    │
│ - Validate image │          │ - Query DynamoDB │
│ - Upload to S3   │          │ - Return status  │
│ - Write DynamoDB │          └────────┬─────────┘
└────┬─────────────┘                   │
     │                                  │
     ▼                                  ▼
┌──────────────────┐          ┌──────────────────┐
│  S3 Input Bucket │          │    DynamoDB      │
│  (Raw images)    │          │  (Job metadata)  │
└────┬─────────────┘          └──────────────────┘
     │
     │ S3 Event
     ▼
┌──────────────────┐
│   EventBridge    │
└────┬─────────────┘
     │
     │ Trigger
     ▼
┌──────────────────────┐
│ Processing Lambda    │
│ - Download from S3   │
│ - Resize image       │
│ - Upload to output   │
│ - Update DynamoDB    │
└────┬─────────────────┘
     │
     ▼
┌──────────────────┐
│ S3 Output Bucket │
│ (Processed imgs) │
└──────────────────┘

         │
         │ All Lambdas send logs
         ▼
┌──────────────────────────────────────────┐
│           CloudWatch                      │
│  - Logs                                  │
│  - Metrics                               │
│  - Alarms → SNS → Email                 │
│  - Dashboard                             │
└──────────────────────────────────────────┘
```

---

## 🛠️ Tech Stack

### AWS Services Used

| Service | Purpose | Key Learning |
|---------|---------|--------------|
| **Lambda** | Serverless compute | Event-driven architecture, cold starts, scaling |
| **API Gateway** | REST API endpoints | Lambda integration, CORS, path parameters |
| **S3** | Object storage | Bucket policies, event notifications |
| **DynamoDB** | NoSQL database | Key-value storage, on-demand billing |
| **EventBridge** | Event routing | S3 event triggers, event-driven patterns |
| **CloudWatch** | Monitoring & logging | Logs, metrics, alarms, dashboards |
| **SNS** | Notifications | Pub/sub messaging, email alerts |
| **IAM** | Security & permissions | Roles, policies, least privilege |

### Development Stack

- **Language**: C# / .NET 8
- **IaC**: AWS CDK (Cloud Development Kit)
- **Libraries**: 
  - AWS SDK for .NET (S3, DynamoDB)
  - Amazon.Lambda.* (Lambda runtime & events)
  - SixLabors.ImageSharp (image processing)
- **Tools**: AWS CLI, PowerShell, Insomnia

---

## 📊 Project Statistics

- **3 Lambda Functions**: Upload, Processing, Status Query
- **2 S3 Buckets**: Input and Output storage
- **1 DynamoDB Table**: Job metadata tracking
- **1 REST API**: 2 endpoints (POST /upload, GET /status/{jobId})
- **4 CloudWatch Alarms**: Error and performance monitoring
- **1 CloudWatch Dashboard**: 5 widgets tracking system health
- **~$0.90/month**: Estimated cost for low usage

---

## 🚀 Getting Started

### Prerequisites

- AWS Account with administrator access
- .NET 8 SDK
- Node.js (for AWS CDK)
- AWS CLI configured
- Git

### Installation

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd ServerlessMediaProcessor
   ```

2. **Install dependencies**
   ```bash
   # Install CDK globally
   npm install -g aws-cdk

   # Restore .NET packages
   dotnet restore
   ```

3. **Configure AWS credentials**
   ```bash
   aws configure
   # Or use AWS SSO: aws sso login
   ```

4. **Bootstrap CDK** (first time only)
   ```bash
   cd Infrastructure
   cdk bootstrap
   ```

### Deployment

**Note**: Due to Docker requirements for CDK Lambda bundling, this project uses manual AWS CLI deployment. See [DEPLOYMENT-WORKFLOW.md](./DEPLOYMENT-WORKFLOW.md) for details.

**Quick Deploy**:
```bash
# Build Lambda handlers
cd LambdaHandlers
dotnet publish -c Release -o bin/Release/net8.0/publish

# Package and deploy (see deployment scripts in root)
# Follow instructions in DEPLOYMENT-WORKFLOW.md
```

For detailed deployment steps, see:
- [DEPLOYMENT-WORKFLOW.md](./DEPLOYMENT-WORKFLOW.md) - When to use CDK vs AWS CLI
- [CloudWatch Setup](./CLOUDWATCH-SUMMARY.md) - Monitoring deployment

---

## 📖 Documentation

### Core Documentation

- **[ARCHITECTURE.md](./ARCHITECTURE.md)** - Detailed system design and AWS concepts
- **[API-REFERENCE.md](./API-REFERENCE.md)** - REST API endpoints and usage
- **[DEPLOYMENT-WORKFLOW.md](./DEPLOYMENT-WORKFLOW.md)** - Deployment guide and troubleshooting

### CloudWatch & Monitoring

- **[CLOUDWATCH-GUIDE.md](./CLOUDWATCH-GUIDE.md)** - Complete CloudWatch concepts guide
- **[CLOUDWATCH-SUMMARY.md](./CLOUDWATCH-SUMMARY.md)** - Monitoring implementation details
- **[CloudWatch-Monitoring.psm1](./CloudWatch-Monitoring.psm1)** - PowerShell monitoring tools

### Additional Resources

- **[STATUS-QUERY-SUMMARY.md](./STATUS-QUERY-SUMMARY.md)** - Status endpoint implementation

---

## 🧪 Testing

### Manual API Testing with Insomnia

**1. Upload an Image**
```http
POST https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/upload
Content-Type: application/json

{
  "fileName": "test-image.png",
  "fileType": "image/png",
  "fileData": "<base64-encoded-image>"
}
```

**Response**:
```json
{
  "message": "Upload successful",
  "jobId": "abc-123-def-456",
  "s3Key": "jobs/abc-123-def-456/test-image.png"
}
```

**2. Check Processing Status**
```http
GET https://xkl2fk7jbb.execute-api.eu-west-1.amazonaws.com/prod/status/abc-123-def-456
```

**Response**:
```json
{
  "jobId": "abc-123-def-456",
  "status": "Completed",
  "fileName": "test-image.png",
  "fileType": "image/png",
  "fileSize": 12345,
  "uploadedAt": "2026-05-10T10:00:00Z",
  "processingStartedAt": "2026-05-10T10:00:05Z",
  "completedAt": "2026-05-10T10:00:40Z",
  "processingDuration": "35.2 seconds",
  "processedDimensions": "800x600",
  "message": "Job completed successfully"
}
```

### Monitoring with PowerShell

```powershell
# Import monitoring module
Import-Module .\CloudWatch-Monitoring.psm1

# Get system health
Get-SystemHealth

# View recent logs
Get-ProcessingLogs -Since "1h"

# Search for specific job
Search-LogsForJobId -JobId "abc-123-def-456"
```

---

## 📊 Monitoring & Observability

### CloudWatch Dashboard

View real-time metrics:
```
https://eu-west-1.console.aws.amazon.com/cloudwatch/home?region=eu-west-1#dashboards:name=MediaProcessor-Dashboard-JSavic
```

Or use PowerShell:
```powershell
Open-CloudWatchDashboard
```

### Alarms

4 CloudWatch Alarms monitor system health:

1. **Upload Errors** - Triggers if upload Lambda fails
2. **Processing Errors** - Triggers if processing Lambda has >3 errors in 5 min
3. **Slow Processing** - Triggers if processing takes >45 seconds
4. **API Gateway Errors** - Triggers if API has >5 server errors in 5 min

Email notifications sent via SNS to: `j.savic@levi9.com`

---

## 🎓 Learning Outcomes

### AWS Concepts Mastered

#### Core Services
- ✅ Lambda functions (event handlers, cold starts, execution context)
- ✅ API Gateway (REST APIs, Lambda proxy integration, CORS)
- ✅ S3 (bucket policies, event notifications, presigned URLs)
- ✅ DynamoDB (key-value storage, on-demand billing, GetItem/PutItem)
- ✅ EventBridge (event routing, S3 event triggers)

#### Observability
- ✅ CloudWatch Logs (log groups, streams, retention)
- ✅ CloudWatch Metrics (namespaces, dimensions, statistics)
- ✅ CloudWatch Alarms (thresholds, evaluation periods, actions)
- ✅ CloudWatch Dashboards (widgets, metric queries)
- ✅ SNS (topics, subscriptions, email notifications)

#### Security & IAM
- ✅ IAM Roles (trust policies, assume role)
- ✅ IAM Policies (permissions, least privilege, managed vs inline)
- ✅ Resource-based policies (Lambda permissions, S3 bucket policies)
- ✅ Temporary credentials (STS, session tokens)

#### Architecture Patterns
- ✅ Event-driven architecture (decoupled components)
- ✅ Asynchronous processing (background jobs)
- ✅ RESTful API design (status codes, path parameters)
- ✅ Serverless patterns (stateless functions, managed services)

---

## 💰 Cost Breakdown

### Monthly Estimate (Low Usage - ~100 images/month)

| Service | Usage | Cost |
|---------|-------|------|
| Lambda (3 functions) | 100 invocations | ~$0.00 (Free tier) |
| API Gateway | 100 requests | ~$0.00 (Free tier) |
| S3 Storage | 1GB | ~$0.02 |
| S3 Requests | 200 requests | ~$0.00 |
| DynamoDB | 100 reads/writes | ~$0.00 (Free tier) |
| CloudWatch Logs | 1GB | ~$0.50 |
| CloudWatch Alarms | 4 alarms | ~$0.40 |
| CloudWatch Dashboard | 1 dashboard | FREE |
| EventBridge | 100 events | ~$0.00 (Free tier) |
| SNS | Email notifications | FREE |
| **TOTAL** | | **~$0.92/month** |

### Cost Optimization Tips

1. Set CloudWatch log retention to 7-30 days (not forever)
2. Use S3 Lifecycle policies to archive old images
3. Delete test data regularly
4. Use DynamoDB on-demand (pay per request, not provisioned)

---

## 🔧 Troubleshooting

### Common Issues

**Problem**: Lambda returns 500 error  
**Solution**: Check CloudWatch Logs: `Get-ProcessingLogs -Since "10m"`

**Problem**: "Missing Authentication Token" in API Gateway  
**Solution**: Verify URL includes `/prod/` stage: `.../prod/upload`

**Problem**: Processing Lambda not triggered  
**Solution**: Check S3 EventBridge notification is enabled

**Problem**: IAM permission errors  
**Solution**: Verify IAM roles have correct policies, wait 1-2 minutes for propagation

**Problem**: Expired AWS credentials  
**Solution**: Run `.\update-creds.ps1` or `aws sso login`

For detailed troubleshooting, see [DEPLOYMENT-WORKFLOW.md](./DEPLOYMENT-WORKFLOW.md).

---

## 📁 Project Structure

```
ServerlessMediaProcessor/
├── Infrastructure/              # AWS CDK Infrastructure code
│   ├── InfrastructureStack.cs  # Main CDK stack definition
│   ├── Program.cs              # CDK app entry point
│   └── cdk.json                # CDK configuration
│
├── LambdaHandlers/             # Lambda function code
│   ├── Handlers/               # Lambda handler classes
│   │   ├── ImageUploadHandler.cs
│   │   ├── ImageProcessingHandler.cs
│   │   └── StatusQueryHandler.cs
│   └── Models/                 # Data models
│       └── ProcessingMetadata.cs
│
├── cloudwatch-alarms/          # CloudWatch alarm definitions
│   ├── upload-errors-alarm.json
│   ├── processing-errors-alarm.json
│   ├── slow-processing-alarm.json
│   └── api-errors-alarm.json
│
├── Documentation/              # Project documentation
│   ├── README.md              # This file
│   ├── ARCHITECTURE.md        # Detailed architecture
│   ├── API-REFERENCE.md       # API documentation
│   ├── DEPLOYMENT-WORKFLOW.md # Deployment guide
│   ├── CLOUDWATCH-GUIDE.md    # CloudWatch concepts
│   └── CLOUDWATCH-SUMMARY.md  # Monitoring setup
│
├── CloudWatch-Monitoring.psm1  # PowerShell monitoring module
├── query-commands.ps1          # DynamoDB/CloudWatch queries
├── update-creds.ps1            # AWS credential helper
└── cloudwatch-dashboard.json   # Dashboard definition
```

---

## 🤝 Contributing

This is a learning project. Feel free to:
- Experiment with code
- Add new features
- Improve documentation
- Share what you learned!

---

## 📝 License

This project is for educational purposes.

---

## 🙏 Acknowledgments

Built as part of AWS upskilling journey to learn:
- Serverless architecture
- Infrastructure as Code
- AWS services and best practices
- Cloud monitoring and observability

---

## 🔗 Useful Links

- [AWS Lambda Documentation](https://docs.aws.amazon.com/lambda/)
- [AWS CDK Documentation](https://docs.aws.amazon.com/cdk/)
- [AWS Well-Architected Framework](https://aws.amazon.com/architecture/well-architected/)
- [Serverless Patterns Collection](https://serverlessland.com/patterns)

---

## 📞 Support

For questions about AWS concepts covered in this project, refer to:
- [CLOUDWATCH-GUIDE.md](./CLOUDWATCH-GUIDE.md) - Monitoring concepts
- [ARCHITECTURE.md](./ARCHITECTURE.md) - System design
- [API-REFERENCE.md](./API-REFERENCE.md) - API usage

---

**Built with ❤️ for learning AWS**
