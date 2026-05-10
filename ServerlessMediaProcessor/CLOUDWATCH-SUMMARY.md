# CloudWatch Monitoring - Implementation Summary

## What We Built

You've successfully implemented a comprehensive CloudWatch monitoring system for your Serverless Media Processor! Here's what was created:

---

## 1. SNS Topic for Notifications

**Resource Created**: `MediaProcessor-Alarms-JSavic`  
**ARN**: `arn:aws:sns:eu-west-1:765891906457:MediaProcessor-Alarms-JSavic`

**What It Does**:
- Acts as a notification hub for all CloudWatch alarms
- Sends email alerts when alarms trigger
- Email subscription to: `j.savic@levi9.com`

**Status**: ⚠️ **Action Required** - Check your email and confirm the subscription!

---

## 2. CloudWatch Alarms (4 Total)

### Alarm 1: Upload Lambda Errors
- **Name**: `MediaProcessor-UploadErrors-JSavic`
- **Trigger**: More than 1 error in 5 minutes
- **Why**: Upload should never fail - indicates critical issue
- **Action**: Sends email via SNS

### Alarm 2: Processing Lambda Errors
- **Name**: `MediaProcessor-ProcessingErrors-JSavic`
- **Trigger**: More than 3 errors in 5 minutes
- **Why**: Multiple failures indicate systemic problem (S3, DynamoDB, or image processing issues)
- **Action**: Sends email via SNS

### Alarm 3: Slow Processing
- **Name**: `MediaProcessor-SlowProcessing-JSavic`
- **Trigger**: Average duration > 45 seconds for 10 minutes
- **Why**: Normal is 30-35 seconds; 45+ indicates performance degradation
- **Action**: Sends email via SNS

### Alarm 4: API Gateway Errors
- **Name**: `MediaProcessor-APIErrors-JSavic`
- **Trigger**: More than 5 server errors (5xx) in 5 minutes
- **Why**: 5XX errors mean Lambda integration issues
- **Action**: Sends email via SNS

**Current State**: All alarms are in `INSUFFICIENT_DATA` state (normal for new alarms)  
**Will transition to**: `OK` within 5-10 minutes as metrics are collected

---

## 3. CloudWatch Dashboard

**Name**: `MediaProcessor-Dashboard-JSavic`

**5 Widgets Tracking**:

1. **Lambda Invocations** (Top Left)
   - Shows call rate for all 3 Lambda functions
   - Helps identify usage patterns
   - Color-coded for each function

2. **Lambda Errors** (Top Right)
   - Error counts for each Lambda
   - Should be zero or very low
   - Red spikes indicate problems

3. **Processing Duration** (Middle Left)
   - Average and maximum processing time
   - Tracks performance over time
   - Shows if processing is getting slower

4. **API Gateway Requests** (Middle Right)
   - Total requests, 4XX (client errors), 5XX (server errors)
   - 5XX should always be zero

5. **DynamoDB Capacity** (Bottom)
   - Read and write capacity usage
   - Helps determine if you need to scale

**How to View**:
```powershell
# Option 1: Use the helper function
Import-Module .\CloudWatch-Monitoring.psm1
Open-CloudWatchDashboard

# Option 2: Manual URL
# https://eu-west-1.console.aws.amazon.com/cloudwatch/home?region=eu-west-1#dashboards:name=MediaProcessor-Dashboard-JSavic
```

---

## 4. Helper Scripts (PowerShell Module)

**File**: `CloudWatch-Monitoring.psm1`

### How to Use:

```powershell
# Import the module
Import-Module .\CloudWatch-Monitoring.psm1

# View all available functions
Get-Command -Module CloudWatch-Monitoring
```

### Available Functions:

#### **Log Viewing Functions**

