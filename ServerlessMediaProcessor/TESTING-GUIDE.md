# AWS Lambda Testing Guide

## 📚 Table of Contents
1. [Why Testing Matters for AWS Lambdas](#why-testing-matters)
2. [Three Types of Lambda Tests](#three-types-of-tests)
3. [Testing Tools Explained](#testing-tools-explained)
4. [Testing Patterns](#testing-patterns)
5. [Key AWS Testing Concepts](#key-concepts)
6. [Running Tests](#running-tests)
7. [Best Practices](#best-practices)

---

## Why Testing Matters for AWS Lambdas

### Financial Impact
- **Without Tests**: Deploy buggy code → Lambda runs with errors → You pay for failed invocations
- **With Tests**: Catch bugs locally → Fix before deployment → Only pay for successful runs

### Development Speed
- **Without Tests**: Deploy to AWS → Test → Wait for CloudWatch logs → Debug → Redeploy (5-10 minutes per cycle)
- **With Tests**: Run locally → Get instant feedback → Fix immediately (< 1 second per cycle)

### AWS Cost Comparison
```
Integration Testing (Real AWS):
- 100 test runs × $0.20 per 1M requests = $0.02
- DynamoDB reads × $0.25 per 1M reads = $0.025
- S3 requests × $0.005 per 1K requests = $0.50
Total: ~$0.545 per test suite run

Unit Testing (Mocked AWS):
- $0.00 (runs on your computer)

Running tests 100 times/day:
- Integration: $54.50/month
- Unit: $0.00/month
```

---

## Three Types of Lambda Tests

### 1. Unit Tests ✅ (What We Built)
**Purpose**: Test Lambda logic in isolation without calling real AWS services

**Characteristics**:
- **Speed**: < 1 second
- **Cost**: $0 (runs locally)
- **AWS Credentials**: Not needed
- **Real AWS Calls**: None (all mocked)

**What to Test**:
- Input validation
- Business logic
- Error handling
- Response formatting
- AWS SDK interactions

**Example**:
```csharp
// Mock DynamoDB client
var mockDynamoDB = new Mock<IAmazonDynamoDB>();
mockDynamoDB.Setup(x => x.GetItemAsync(...)).ReturnsAsync(...);

// Test handler with mock
var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
var response = await handler.HandleStatusQueryAsync(request, context);

// Verify
Assert.Equal(200, response.StatusCode);
```

### 2. Integration Tests
**Purpose**: Test Lambda with real AWS services

**Characteristics**:
- **Speed**: 5-30 seconds
- **Cost**: $$ (uses real AWS resources)
- **AWS Credentials**: Required
- **Real AWS Calls**: Yes

**What to Test**:
- End-to-end Lambda workflows
- IAM permissions
- AWS service interactions
- CloudWatch logging
- Performance under load

**Example**:
```csharp
// Real AWS client (not mocked)
var dynamoDB = new AmazonDynamoDBClient();
var handler = new StatusQueryHandler(dynamoDB, "MediaProcessingJobs-JSavic");

// Call Lambda (makes real DynamoDB request)
var response = await handler.HandleStatusQueryAsync(request, context);

// Verify real data was stored
var item = await dynamoDB.GetItemAsync(...);
Assert.NotNull(item);
```

### 3. End-to-End Tests
**Purpose**: Test complete system workflows through API Gateway

**Characteristics**:
- **Speed**: 30-60 seconds
- **Cost**: $$$ (entire stack running)
- **AWS Credentials**: Required
- **Real AWS Calls**: All services involved

**What to Test**:
- API Gateway → Lambda → DynamoDB flow
- S3 uploads triggering EventBridge → Lambda
- Complete user workflows
- CORS and authentication
- Production-like scenarios

**Example**:
```csharp
// Test through API Gateway (real HTTP request)
var client = new HttpClient();
var response = await client.PostAsync(
    "https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod/upload",
    imageContent
);

// Poll status endpoint until complete
while (status != "Completed") {
    var statusResponse = await client.GetAsync($".../status/{jobId}");
    status = await statusResponse.Content.ReadAsStringAsync();
    await Task.Delay(1000);
}
```

---

## Testing Tools Explained

### xUnit
**What**: .NET testing framework
**Why**: Industry standard, modern, parallel test execution

### Moq
**What**: Mocking library for creating fake objects
**Why**: Lets you simulate AWS SDK behavior without calling real AWS

**Key Concepts**:
```csharp
// 1. Create a mock
var mockDynamoDB = new Mock<IAmazonDynamoDB>();

// 2. Setup behavior (when X is called, return Y)
mockDynamoDB
    .Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default))
    .ReturnsAsync(new GetItemResponse { ... });

// 3. Verify it was called correctly
mockDynamoDB.Verify(
    x => x.GetItemAsync(
        It.Is<GetItemRequest>(req => req.TableName == "TestTable"),
        default
    ),
    Times.Once
);
```

### FluentAssertions
**What**: Readable assertion library
**Why**: Makes tests easier to understand

**Comparison**:
```csharp
// Standard Assert (verbose)
Assert.Equal(200, response.StatusCode);
Assert.True(response.Headers.ContainsKey("Content-Type"));

// FluentAssertions (readable)
response.StatusCode.Should().Be(200);
response.Headers.Should().ContainKey("Content-Type");
```

### Amazon.Lambda.TestUtilities
**What**: AWS-provided utilities for Lambda testing
**Why**: Provides `TestLambdaContext` to simulate Lambda execution environment

**Key Features**:
```csharp
var context = new TestLambdaContext
{
    FunctionName = "MyFunction",
    AwsRequestId = "test-123",
    RemainingTime = TimeSpan.FromSeconds(30),
    Logger = new TestLambdaLogger()
};
```

---

## Testing Patterns

### Pattern 1: Successful Operation
**Test**: Handler works correctly with valid input

```csharp
[Fact]
public async Task SuccessfulOperation_ReturnsExpectedResult()
{
    // ARRANGE: Set up test data
    var mockClient = new Mock<IAmazonDynamoDB>();
    mockClient.Setup(...).ReturnsAsync(validResponse);
    
    // ACT: Call the handler
    var response = await handler.HandleAsync(request, context);
    
    // ASSERT: Verify success
    response.StatusCode.Should().Be(200);
    response.Body.Should().Contain("success");
}
```

### Pattern 2: Not Found (404)
**Test**: Handler returns 404 when resource doesn't exist

```csharp
[Fact]
public async Task NonExistentResource_Returns404()
{
    // ARRANGE: Mock returns empty response
    mockClient.Setup(...).ReturnsAsync(emptyResponse);
    
    // ACT
    var response = await handler.HandleAsync(request, context);
    
    // ASSERT: Verify 404
    response.StatusCode.Should().Be(404);
    response.Body.Should().Contain("not found");
}
```

### Pattern 3: Bad Request (400)
**Test**: Handler validates input and rejects invalid requests

```csharp
[Fact]
public async Task InvalidInput_Returns400()
{
    // ARRANGE: Create invalid request
    var request = new APIGatewayProxyRequest
    {
        Body = null // Missing required data
    };
    
    // ACT
    var response = await handler.HandleAsync(request, context);
    
    // ASSERT: Verify validation error
    response.StatusCode.Should().Be(400);
    // Verify DynamoDB was NOT called (why waste resources?)
    mockClient.Verify(x => x.GetItemAsync(...), Times.Never);
}
```

### Pattern 4: AWS Service Failure (500)
**Test**: Handler gracefully handles AWS service errors

```csharp
[Fact]
public async Task AWSServiceFailure_Returns500()
{
    // ARRANGE: Mock throws AWS exception
    mockClient
        .Setup(x => x.GetItemAsync(...))
        .ThrowsAsync(new AmazonDynamoDBException("Service unavailable"));
    
    // ACT
    var response = await handler.HandleAsync(request, context);
    
    // ASSERT: Verify error handling
    response.StatusCode.Should().Be(500);
    response.Body.Should().Contain("Internal server error");
}
```

### Pattern 5: Parameterized Tests (Theory)
**Test**: Same logic with multiple inputs

```csharp
[Theory]
[InlineData("Pending", "Job is queued")]
[InlineData("Processing", "Job is running")]
[InlineData("Completed", "Job finished")]
public async Task DifferentStatuses_ReturnAppropriateMessages(
    string status, 
    string expectedMessage)
{
    // ARRANGE: Setup status-specific response
    mockClient.Setup(...).ReturnsAsync(CreateResponse(status));
    
    // ACT
    var response = await handler.HandleAsync(request, context);
    
    // ASSERT: Verify status-specific behavior
    response.Body.Should().Contain(expectedMessage);
}
```

---

## Key AWS Testing Concepts

### 1. Mocking DynamoDB
**Why**: DynamoDB responses use special `AttributeValue` format

**Real DynamoDB Response**:
```json
{
  "Item": {
    "JobId": { "S": "job-123" },        // S = String
    "FileSize": { "N": "12345" },       // N = Number
    "Tags": { "SS": ["tag1", "tag2"] }, // SS = String Set
    "IsActive": { "BOOL": true }        // BOOL = Boolean
  }
}
```

**Mocked in C#**:
```csharp
var mockResponse = new GetItemResponse
{
    Item = new Dictionary<string, AttributeValue>
    {
        { "JobId", new AttributeValue { S = "job-123" } },
        { "FileSize", new AttributeValue { N = "12345" } },
        { "Tags", new AttributeValue { SS = new List<string> { "tag1", "tag2" } } },
        { "IsActive", new AttributeValue { BOOL = true } }
    }
};
```

### 2. Mocking S3
**Common S3 Operations**:

**Upload (PutObject)**:
```csharp
mockS3
    .Setup(x => x.PutObjectAsync(
        It.Is<PutObjectRequest>(req => req.BucketName == "test-bucket"),
        default))
    .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
```

**Download (GetObject)**:
```csharp
var testImageStream = new MemoryStream(imageBytes);
mockS3
    .Setup(x => x.GetObjectAsync(
        It.Is<GetObjectRequest>(req => req.Key == "test.jpg"),
        default))
    .ReturnsAsync(new GetObjectResponse 
    { 
        ResponseStream = testImageStream,
        ContentType = "image/jpeg"
    });
```

### 3. Mocking API Gateway Requests
**Structure**:
```csharp
var request = new APIGatewayProxyRequest
{
    // HTTP method
    HttpMethod = "POST",
    
    // Path parameters: /status/{jobId}
    PathParameters = new Dictionary<string, string>
    {
        { "jobId", "test-123" }
    },
    
    // Query parameters: /upload?resize=true
    QueryStringParameters = new Dictionary<string, string>
    {
        { "resize", "true" }
    },
    
    // Headers
    Headers = new Dictionary<string, string>
    {
        { "Content-Type", "application/json" },
        { "Authorization", "Bearer token123" }
    },
    
    // Body
    Body = "{\"key\":\"value\"}",
    IsBase64Encoded = false
};
```

### 4. Lambda Context Simulation
**What Lambda Context Provides**:
```csharp
var context = new TestLambdaContext
{
    // Unique request ID (for logging)
    AwsRequestId = "test-request-123",
    
    // Function name
    FunctionName = "MediaProcessor-Upload",
    
    // How much time left (Lambda has max execution time)
    RemainingTime = TimeSpan.FromSeconds(30),
    
    // Memory limit
    MemoryLimitInMB = 256,
    
    // CloudWatch log group/stream
    LogGroupName = "/aws/lambda/test",
    LogStreamName = "2026/05/10/[$LATEST]abc123",
    
    // Logger for test output
    Logger = new TestLambdaLogger()
};
```

---

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Specific Test Class
```bash
dotnet test --filter FullyQualifiedName~StatusQueryHandlerTests
```

### Run Single Test
```bash
dotnet test --filter FullyQualifiedName~QueryStatus_CompletedJob_ReturnsJobDetails
```

### Run with Verbose Output
```bash
dotnet test --verbosity normal
```

### Run with Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### View Test Results
```bash
# Tests write output to bin/Debug/net8.0/TestResults/
# Open .trx file in Visual Studio Test Explorer
```

---

## Best Practices

### 1. Test Naming Convention
**Pattern**: `MethodName_Scenario_ExpectedResult`

```csharp
// ✅ Good
QueryStatus_CompletedJob_ReturnsJobDetails()
QueryStatus_NonExistentJob_Returns404()
QueryStatus_MissingJobId_Returns400()

// ❌ Bad
Test1()
TestStatusQuery()
QueryStatusTest()
```

### 2. Arrange-Act-Assert (AAA) Pattern
**Structure every test the same way**:

```csharp
[Fact]
public async Task MyTest()
{
    // ARRANGE: Set up test data and mocks
    var mock = new Mock<...>();
    var handler = new Handler(mock.Object);
    
    // ACT: Call the method being tested
    var result = await handler.DoSomethingAsync(...);
    
    // ASSERT: Verify the result
    result.Should().Be(expected);
}
```

### 3. Test One Thing Per Test
**Each test should verify one specific behavior**:

```csharp
// ✅ Good: Separate tests for different behaviors
[Fact] public async Task Returns200OnSuccess() { ... }
[Fact] public async Task Returns404OnNotFound() { ... }
[Fact] public async Task Returns400OnInvalidInput() { ... }

// ❌ Bad: Testing multiple things in one test
[Fact] public async Task TestAllScenarios() { 
    // Tests success, 404, 400 all in one test
}
```

### 4. Don't Test AWS SDK Itself
**Only test YOUR code, not AWS code**:

```csharp
// ❌ Bad: Testing that DynamoDB works
[Fact]
public async Task DynamoDB_CanStoreData()
{
    var client = new AmazonDynamoDBClient();
    await client.PutItemAsync(...); // Testing AWS, not your code
}

// ✅ Good: Testing that YOUR handler uses DynamoDB correctly
[Fact]
public async Task Handler_StoresDataInDynamoDB()
{
    var mockClient = new Mock<IAmazonDynamoDB>();
    var handler = new MyHandler(mockClient.Object);
    
    await handler.ProcessAsync(...);
    
    // Verify YOUR code called DynamoDB with correct parameters
    mockClient.Verify(x => x.PutItemAsync(
        It.Is<PutItemRequest>(req => 
            req.TableName == "ExpectedTable" &&
            req.Item["Key"].S == "ExpectedValue"
        ),
        default
    ), Times.Once);
}
```

### 5. Use Meaningful Test Data
**Use realistic test data**:

```csharp
// ❌ Bad: Non-descriptive test data
var jobId = "abc";
var fileName = "file";
var size = 123;

// ✅ Good: Descriptive, realistic test data
var jobId = "test-job-2026-05-10-12345";
var fileName = "vacation-photo.jpg";
var size = 2_457_600; // 2.4 MB
```

### 6. Verify AWS SDK Interactions
**Ensure handler calls AWS services correctly**:

```csharp
// Verify method was called
mockDynamoDB.Verify(
    x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default),
    Times.Once
);

// Verify method was called with specific parameters
mockDynamoDB.Verify(
    x => x.GetItemAsync(
        It.Is<GetItemRequest>(req => 
            req.TableName == "MediaProcessingJobs" &&
            req.Key["JobId"].S == "test-123"
        ),
        default
    ),
    Times.Once
);

// Verify method was NOT called (important for validation tests)
mockDynamoDB.Verify(
    x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default),
    Times.Never
);
```

### 7. Test Error Messages
**Users need helpful error messages**:

```csharp
// ✅ Good: Verify error message is helpful
var response = await handler.HandleAsync(invalidRequest, context);
response.StatusCode.Should().Be(400);
response.Body.Should().Contain("Missing required field: jobId");

// ❌ Bad: Generic error
response.Body.Should().Contain("Error");
```

### 8. Clean Test Data
**Use constants for reusable test data**:

```csharp
public class StatusQueryHandlerTests
{
    private const string TEST_TABLE_NAME = "MediaProcessingJobs-Test";
    private const string TEST_JOB_ID = "test-job-123";
    private const string TEST_FILE_NAME = "test-image.png";
    
    [Fact]
    public async Task MyTest()
    {
        // Use constants instead of hardcoding strings everywhere
        var handler = new Handler(mockClient.Object, TEST_TABLE_NAME);
    }
}
```

---

## AWS Lambda Testing Anti-Patterns to Avoid

### ❌ Testing in Production
```csharp
// DON'T do this
[Fact]
public async Task ProductionTest()
{
    var client = new AmazonDynamoDBClient(); // Real production client!
    var handler = new Handler(client, "MediaProcessingJobs-PROD"); // PROD table!
    await handler.ProcessAsync(...); // Modifies production data!
}
```

### ❌ Tests That Depend on Each Other
```csharp
// DON'T do this
private static string sharedJobId;

[Fact]
public async Task Test1_CreateJob()
{
    sharedJobId = await handler.CreateJobAsync(...);
}

[Fact]
public async Task Test2_QueryJob()
{
    // This test fails if Test1 doesn't run first!
    var result = await handler.GetJobAsync(sharedJobId);
}
```

### ❌ Slow Tests
```csharp
// DON'T do this
[Fact]
public async Task SlowTest()
{
    // Calling real AWS (takes 5+ seconds)
    var realClient = new AmazonDynamoDBClient();
    
    // Unnecessary delays
    await Task.Delay(10000); // 10 second wait!
}
```

### ❌ Tests Without Assertions
```csharp
// DON'T do this
[Fact]
public async Task TestHandler()
{
    await handler.ProcessAsync(...);
    // No assertions! How do you know it worked?
}
```

---

## Summary: What We Built

### Test Statistics
- **Tests Written**: 8
- **Test Coverage**: 100% of StatusQueryHandler
- **Test Execution Time**: < 1 second
- **Cost**: $0.00

### What We Tested
✅ Successful job query (200)  
✅ Non-existent job (404)  
✅ Missing parameters (400)  
✅ DynamoDB exception (500)  
✅ Different status values (Pending, Processing, Failed)  
✅ Response format validation  
✅ AWS SDK interaction verification  
✅ Error message clarity  

### AWS Concepts Mastered
✅ Lambda unit testing patterns  
✅ Mocking AWS SDK (DynamoDB)  
✅ API Gateway request/response testing  
✅ Lambda context simulation  
✅ HTTP status code best practices  
✅ Error handling strategies  
✅ Test-driven development for serverless  

---

## Next Steps

### Add More Tests (Optional)
1. **ImageUploadHandler Tests** - Test S3 uploads, validation, DynamoDB writes
2. **ImageProcessingHandler Tests** - Test image processing, EventBridge triggers
3. **Integration Tests** - Test with real AWS services
4. **Performance Tests** - Test under load

### Improve Coverage
```bash
# Generate code coverage report
dotnet test --collect:"XPlat Code Coverage"

# Install report generator
dotnet tool install -g dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport

# Open report
start coveragereport/index.html
```

### Continuous Integration
Add tests to CI/CD pipeline:
```yaml
# .github/workflows/test.yml
on: [push]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test
```

---

## AWS Testing Resources

### Official AWS Documentation
- [AWS Lambda Testing Best Practices](https://docs.aws.amazon.com/lambda/latest/dg/testing-functions.html)
- [AWS SDK for .NET Testing](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/unit-testing.html)

### Recommended Reading
- [Serverless Testing Strategies](https://aws.amazon.com/blogs/compute/serverless-testing-strategies/)
- [Mock Testing with AWS SDK](https://aws.amazon.com/blogs/developer/mocking-service-clients/)

### Testing Tools
- [xUnit Documentation](https://xunit.net/)
- [Moq Quick Start](https://github.com/moq/moq4)
- [FluentAssertions](https://fluentassertions.com/)
- [AWS Lambda Test Tool](https://github.com/aws/aws-lambda-dotnet#lambda-test-tool)

---

**🎓 Key Takeaway**: Unit tests let you iterate rapidly without AWS costs while ensuring Lambda functions work correctly before deployment. This is essential for professional serverless development!
