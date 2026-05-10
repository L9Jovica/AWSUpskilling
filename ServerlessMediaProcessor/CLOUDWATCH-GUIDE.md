# CloudWatch Monitoring Guide for Serverless Media Processor

## AWS CloudWatch - Core Concepts

### What is CloudWatch?
CloudWatch is AWS's observability service that collects and tracks metrics, logs, and events from your AWS resources. Think of it as the "eyes and ears" of your AWS infrastructure.

---

## 1. CloudWatch Logs

### What Are Logs?
Logs are timestamped records of events that happen in your application. Every time your Lambda function runs, it writes logs to CloudWatch.

### Log Organization Hierarchy:
```
CloudWatch Logs
  └── Log Groups (one per Lambda function)
       └── Log Streams (one per Lambda execution environment)
            └── Log Events (individual log entries)
```

### Your Current Log Groups:
- `/aws/lambda/MediaProcessor-ImageUpload-JSavic` - Upload Lambda logs
- `/aws/lambda/MediaProcessor-ProcessingHandler-JSavic` - Processing Lambda logs
- `/aws/lambda/MediaProcessor-StatusQuery-JSavic` - Status Query Lambda logs

### Key Concepts:

**Log Group**: 
- Container for all logs from one Lambda function
- Has retention settings (how long to keep logs)
- Default: Logs kept forever (can be expensive!)
- Best Practice: Set retention to 7-30 days for cost savings

**Log Stream**:
- Each Lambda execution environment creates its own stream
- Named with date and unique ID: `2026/05/08/[$LATEST]abc123...`
- New stream created on Lambda cold starts
- Same stream reused for warm invocations

**Log Event**:
- Individual log entry with timestamp and message
- Created by: `context.Logger.LogInformation()`, `Console.WriteLine()`, or automatic Lambda logs

---

## 2. CloudWatch Metrics

### What Are Metrics?
Metrics are numerical measurements collected over time. AWS automatically collects metrics for Lambda, API Gateway, DynamoDB, and S3.

### Built-in Lambda Metrics (Automatic, Free):

1. **Invocations**
   - How many times the Lambda was called
   - Why it matters: Tracks usage and helps estimate costs

2. **Duration**
   - How long each invocation took (milliseconds)
   - Why it matters: Identifies performance issues

3. **Errors**
   - Number of failed invocations (unhandled exceptions)
   - Why it matters: Critical for identifying problems

4. **Throttles**
   - Invocations rejected due to concurrency limits
   - Why it matters: Indicates you need to increase Lambda limits

5. **ConcurrentExecutions**
   - Number of Lambdas running simultaneously
   - Why it matters: Helps understand load patterns

6. **DeadLetterErrors** (if configured)
   - Failed attempts to send failure info to dead letter queue
   - Why it matters: Ensures failure handling works

### Metric Dimensions:
Metrics can be filtered by:
- **FunctionName**: Specific Lambda (e.g., "MediaProcessor-ImageUpload-JSavic")
- **Resource**: Lambda version or alias
- **ExecutedVersion**: $LATEST or specific version number

### Custom Metrics (We'll Create These):
- **JobsProcessed**: Number of images successfully processed
- **ProcessingTime**: Time spent processing images
- **UploadSize**: Size of uploaded images
- **DynamoDBWrites**: Number of successful DynamoDB writes

---

## 3. CloudWatch Dashboards

### What Is a Dashboard?
A visual interface showing multiple metrics in one place. Like a car dashboard shows speed, fuel, temperature - CloudWatch Dashboard shows your application's health.

### Dashboard Components (Widgets):

1. **Line Graphs**
   - Show trends over time
   - Example: Lambda invocations per minute

2. **Number Widgets**
   - Show single current value
   - Example: Total errors today

3. **Bar Charts**
   - Compare values across dimensions
   - Example: Errors per Lambda function

4. **Log Widgets**
   - Display recent log entries
   - Example: Last 10 errors from all Lambdas

### Our Dashboard Will Show:
- Lambda invocation rates (all 3 functions)
- Error counts (with alarm thresholds)
- Average processing duration
- API Gateway request rates
- DynamoDB read/write capacity usage
- Recent errors from logs

---

## 4. CloudWatch Alarms

### What Is an Alarm?
An alarm monitors a metric and triggers an action when a threshold is crossed. It's like a smoke detector for your application.