```powershell
# Get recent errors from all Lambdas
Get-RecentErrors -Since "1h"

# View specific Lambda logs
Get-UploadLogs -Since "30m"
Get-ProcessingLogs -Since "1h"
Get-StatusQueryLogs -Since "10m"

# Stream logs live
Get-ProcessingLogs -Follow

# Search for a specific Job ID across all logs
Search-LogsForJobId -JobId "abc123-def456-..."
```

#### **Metrics Functions**

```powershell
# Get invocation counts (last hour)
Get-LambdaInvocationCount

# Get error counts (last hour)
Get-LambdaErrors

# Get Processing Lambda performance stats
Get-ProcessingDuration
```

#### **Alarm Functions**

```powershell
# Check current alarm status
Get-AlarmStatus

# View alarm state change history
Get-AlarmHistory -Since "24h"
```

#### **Health Check**

```powershell
# Get complete system health report
Get-SystemHealth
```

This runs all health checks and displays:
- Invocation counts for all Lambdas
- Error counts (color-coded: green = 0, red = errors)
- Processing duration statistics
- Current alarm states

---

## 5. AWS Concepts You Learned

### CloudWatch Logs
- **Log Groups**: Container for logs from one Lambda
- **Log Streams**: One per Lambda execution environment
- **Log Retention**: How long logs are kept (impacts cost)
- **Log Insights**: SQL-like queries for log analysis

### CloudWatch Metrics
- **Namespaces**: Categorization (AWS/Lambda, AWS/ApiGateway, etc.)
- **Dimensions**: Filters (FunctionName, TableName, etc.)
- **Statistics**: How to aggregate data (Sum, Average, Maximum, Minimum)
- **Period**: Time window for aggregation (300 seconds = 5 minutes)

### CloudWatch Alarms
- **Threshold**: The limit that triggers the alarm
- **Evaluation Period**: How long to check
- **Datapoints to Alarm**: How many periods must breach
- **Alarm States**: INSUFFICIENT_DATA → OK or ALARM
- **Actions**: What to do when alarm fires (SNS, Lambda, Auto Scaling)

### SNS (Simple Notification Service)
- **Topics**: Notification hubs (like a mailing list)
- **Subscriptions**: Who receives notifications (email, SMS, Lambda)
- **Confirmation**: Email subscriptions must be confirmed before active

### CloudWatch Dashboards
- **Widgets**: Visual components (line graphs, numbers, logs)
- **Metric Format**: Specific JSON structure `[Namespace, MetricName, DimensionName, DimensionValue, Options]`
- **Layout**: Grid-based (24 columns wide)

---

## 6. Cost Breakdown

### Monthly Estimated Costs (Low Usage):

| Service | Cost | Notes |
|---------|------|-------|
| CloudWatch Logs (1GB/month) | $0.50 | First 5GB ingestion free |
| CloudWatch Alarms (4 alarms) | $0.40 | $0.10 per standard alarm |
| CloudWatch Dashboard | FREE | First 3 dashboards free |
| SNS Email Notifications | FREE | First 1,000 emails free |
| **TOTAL** | **~$0.90/month** | Very affordable! |

### Cost Optimization Tips:
1. Set log retention to 7-30 days (not forever)
2. Use log filters to reduce ingested data
3. Delete unused alarms and dashboards
4. Use CloudWatch Logs Insights efficiently (charges per GB scanned)

---

## 7. What Happens When an Alarm Triggers

### Example: Upload Lambda has an error

1. **AWS CloudWatch**: Detects `Errors` metric > 1
2. **Alarm State**: Changes from OK → ALARM
3. **SNS Topic**: Receives notification from alarm
4. **Email Sent**: To `j.savic@levi9.com` with details:
   ```
   Subject: ALARM: "MediaProcessor-UploadErrors-JSavic" in EU (Ireland)
   
   You are receiving this email because your Amazon CloudWatch Alarm
   "MediaProcessor-UploadErrors-JSavic" in the EU (Ireland) region
   has entered the ALARM state...
   
   Threshold: Errors > 1.0 for 1 period(s) of 300 seconds.
   ```
