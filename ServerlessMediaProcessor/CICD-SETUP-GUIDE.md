# CI/CD Pipeline Setup Guide

## 🎓 What You're About to Set Up

This guide walks you through creating an **automated CI/CD pipeline** that:
- ✅ Watches your GitHub repository for changes
- ✅ Automatically builds and tests your code
- ✅ Deploys to Staging environment
- ✅ Waits for your approval
- ✅ Deploys to Production environment

**Time to complete**: ~30 minutes  
**Cost**: ~$2-3/month  

---

## 📋 Prerequisites

Before starting, ensure you have:

1. ✅ AWS CLI configured with credentials
2. ✅ AWS CDK installed (`npm install -g aws-cdk`)
3. ✅ Git repository: https://github.com/L9Jovica/AWSUpskilling
4. ✅ Code pushed to GitHub main branch
5. ✅ AWS account with admin permissions

---

## 🚀 Step-by-Step Setup

### Step 1: Push Your Code to GitHub (If Not Already Done)

**What this does**: Makes your code available for CodePipeline to access

```bash
# Navigate to your project
cd C:\GitHub\Personal Jovica\AWSUpskilling\AWSUpskilling\ServerlessMediaProcessor

# Initialize git (if not already done)
git init

# Add all files
git add .

# Commit
git commit -m "Add CI/CD pipeline infrastructure"

# Add remote (if not already done)
git remote add origin https://github.com/L9Jovica/AWSUpskilling.git

# Push to main branch
git push -u origin main
```

**AWS Concept**: CodePipeline needs your code in GitHub to watch for changes via webhooks.

---

### Step 2: Create GitHub Connection in AWS

**What this does**: Allows AWS to securely access your GitHub repository

#### Option A: Using AWS Console (Recommended)

1. Open AWS Console: https://console.aws.amazon.com/codesuite/settings/connections
2. Click **"Create connection"**
3. Select **"GitHub"** as provider
4. Connection name: `github-media-processor`
5. Click **"Connect to GitHub"**
6. Authorize AWS in GitHub popup
7. Select your repository: `L9Jovica/AWSUpskilling`
8. Click **"Connect"**
9. **IMPORTANT**: Copy the Connection ARN (looks like: `arn:aws:codestar-connections:eu-west-1:123456789:connection/abc-123`)

#### Option B: Using AWS CLI

```bash
# Create connection
aws codestar-connections create-connection \
  --provider-type GitHub \
  --connection-name github-media-processor

# This returns a Connection ARN - save it!
# Then complete authorization in AWS Console
```

**AWS Concept**: CodeStar Connections use OAuth (more secure than personal access tokens). Connection only needs to be created once.

---

### Step 3: Deploy the Pipeline Stack

**What this does**: Creates CodePipeline, CodeBuild, S3 bucket, SNS topic, IAM roles

```bash
# Navigate to Infrastructure directory
cd Infrastructure

# Install/update CDK dependencies (if needed)
dotnet restore

# Bootstrap CDK (one-time setup per AWS account/region)
cdk bootstrap

# Deploy the pipeline
cdk deploy PipelineStack
```

**What gets created**:
- CodePipeline with 5 stages
- CodeBuild project
- S3 bucket for artifacts
- SNS topic for notifications
- IAM roles and policies
- CloudWatch log groups

**Deployment time**: ~3-5 minutes

**AWS Concept**: CDK generates CloudFormation template and creates all resources automatically.

---

### Step 4: Update GitHub Connection (First Time Only)

**What this does**: Activates the connection between AWS and GitHub

After deploying the pipeline, the GitHub connection needs to be activated:

1. Go to: https://console.aws.amazon.com/codesuite/settings/connections
2. Find connection: `github-media-processor`
3. Status will be **"PENDING"**
4. Click **"Update pending connection"**
5. Click **"Install a new app"** (if first time)
6. Select `L9Jovica` account
7. Select `AWSUpskilling` repository
8. Click **"Connect"**
9. Status changes to **"AVAILABLE"** ✅

**AWS Concept**: This one-time setup creates a GitHub webhook that notifies AWS when you push code.

---

### Step 5: Trigger Your First Pipeline Run

**What this does**: Tests the entire CI/CD pipeline end-to-end

#### Method 1: Push to GitHub (Automatic Trigger)

```bash
# Make a small change
echo "# CI/CD Pipeline Active" >> README.md

# Commit and push
git add .
git commit -m "Test CI/CD pipeline"
git push origin main
```

**What happens**:
1. GitHub webhook notifies CodePipeline
2. Pipeline automatically starts
3. Watch progress in AWS Console!