### Alarm States:
1. **OK**: Metric is within acceptable range
2. **ALARM**: Metric crossed the threshold (something's wrong!)
3. **INSUFFICIENT_DATA**: Not enough data points yet

### Alarm Components:

**Metric**: What to monitor (e.g., Lambda Errors)

**Threshold**: The limit (e.g., more than 5 errors)

**Evaluation Period**: How long to check (e.g., 5 minutes)

**Datapoints to Alarm**: How many periods must breach (e.g., 2 out of 3)

**Actions**: What to do when alarm triggers:
- Send SNS notification (email/SMS)
- Trigger Lambda function
- Auto-scale resources
- Call webhook

### Alarms We'll Create:

1. **High Error Rate**
   - Metric: Lambda Errors
   - Threshold: > 3 errors in 5 minutes
   - Action: Send email notification

2. **Slow Processing**
   - Metric: Lambda Duration
   - Threshold: Average > 45 seconds (our processing takes ~35s normally)
   - Action: Send email notification

3. **Upload Function Failures**
   - Metric: Upload Lambda Errors
   - Threshold: > 0 errors in 5 minutes
   - Action: Send email notification (uploads should never fail)

4. **DynamoDB Throttling**
   - Metric: DynamoDB ThrottledRequests
   - Threshold: > 0 throttles
   - Action: Send email notification

---

## 5. CloudWatch Logs Insights

### What Is Logs Insights?
A query language for searching and analyzing CloudWatch Logs. Like SQL for logs!

### Query Examples:

**Find all errors in last hour**:
```
fields @timestamp, @message
| filter @message like /ERROR/
| sort @timestamp desc
| limit 100
```

**Count errors by Lambda function**:
```
fields @logStream
| filter @message like /ERROR/
| stats count() by @logStream
```

**Average processing duration**:
```
fields @duration
| filter @message like /Processing completed/
| stats avg(@duration) as avg_duration
```

**Find slow invocations (> 40 seconds)**:
```
fields @timestamp, @duration, @requestId
| filter @duration > 40000
| sort @duration desc
```

---

## 6. Cost Considerations

### What Costs Money:

1. **Log Storage**
   - $0.50 per GB ingested
   - $0.03 per GB stored per month
   - **Mitigation**: Set retention periods (7-30 days instead of forever)

2. **Log Data Transfer**
   - Free within same region
   - Charged if querying from different region

3. **Metrics**
   - Standard metrics (Lambda, S3, DynamoDB): FREE
   - Custom metrics: $0.30 per metric per month
   - High-resolution metrics (< 1 minute): $0.30 per metric per month

4. **Dashboards**
   - First 3 dashboards: FREE
   - Additional dashboards: $3 per month per dashboard

5. **Alarms**
   - Standard alarms: $0.10 per alarm per month
   - High-resolution alarms: $0.30 per alarm per month

6. **Logs Insights Queries**
   - $0.005 per GB scanned
   - Tip: Use time filters to scan less data

### Estimated Monthly Cost for Our Setup:
- 3 Lambda functions with ~1000 invocations/day = ~$0.50 in logs
- 3 Standard alarms = $0.30
- 1 Dashboard = FREE
- **Total: ~$0.80/month** (very affordable for learning!)

---

## 7. Best Practices

### Logging Best Practices:

1. **Use Structured Logging**
   ```csharp
   // Good - easy to parse
   logger.LogInformation($"JobId={jobId}, Status={status}, Duration={duration}ms");
   
   // Bad - hard to parse
   logger.LogInformation($"Job completed");
   ```

2. **Include Request IDs**
   - Always log `context.AwsRequestId`
   - Enables tracing a request across multiple Lambdas

3. **Log at Appropriate Levels**
   - ERROR: Unrecoverable failures
   - WARNING: Recoverable issues
   - INFO: Normal operations (job started, completed)
   - DEBUG: Detailed diagnostic info (only in development)

4. **Don't Log Sensitive Data**
   - Never log passwords, tokens, credit cards
   - Mask or redact personal information

### Monitoring Best Practices:

1. **Monitor the 4 Golden Signals**
   - **Latency**: How long requests take (Duration metric)
   - **Traffic**: Request rate (Invocations metric)
   - **Errors**: Failure rate (Errors metric)
   - **Saturation**: Resource usage (ConcurrentExecutions)

2. **Set Realistic Alarm Thresholds**
   - Too sensitive = alarm fatigue (you'll ignore them)
   - Too relaxed = miss real problems
   - Tip: Analyze 2 weeks of metrics first to set baselines

3. **Use Composite Alarms**
   - Combine multiple alarms with AND/OR logic
   - Example: Alert only if BOTH error rate is high AND duration is slow

4. **Create Runbooks**
   - Document what to do when each alarm fires
   - Include: How to investigate, common causes, how to fix

---

## Next Steps

We'll now create:
1. ✅ CloudWatch Dashboard with all key metrics
2. ✅ CloudWatch Alarms for critical issues
3. ✅ SNS Topic for email notifications
4. ✅ PowerShell scripts for querying logs

Ready to implement!