5. **You Investigate**: 
   - Open CloudWatch Dashboard
   - Run `Get-RecentErrors`
   - Check specific Lambda logs
   - Find and fix the issue
6. **Alarm Recovers**: Once errors stop, alarm returns to OK state

---

## 8. Testing Your Monitoring

### Test Scenario 1: Generate an Upload
```powershell
# This will create Lambda invocations and DynamoDB writes
# Visible in dashboard within 5 minutes
```

### Test Scenario 2: View Real Logs
```powershell
Import-Module .\CloudWatch-Monitoring.psm1

# Upload an image, then check logs
Get-UploadLogs -Since "5m"

# Check status query
Get-StatusQueryLogs -Since "5m"
```

### Test Scenario 3: Check System Health
```powershell
# Run complete health check
Get-SystemHealth
```

Expected output:
- Invocation counts > 0 (if you've used the system)
- Error counts = 0 (green)
- All alarms in OK state

---

## 9. Next Steps

### Immediate Actions:
1. ✅ **Confirm SNS email subscription** (check your inbox)
2. ✅ **Open CloudWatch Dashboard** to see your metrics
3. ✅ **Test the helper scripts** to get familiar with monitoring

### Optional Enhancements:
- **Add more alarms**: DynamoDB throttling, S3 request errors
- **Create custom metrics**: Track business metrics (images processed, average file size)
- **Set up log retention**: Reduce costs by setting 30-day retention
- **Configure SNS for SMS**: Get text alerts for critical issues
- **Create composite alarms**: Combine multiple alarms with AND/OR logic

---

## 10. Troubleshooting Guide

### Problem: "No data in dashboard"
**Solution**: 
- Metrics appear 5-10 minutes after activity
- Generate some traffic (upload an image, query status)
- Wait 5 minutes and refresh

### Problem: "Alarms stuck in INSUFFICIENT_DATA"
**Solution**:
- Normal for new alarms with no traffic
- Generate activity (upload images)
- Will transition to OK within 10 minutes

### Problem: "Email subscription not working"
**Solution**:
- Check spam folder for confirmation email
- Resend confirmation: 
  ```powershell
  aws sns subscribe --topic-arn arn:aws:sns:eu-west-1:765891906457:MediaProcessor-Alarms-JSavic --protocol email --notification-endpoint your@email.com
  ```

### Problem: "Helper scripts not found"
**Solution**:
```powershell
# Make sure you're in the correct directory
cd C:\GitHub\Personal Jovica\AWSUpskilling\AWSUpskilling\ServerlessMediaProcessor

# Import module
Import-Module .\CloudWatch-Monitoring.psm1 -Force
```

---

## 11. Resources for Learning More

### AWS Documentation:
- [CloudWatch Concepts](https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/cloudwatch_concepts.html)
- [CloudWatch Logs Insights Query Syntax](https://docs.aws.amazon.com/AmazonCloudWatch/latest/logs/CWL_QuerySyntax.html)
- [CloudWatch Alarms](https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/AlarmThatSendsEmail.html)

### Best Practices:
- [AWS Well-Architected Framework - Observability](https://docs.aws.amazon.com/wellarchitected/latest/framework/a-observability.html)
- [Monitoring Lambda Functions](https://docs.aws.amazon.com/lambda/latest/dg/lambda-monitoring.html)

---

## Summary

🎉 **Congratulations!** You've built a production-grade monitoring system including:
- ✅ 4 CloudWatch Alarms watching for errors and performance issues
- ✅ 1 SNS Topic for email notifications
- ✅ 1 CloudWatch Dashboard with 5 widgets
- ✅ PowerShell module with 12 helper functions
- ✅ Complete observability for your serverless application

**You now have**:
- Real-time visibility into your application's health
- Automated alerts when problems occur
- Tools to quickly diagnose issues
- Understanding of AWS observability services

**Next Task**: Unit Testing and Documentation! 🚀