#### Method 2: Manual Trigger (AWS Console)

1. Go to: https://console.aws.amazon.com/codesuite/codepipeline/pipelines
2. Click **"MediaProcessor-Pipeline"**
3. Click **"Release change"**
4. Pipeline starts running!

**AWS Concept**: Pipelines can be triggered automatically (git push) or manually (AWS Console button).

---

### Step 6: Watch Your Pipeline Run

**What this does**: Monitor the build and deployment process

Open Pipeline Console:
```
https://eu-west-1.console.aws.amazon.com/codesuite/codepipeline/pipelines/MediaProcessor-Pipeline/view
```

**You'll see 5 stages**:

1. **Source** (30 seconds)
   - Status: In Progress → Downloads code from GitHub
   - Status: Succeeded → Code downloaded ✅

2. **Build** (5 minutes)
   - Status: In Progress → CodeBuild compiling code
   - Click **"Details"** to see live build logs
   - Status: Succeeded → Code built and tested ✅

3. **Deploy-Staging** (5 minutes)
   - Status: In Progress → Deploying to staging
   - Updates Lambda functions
   - Updates infrastructure
   - Status: Succeeded → Staging deployed ✅

4. **Approval** (Waiting for You!)
   - Status: Waiting for Approval
   - **YOU MUST APPROVE MANUALLY**
   - Click **"Review"** → **"Approve"**

5. **Deploy-Production** (5 minutes)
   - Status: In Progress → Deploying to production
   - Status: Succeeded → Production live! ✅

**Total time**: ~15 minutes + your approval time

**AWS Concept**: Each stage must succeed before next stage starts. If any stage fails, pipeline stops.

---

## 🎓 Understanding Each Stage

### Stage 1: Source (GitHub)

**What it does**: Downloads latest code from GitHub

**Behind the scenes**:
```
1. GitHub webhook fires: "New commit!"
2. CodePipeline receives webhook
3. CodePipeline downloads code from GitHub
4. Code stored in S3 artifact: s3://artifacts/source/code.zip
```

**Logs**: Not much to see here (just downloads code)

**If it fails**: Check GitHub connection status

---

### Stage 2: Build (CodeBuild)

**What it does**: Compiles code, runs tests, creates deployment packages

**Behind the scenes**:
```
1. CodeBuild spins up Docker container
2. Container has .NET 8 SDK installed
3. Runs commands from buildspec.yml:
   - dotnet restore (download NuGet packages)
   - dotnet build (compile C# code)
   - dotnet test (run unit tests)
   - dotnet publish (create Lambda packages)
   - zip files (create deployment ZIPs)
4. Uploads artifacts to S3
5. Container destroyed
```

**Logs to watch**:
```bash
# Click "Details" in Build stage to see:

===== Phase: INSTALL =====
Installing .NET 8 SDK...
✅ .NET 8.0.26 installed

===== Phase: PRE_BUILD =====
Restoring NuGet packages...
✅ Packages restored

===== Phase: BUILD =====
Building solution...
✅ Build succeeded: 0 errors, 6 warnings

Running unit tests...
Test run for LambdaHandlers.Tests.dll
Passed!  - TestStatusQuery_Success [100ms]
Passed!  - TestStatusQuery_NotFound [50ms]
...
✅ 8/8 tests passed

===== Phase: POST_BUILD =====
Creating Lambda deployment package...
✅ upload-lambda.zip created (5.2 MB)

Synthesizing CDK templates...
✅ CloudFormation templates generated
```

**If it fails**: 
- **Compile error**: Fix code syntax
- **Test failure**: Fix failing test
- **Package error**: Check dependencies

---

### Stage 3: Deploy-Staging

**What it does**: Deploys everything to staging environment

**Behind the scenes**:
```
1. CodePipeline retrieves artifacts from S3
2. CloudFormation updates stack: MediaProcessor-Staging
3. Lambda functions updated with new code
4. Infrastructure updated (API Gateway, DynamoDB, etc.)
5. CloudFormation monitors for errors
```

**What gets deployed**:
- ✅ Lambda functions (Upload, Processing, StatusQuery)
- ✅ API Gateway endpoints
- ✅ DynamoDB tables
- ✅ S3 buckets
- ✅ EventBridge rules
- ✅ VPC/ECS/ALB (if changed)
- ✅ SNS/SQS (if changed)

**Staging URLs**:
- API: `https://staging-api-gateway-id.execute-api.eu-west-1.amazonaws.com/prod/upload`
- Admin: `http://staging-alb-dns-name.eu-west-1.elb.amazonaws.com`

