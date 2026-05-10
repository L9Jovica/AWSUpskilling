# AWS Deployment Workflow Guide

## Overview

This guide explains **when and how** to deploy changes to AWS, focusing on the differences between **AWS CDK deployments** and **manual AWS CLI updates**. Understanding this is critical for efficient development and avoiding deployment issues.

---

## 📋 Table of Contents

1. [Types of Changes](#types-of-changes)
2. [CDK Deployment (Infrastructure)](#cdk-deployment-infrastructure)
3. [Manual Deployment (Lambda Code)](#manual-deployment-lambda-code)
4. [When to Use Which Method](#when-to-use-which-method)
5. [Common Pitfalls & Lessons Learned](#common-pitfalls--lessons-learned)
6. [Deployment Checklist](#deployment-checklist)

---

## Types of Changes

### 1. Infrastructure Changes
- Creating/modifying AWS resources (S3, DynamoDB, Lambda, API Gateway, EventBridge, IAM roles)
- Changing resource properties (timeout, memory, environment variables)
- Updating IAM permissions
- Adding/removing resources

### 2. Code Changes
- Modifying Lambda handler logic
- Updating dependencies (NuGet packages)
- Fixing bugs in Lambda code

### 3. Configuration Changes
- API Gateway routes
- EventBridge rules
- S3 bucket policies

---

## CDK Deployment (Infrastructure)

### What CDK Manages

AWS CDK uses **CloudFormation** under the hood to manage infrastructure as code. CDK automatically:

1. **Tracks all resources** defined in `InfrastructureStack.cs`
2. **Creates IAM roles** with proper permissions
3. **Manages resource dependencies** (ensures resources are created in the correct order)
4. **Handles updates** (detects what changed and only updates those resources)
5. **Rolls back on failure** (if deployment fails, CloudFormation rolls back)

### CDK Workflow

```bash
# 1. Check what will change (ALWAYS run this first!)
cdk diff

# 2. Generate CloudFormation template (optional, for review)
cdk synth

# 3. Deploy changes to AWS
cdk deploy
```

### How CDK Detects Changes

CDK uses **hashing** to detect changes:

- **Infrastructure changes**: CDK compares your code with the deployed CloudFormation stack
- **Lambda code changes**: CDK hashes the Lambda source directory (`LambdaHandlers/`)
  - If the hash changes, CDK packages and deploys new Lambda code
  - **Problem**: CDK's hash detection can miss changes or be slow

### When CDK Deploys Lambdas

CDK will update Lambda functions when:

1. **Handler code changes** (files in `LambdaHandlers/`)
2. **Dependencies change** (`.csproj` file modified)
3. **Lambda properties change** (memory, timeout, environment variables)
4. **IAM role changes**

### CDK Deployment Example

```csharp
// In InfrastructureStack.cs
var uploadFunction = new Function(this, "UploadFunction", new FunctionProps
{
    Runtime = Runtime.DOTNET_8,
    Handler = "LambdaHandlers::LambdaHandlers.Handlers.ImageUploadHandler::HandleUploadAsync",
    Code = Code.FromAsset("../LambdaHandlers", new AssetOptions
    {
        Bundling = new BundlingOptions
        {
            Image = Runtime.DOTNET_8.BundlingImage,
            Command = new[]
            {
                "/bin/bash", "-c",
                "dotnet publish -c Release -o /asset-output"
            }
        }
    }),
    MemorySize = 512,
    Timeout = Duration.Seconds(30),
    Environment = new Dictionary<string, string>
    {
        ["INPUT_BUCKET_NAME"] = inputBucket.BucketName,
        ["DYNAMODB_TABLE_NAME"] = jobsTable.TableName
    }
});

// CDK automatically creates an IAM role for this Lambda
inputBucket.GrantWrite(uploadFunction);
jobsTable.GrantWriteData(uploadFunction);
```

**What happens when you run `cdk deploy`:**

1. CDK compiles your C# infrastructure code
2. Generates a CloudFormation template
3. Compares with the deployed stack
4. Builds Lambda code (`dotnet publish`)
5. Packages Lambda code (creates `.zip`)
6. Uploads `.zip` to CDK's S3 staging bucket
7. Creates/updates Lambda function
8. Creates/updates IAM role with correct permissions
9. Creates/updates other resources (S3, DynamoDB, API Gateway, EventBridge)

---

## Manual Deployment (Lambda Code)

### When to Use Manual Deployment

Manual deployment is **faster for development** when you're iterating on Lambda code:

- Quick bug fixes
- Testing code changes
- Rapid development iteration

### Manual Deployment Workflow

```bash
# 1. Navigate to Lambda project
cd LambdaHandlers

# 2. Build and publish Lambda
dotnet publish -c Release -o bin/Release/net8.0/publish

# 3. Package Lambda (create .zip)
cd bin/Release/net8.0/publish
Compress-Archive -Path * -DestinationPath lambda.zip -Force

# 4. Update Lambda function code
aws lambda update-function-code `
  --function-name MediaProcessor-UploadHandler-JSavic `
  --zip-file fileb://lambda.zip

# 5. Wait for update to complete
aws lambda wait function-updated `
  --function-name MediaProcessor-UploadHandler-JSavic

# 6. Verify update
aws lambda get-function-configuration `
  --function-name MediaProcessor-UploadHandler-JSavic `
  --query "LastUpdateStatus"
```

### What Manual Deployment DOES NOT Update

❌ Infrastructure resources (S3, DynamoDB, API Gateway)
❌ IAM roles and permissions
❌ Lambda configuration (memory, timeout, environment variables)
❌ EventBridge rules
❌ API Gateway routes

✅ **Only updates**: Lambda function code

---

## When to Use Which Method

### Use CDK (`cdk deploy`) When:

| Scenario | Why CDK? |
|----------|----------|
| Creating new resources | CDK manages all dependencies |
| Changing Lambda configuration | Memory, timeout, env vars need CDK |
| Updating IAM permissions | CDK ensures correct role policies |
| Adding API Gateway routes | CDK manages API configuration |
| First deployment | CDK sets up everything |
| Production deployments | CDK provides rollback safety |
| Changing infrastructure | Only CDK can modify infrastructure |

### Use Manual Deployment (`aws lambda update-function-code`) When:

| Scenario | Why Manual? |
|----------|-------------|
| Bug fix in Lambda code | Faster than CDK |
| Testing code changes | Quick iteration |
| Development phase | Rapid feedback loop |
| Code-only changes | No infrastructure changes |

---

## Common Pitfalls & Lessons Learned

### 1. ❌ Creating Resources Manually (Not CDK)

**Problem:** We created the Processing Lambda manually via AWS CLI:

```bash
aws lambda create-function --function-name MediaProcessor-ProcessingHandler-JSavic ...
```

**What Went Wrong:**
- Lambda used the wrong IAM role (Upload Lambda's role!)
- EventBridge notifications weren't configured
- DynamoDB table name mismatch in permissions
- IAM permissions were incomplete

**Lesson:** Always use CDK for infrastructure. Manual creation leads to:
- Missing permissions
- Configuration drift
- Hard to track resources
- No automated updates

---

### 2. ❌ IAM Permission Caching

**Problem:** After updating IAM permissions, the Lambda still failed with permission errors.

**Why:** Lambda caches IAM credentials in the execution environment (for performance).

**Solutions:**
1. **Wait for cold start**: Lambda gets new credentials on next cold start
2. **Trigger new execution**: Upload a new test file
3. **Update Lambda config**: Forces new deployment

```bash
# Force new execution environment
aws lambda update-function-configuration `
  --function-name MediaProcessor-ProcessingHandler-JSavic `
  --description "Force update to refresh IAM credentials"
```

**Lesson:** IAM changes take effect immediately in AWS, but Lambda caches credentials. Force a new execution environment to pick up new permissions.

---

### 3. ❌ S3 EventBridge Notification Not Enabled

**Problem:** EventBridge rule was created, but S3 wasn't sending events to EventBridge.

**Why:** S3 buckets need **explicit configuration** to send events to EventBridge.

**Fix:**
```bash
aws s3api put-bucket-notification-configuration `
  --bucket media-processor-input-jsavic-765891906457 `
  --notification-configuration '{"EventBridgeConfiguration": {}}'
```

**Lesson:** S3 → EventBridge requires **two** configurations:
1. S3 bucket notification configuration (enables EventBridge)
2. EventBridge rule (matches events and triggers targets)

In CDK, use:
```csharp
inputBucket.EnableEventBridgeNotification();
```

---

### 4. ❌ DynamoDB Table Name Mismatch

**Problem:** IAM policy referenced wrong table name:
- Policy said: `MediaProcessor-Jobs-JSavic`
- Actual table: `MediaProcessingJobs-JSavic`

**Why:** Manual IAM policy creation led to typo.

**Lesson:** Use CDK references:
```csharp
// ✅ Good: CDK uses actual table name
jobsTable.GrantReadWriteData(processingFunction);

// ❌ Bad: Manual policy with hardcoded name
new PolicyStatement(new PolicyStatementProps
{
    Actions = new[] { "dynamodb:PutItem", "dynamodb:UpdateItem" },
    Resources = new[] { "arn:aws:dynamodb:eu-west-1:765891906457:table/MediaProcessor-Jobs-JSavic" }
});
```

---

### 5. ❌ Lambda Code Bundling Issues

**Problem:** Lambda failed to start with "missing .deps.json" error.

**Why:** Lambda needs:
- `.dll` files (compiled code)
- `.deps.json` (dependency manifest)
- `.runtimeconfig.json` (runtime configuration)

**Fix:**
1. Add to `.csproj`:
```xml
<PropertyGroup>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AWSProjectType>Lambda</AWSProjectType>
</PropertyGroup>
```

2. Add CDK bundling options:
```csharp
Code = Code.FromAsset("../LambdaHandlers", new AssetOptions
{
    Bundling = new BundlingOptions
    {
        Image = Runtime.DOTNET_8.BundlingImage,
        Command = new[] { "/bin/bash", "-c", "dotnet publish -c Release -o /asset-output" }
    }
})
```

**Lesson:** Use `dotnet publish` (not `dotnet build`) for Lambda deployment. `publish` includes all runtime files.

---

## Deployment Checklist

### Before Deployment

- [ ] Run `cdk diff` to review changes
- [ ] Check if changes are infrastructure or code-only
- [ ] Review IAM permission changes
- [ ] Test locally if possible

### Infrastructure Changes (CDK)

- [ ] Update `InfrastructureStack.cs`
- [ ] Run `cdk diff` to preview changes
- [ ] Run `cdk deploy`
- [ ] Verify deployment success
- [ ] Test the deployed resources

### Lambda Code Changes (Manual)

- [ ] Build: `dotnet publish -c Release`
- [ ] Package: `Compress-Archive -Path * -DestinationPath lambda.zip`
- [ ] Deploy: `aws lambda update-function-code --function-name <name> --zip-file fileb://lambda.zip`
- [ ] Wait: `aws lambda wait function-updated --function-name <name>`
- [ ] Test: Trigger Lambda and check logs

### After Deployment

- [ ] Check CloudWatch Logs for errors
- [ ] Verify resource creation/update in AWS Console
- [ ] Test end-to-end workflow
- [ ] Update documentation if needed

---

## Decision Flow

```
┌─────────────────────────────┐
│  What are you changing?     │
└──────────┬──────────────────┘
           │
           ├──► Infrastructure (S3, DynamoDB, IAM, API Gateway, EventBridge)
           │    └──► Use CDK (`cdk deploy`)
           │
           ├──► Lambda Code Only (bug fix, logic change)
           │    └──► Use Manual (`aws lambda update-function-code`)
           │
           ├──► Lambda Config (memory, timeout, env vars)
           │    └──► Use CDK (`cdk deploy`)
           │
           └──► Production Deployment
                └──► Always use CDK (rollback safety)
```

---

## Summary

| Method | Speed | Safety | Use Case |
|--------|-------|--------|----------|
| **CDK** | Slower | ✅ Safer | Infrastructure, IAM, Configuration, Production |
| **Manual** | ⚡ Faster | ⚠️ Risky | Development, Code-only changes, Quick fixes |

**Golden Rule:** 
- **Development**: Manual for Lambda code iteration
- **Production**: Always use CDK for all changes

---

## Additional Resources

- [AWS CDK Documentation](https://docs.aws.amazon.com/cdk/)
- [AWS Lambda Best Practices](https://docs.aws.amazon.com/lambda/latest/dg/best-practices.html)
- [EventBridge with S3](https://docs.aws.amazon.com/AmazonS3/latest/userguide/EventBridge.html)

---

**Last Updated:** 2026-05-07  
**Project:** Serverless Media Processor  
**Author:** AWS Upskilling Project