**If it fails**: Check CloudFormation Events tab for error details

---

### Stage 4: Approval (Manual Gate)

**What it does**: Waits for you to approve production deployment

**What YOU should do**:

1. **Test Staging Environment**:
   ```bash
   # Test API
   curl -X POST https://staging-api.../prod/upload \
     --data-binary @test-image.jpg
   
   # Check status
   curl https://staging-api.../prod/status/job-123
   ```

2. **Check CloudWatch Logs**:
   - Go to CloudWatch Console
   - Check for errors in staging Lambda logs

3. **Review Code Changes**:
   - What changed in this deployment?
   - Any breaking changes?

4. **Approve or Reject**:
   - Click **"Review"** button in pipeline
   - Add comment (optional): "Tested staging, looks good!"
   - Click **"Approve"** (deploys to prod) or **"Reject"** (stops pipeline)

**AWS Concept**: Manual approval is a safety gate. Only you can approve production deployments.

---

### Stage 5: Deploy-Production

**What it does**: Same as staging, but to production environment

**Behind the scenes**:
```
1. CloudFormation updates stack: MediaProcessor-Production
2. Lambda functions updated with Blue/Green deployment:
   - Old version (Blue): 100% traffic
   - New version (Green): 0% traffic
   - Gradually shift: 90% → 50% → 10% → 0% Blue
   - Monitor CloudWatch alarms
   - If errors spike: Automatic rollback!
3. Infrastructure updated
4. CloudFormation marks deployment complete
```

**Production URLs**:
- API: `https://prod-api-gateway-id.execute-api.eu-west-1.amazonaws.com/prod/upload`
- Admin: `http://prod-alb-dns-name.eu-west-1.elb.amazonaws.com`

**If it fails**: 
- Automatic rollback to previous version
- Check CloudWatch alarms
- Review CloudFormation Events

---

## 🔧 Troubleshooting Common Issues

### Issue 1: Pipeline Fails at Source Stage

**Error**: `Unable to access repository`

**Solution**:
```bash
# Check connection status
aws codestar-connections get-connection \
  --connection-arn arn:aws:codestar-connections:...

# Status should be "AVAILABLE"
# If "PENDING", go to AWS Console and complete setup
```

---

### Issue 2: Build Fails with "dotnet: command not found"

**Error**: `dotnet: command not found`

**Solution**: Check `buildspec.yml` has correct runtime:
```yaml
runtime-versions:
  dotnet: 8.0  # Make sure this line exists
```

---

### Issue 3: Tests Fail

**Error**: `Test run failed: 2 passed, 1 failed`

**Solution**:
```bash
# Run tests locally first
cd LambdaHandlers.Tests
dotnet test

# Fix failing tests
# Commit and push again
```

**AWS Concept**: CodeBuild stops on first failed command. Fix issues locally before pushing.

---

### Issue 4: Deploy Fails with "Stack is in UPDATE_ROLLBACK_COMPLETE"

**Error**: CloudFormation stack in bad state

**Solution**:
```bash
# Option 1: Delete stack and redeploy
aws cloudformation delete-stack --stack-name MediaProcessor-Staging

# Option 2: Continue rollback in AWS Console
# Go to CloudFormation → Select stack → Continue rollback
```

---

### Issue 5: Manual Approval Times Out

**Error**: Approval stage times out after 7 days

**Solution**: Click "Retry" to restart approval stage. Note: Approval expires after 7 days if not approved/rejected.

---

## 📊 Monitoring Your Pipeline

### View Pipeline Status

**AWS Console**:
```
https://console.aws.amazon.com/codesuite/codepipeline/pipelines
```

**AWS CLI**:
```bash
# Get pipeline status
aws codepipeline get-pipeline-state \
  --name MediaProcessor-Pipeline

# View execution history
aws codepipeline list-pipeline-executions \
  --pipeline-name MediaProcessor-Pipeline
```

---

### View Build Logs

**AWS Console**:
1. Click **"Details"** in Build stage
2. View live build logs
3. Download full logs

**AWS CLI**:
```bash
# Get latest build ID
BUILD_ID=$(aws codebuild list-builds-for-project \
  --project-name MediaProcessor-Build \
  --query 'ids[0]' --output text)

# View build logs
aws codebuild batch-get-builds --ids $BUILD_ID
```

---

### Set Up Notifications (Optional)

**Add email notifications**:

1. Go to SNS Console: https://console.aws.amazon.com/sns
2. Find topic: `MediaProcessor-Pipeline-Notifications`
3. Click **"Create subscription"**
4. Protocol: **Email**
5. Endpoint: `your-email@example.com`
6. Click **"Create subscription"**
7. Check email and click confirmation link

**You'll get notified when**:
- Build succeeds
- Build fails
- Deployment succeeds
- Deployment fails
- Approval needed

---

## 🎯 Testing Your Pipeline

### Test 1: Successful Deployment

1. Make a small code change:
   ```csharp
   // In StatusQueryHandler.cs
   // Change log message
   Console.WriteLine("Status query received - v2");
   ```

2. Commit and push:
   ```bash
   git add .
   git commit -m "Update log message"
   git push origin main
   ```

3. Watch pipeline:
   - Source: ✅ (30 sec)
   - Build: ✅ (5 min)
   - Deploy-Staging: ✅ (5 min)
   - Approval: ⏸️ (waiting)
   - Click "Approve"
   - Deploy-Production: ✅ (5 min)

4. Verify deployment:
   ```bash
   # Test production API
   curl https://your-prod-api.../prod/status/test-123
   ```

**Expected result**: New log message appears in CloudWatch Logs

---

### Test 2: Failed Build (Intentional)

1. Break the code:
   ```csharp
   // Add syntax error
   return new APIGatewayProxyResponse  // Missing semicolon
   {
       StatusCode = 200
   }
   ```

2. Commit and push:
   ```bash
   git add .
   git commit -m "Test build failure"
   git push origin main
   ```

3. Watch pipeline:
   - Source: ✅ (30 sec)
   - Build: ❌ (FAILED!)
   - Pipeline stops here

4. View build logs:
   - Click "Details" in Build stage
   - See compiler error
   - Fix code locally
   - Push again

**Expected result**: Build stops at compilation error, nothing deployed (safe!)

---

### Test 3: Failed Test

1. Break a test:
   ```csharp
   // In StatusQueryHandlerTests.cs
   response.StatusCode.Should().Be(404);  // Change 200 to 404
   ```

2. Push:
   ```bash
   git add .
   git commit -m "Test test failure"
   git push origin main
   ```

3. Watch pipeline:
   - Source: ✅
   - Build: ❌ (test failure)
   - Pipeline stops

**Expected result**: Build fails at test phase, nothing deployed

---

## 💰 Cost Breakdown

| Service | Usage | Cost |
|---------|-------|------|
| **CodePipeline** | 1 active pipeline | $1/month |
| **CodeBuild** | 20 builds × 5 min | ~$0.50/month |
| **S3 Artifacts** | 1 GB storage | $0.02/month |
| **Data Transfer** | Minimal | $0.10/month |
| **SNS Notifications** | 100 emails | $0.01/month |
| **CloudWatch Logs** | 1 GB | $0.50/month |
| **TOTAL** | | **~$2-3/month** |

**First 100 build minutes FREE each month!**

---

## 🚀 What You've Accomplished!

✅ **Automated CI/CD Pipeline** - Code to production in minutes  
✅ **Automated Testing** - Catches bugs before deployment  
✅ **Staged Rollout** - Test in staging first  
✅ **Manual Approval** - Human oversight before production  
✅ **Automatic Rollback** - Safety net if deployment fails  
✅ **Audit Trail** - Know who deployed what, when  
✅ **Notifications** - Team awareness of deployments  

**This is professional-grade DevOps! 🎉**

---

## 📚 Next Steps

### Short Term:
1. Set up email notifications
2. Add more unit tests
3. Test the pipeline with real changes

### Long Term:
1. Add integration tests
2. Add performance tests
3. Implement blue/green deployment for ECS
4. Add deployment approval via Slack
5. Add automated rollback on CloudWatch alarm

---

## 🆘 Need Help?

**View Pipeline**: https://console.aws.amazon.com/codesuite/codepipeline  
**View Builds**: https://console.aws.amazon.com/codesuite/codebuild  
**View Logs**: https://console.aws.amazon.com/cloudwatch/home#logsV2  

**Common Commands**:
```bash
# View pipeline status
aws codepipeline get-pipeline-state --name MediaProcessor-Pipeline

# Retry failed stage
aws codepipeline retry-stage-execution \
  --pipeline-name MediaProcessor-Pipeline \
  --stage-name Build \
  --pipeline-execution-id <execution-id> \
  --retry-mode FAILED_ACTIONS

# Stop pipeline execution
aws codepipeline stop-pipeline-execution \
  --pipeline-name MediaProcessor-Pipeline \
  --pipeline-execution-id <execution-id>
```

---

**🎓 You now have enterprise-grade CI/CD! Welcome to modern DevOps! 🚀**
