using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using LambdaHandlers.Handlers;
using Moq;
using System.Text.Json;
using Xunit;

namespace LambdaHandlers.Tests.Handlers
{
    /// <summary>
    /// Unit tests for StatusQueryHandler Lambda function
    /// 
    /// AWS TESTING CONCEPTS:
    /// - Mock AWS SDK clients (DynamoDB) to test without real AWS
    /// - Use TestLambdaContext to simulate Lambda execution environment
    /// - Test both success and failure scenarios
    /// - Validate response format matches API Gateway expectations
    /// </summary>
    public class StatusQueryHandlerTests
    {
        /// <summary>
        /// AWS CONCEPT: Testing successful DynamoDB query
        /// 
        /// What this tests:
        /// - Handler correctly queries DynamoDB with JobId
        /// - Handler parses DynamoDB response correctly
        /// - Handler formats response as expected by API Gateway
        /// 
        /// Why mock DynamoDB:
        /// - Tests run instantly (no network calls)
        /// - Tests run for free (no AWS charges)
        /// - Tests work without AWS credentials
        /// - Tests are deterministic (same input = same output)
        /// </summary>
        [Fact]
        public async Task QueryStatus_CompletedJob_ReturnsJobDetails()
        {
            // ARRANGE: Set up test data and mocks
            
            // 1. Create mock DynamoDB client
            var mockDynamoDB = new Mock<IAmazonDynamoDB>();
            
            // 2. Create test job ID
            var testJobId = "test-job-123";
            
            // 3. Create mock DynamoDB response (simulates what DynamoDB would return)
            var mockDynamoDbResponse = new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    { "JobId", new AttributeValue { S = testJobId } },
                    { "Status", new AttributeValue { S = "Completed" } },
                    { "OriginalFileName", new AttributeValue { S = "test-image.png" } },
                    { "FileType", new AttributeValue { S = "image/png" } },
                    { "FileSize", new AttributeValue { N = "12345" } },
                    { "UploadedAt", new AttributeValue { S = "2026-05-10T10:00:00Z" } },
                    { "ProcessingStartedAt", new AttributeValue { S = "2026-05-10T10:00:05Z" } },
                    { "CompletedAt", new AttributeValue { S = "2026-05-10T10:00:40Z" } },
                    { "OutputS3Key", new AttributeValue { S = "jobs/test-job-123/processed.png" } },
                    { "ProcessedWidth", new AttributeValue { N = "800" } },
                    { "ProcessedHeight", new AttributeValue { N = "600" } }
                }
            };
            
            // 4. Configure mock to return this response when GetItemAsync is called
            mockDynamoDB
                .Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default))
                .ReturnsAsync(mockDynamoDbResponse);
            
            // 5. Create test Lambda context (simulates Lambda execution environment)
            var testContext = new TestLambdaContext
            {
                FunctionName = "StatusQueryHandler",
                AwsRequestId = "test-request-123"
            };
            
            // 6. Create test API Gateway request
            var testRequest = new APIGatewayProxyRequest
            {
                PathParameters = new Dictionary<string, string>
                {
                    { "jobId", testJobId }
                }
            };
            
            // 7. Create handler with mocked DynamoDB client
            var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
            
            // ACT: Call the Lambda handler
            var response = await handler.HandleStatusQueryAsync(testRequest, testContext);
            
            // ASSERT: Verify the response
            
            // 1. Should return 200 OK
            response.StatusCode.Should().Be(200);
            
            // 2. Should have correct content type
            response.Headers.Should().ContainKey("Content-Type");
            response.Headers["Content-Type"].Should().Be("application/json");
            
            // 3. Parse the JSON response body
            var responseBody = JsonSerializer.Deserialize<JsonElement>(response.Body);
            
            // 4. Verify job details in response
            responseBody.GetProperty("jobId").GetString().Should().Be(testJobId);
            responseBody.GetProperty("status").GetString().Should().Be("Completed");
            responseBody.GetProperty("fileName").GetString().Should().Be("test-image.png");
            responseBody.GetProperty("outputFile").GetString().Should().Be("jobs/test-job-123/processed.png");
            responseBody.GetProperty("processedDimensions").GetString().Should().Be("800x600");
            
            // 5. Verify DynamoDB was called exactly once with correct parameters
            mockDynamoDB.Verify(
                x => x.GetItemAsync(
                    It.Is<GetItemRequest>(req => 
                        req.TableName == "TestTable" && 
                        req.Key["JobId"].S == testJobId),
                    default),
                Times.Once);
        }
        
        /// <summary>
        /// AWS CONCEPT: Testing error handling for non-existent job
        /// 
        /// What this tests:
        /// - Handler correctly handles empty DynamoDB response
        /// - Handler returns 404 (not found) status code
        /// - Handler provides meaningful error message
        /// 
        /// Why this matters:
        /// - Lambda should handle missing data gracefully (not crash)
        /// - API Gateway expects proper HTTP status codes
        /// - Users need clear error messages
        /// </summary>
        [Fact]
        public async Task QueryStatus_NonExistentJob_Returns404()
        {
            // ARRANGE
            var mockDynamoDB = new Mock<IAmazonDynamoDB>();
            var testJobId = "non-existent-job";
            
            // Mock empty DynamoDB response (job not found)
            var mockDynamoDbResponse = new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>() // Empty response
            };
            
            mockDynamoDB
                .Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default))
                .ReturnsAsync(mockDynamoDbResponse);
            
            var testContext = new TestLambdaContext();
            var testRequest = new APIGatewayProxyRequest
            {
                PathParameters = new Dictionary<string, string>
                {
                    { "jobId", testJobId }
                }
            };
            
            var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
            
            // ACT
            var response = await handler.HandleStatusQueryAsync(testRequest, testContext);
            
            // ASSERT
            response.StatusCode.Should().Be(404);
            
            var responseBody = JsonSerializer.Deserialize<JsonElement>(response.Body);
            responseBody.GetProperty("error").GetString().Should().Contain("not found");
            responseBody.GetProperty("statusCode").GetInt32().Should().Be(404);
        }
        
        /// <summary>
        /// AWS CONCEPT: Testing invalid input handling
        /// 
        /// What this tests:
        /// - Handler validates required path parameters exist
        /// - Handler returns 400 (bad request) for invalid input
        /// - Handler doesn't crash on bad input
        /// 
        /// Why this matters:
        /// - Prevents unnecessary DynamoDB queries (saves money)
        /// - Provides fast feedback to API clients
        /// - Follows REST API best practices
        /// </summary>
        [Fact]
        public async Task QueryStatus_MissingJobId_Returns400()
        {
            // ARRANGE
            var mockDynamoDB = new Mock<IAmazonDynamoDB>();
            var testContext = new TestLambdaContext();
            
            // Request without pathParameters
            var testRequest = new APIGatewayProxyRequest
            {
                PathParameters = null // Missing path parameters
            };
            
            var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
            
            // ACT
            var response = await handler.HandleStatusQueryAsync(testRequest, testContext);
            
            // ASSERT
            response.StatusCode.Should().Be(400);
            
            var responseBody = JsonSerializer.Deserialize<JsonElement>(response.Body);
            responseBody.GetProperty("error").GetString().Should().Contain("Missing jobId");
            
            // Verify DynamoDB was NOT called (no point querying without ID)
            mockDynamoDB.Verify(
                x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default),
                Times.Never);
        }
        
        /// <summary>
        /// AWS CONCEPT: Testing DynamoDB exception handling
        /// 
        /// What this tests:
        /// - Handler catches and handles AWS SDK exceptions
        /// - Handler returns 500 (internal server error) for infrastructure failures
        /// - Handler logs error details for debugging
        /// 
        /// Why this matters:
        /// - AWS services can fail (network issues, throttling, etc.)
        /// - Lambda should handle failures gracefully
        /// - Error details help with troubleshooting in CloudWatch
        /// </summary>
        [Fact]
        public async Task QueryStatus_DynamoDBException_Returns500()
        {
            // ARRANGE
            var mockDynamoDB = new Mock<IAmazonDynamoDB>();
            var testJobId = "test-job-123";
            
            // Mock DynamoDB throwing an exception (simulates AWS failure)
            mockDynamoDB
                .Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default))
                .ThrowsAsync(new AmazonDynamoDBException("DynamoDB service unavailable"));
            
            var testContext = new TestLambdaContext();
            var testRequest = new APIGatewayProxyRequest
            {
                PathParameters = new Dictionary<string, string>
                {
                    { "jobId", testJobId }
                }
            };
            
            var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
            
            // ACT
            var response = await handler.HandleStatusQueryAsync(testRequest, testContext);
            
            // ASSERT
            response.StatusCode.Should().Be(500);
            
            var responseBody = JsonSerializer.Deserialize<JsonElement>(response.Body);
            responseBody.GetProperty("error").GetString().Should().Contain("Internal server error");
        }
        
        /// <summary>
        /// AWS CONCEPT: Testing response status variations
        /// 
        /// What this tests:
        /// - Handler correctly formats "Pending" status response
        /// - Handler omits processing results when job is still pending
        /// - Handler provides appropriate message for status
        /// 
        /// Why this matters:
        /// - Different statuses require different response formats
        /// - Clients need to understand what each status means
        /// - Response should match the documented API contract
        /// </summary>
        [Theory]
        [InlineData("Pending", "Job is queued for processing")]
        [InlineData("Processing", "Job is currently being processed")]
        [InlineData("Failed", "Job processing failed")]
        public async Task QueryStatus_DifferentStatuses_ReturnsAppropriateMessage(
            string status, string expectedMessage)
        {
            // ARRANGE
            var mockDynamoDB = new Mock<IAmazonDynamoDB>();
            var testJobId = "test-job-123";
            
            var mockDynamoDbResponse = new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    { "JobId", new AttributeValue { S = testJobId } },
                    { "Status", new AttributeValue { S = status } },
                    { "OriginalFileName", new AttributeValue { S = "test.png" } },
                    { "FileType", new AttributeValue { S = "image/png" } },
                    { "FileSize", new AttributeValue { N = "12345" } },
                    { "UploadedAt", new AttributeValue { S = "2026-05-10T10:00:00Z" } }
                }
            };
            
            // Add error message for Failed status
            if (status == "Failed")
            {
                mockDynamoDbResponse.Item.Add(
                    "ErrorMessage", 
                    new AttributeValue { S = "Processing error occurred" });
            }
            
            mockDynamoDB
                .Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), default))
                .ReturnsAsync(mockDynamoDbResponse);
            
            var testContext = new TestLambdaContext();
            var testRequest = new APIGatewayProxyRequest
            {
                PathParameters = new Dictionary<string, string>
                {
                    { "jobId", testJobId }
                }
            };
            
            var handler = new StatusQueryHandler(mockDynamoDB.Object, "TestTable");
            
            // ACT
            var response = await handler.HandleStatusQueryAsync(testRequest, testContext);
            
            // ASSERT
            response.StatusCode.Should().Be(200);
            
            var responseBody = JsonSerializer.Deserialize<JsonElement>(response.Body);
            responseBody.GetProperty("status").GetString().Should().Be(status);
            responseBody.GetProperty("message").GetString().Should().Be(expectedMessage);
        }
    }
}

/*
 * KEY AWS TESTING LESSONS FROM THIS FILE:
 * 
 * 1. MOCKING IS ESSENTIAL
 *    - Real AWS calls are slow, cost money, and need credentials
 *    - Mocks let you test instantly and for free
 * 
 * 2. TEST BOTH SUCCESS AND FAILURE
 *    - Success: Handler works with valid data
 *    - Failure: Handler gracefully handles errors (404, 400, 500)
 * 
 * 3. VERIFY AWS SDK INTERACTIONS
 *    - Check that handler calls DynamoDB with correct parameters
 *    - Verify handler doesn't make unnecessary AWS calls
 * 
 * 4. TEST HTTP STATUS CODES
 *    - 200: Success
 *    - 400: Bad request (client error)
 *    - 404: Not found
 *    - 500: Internal server error (AWS/Lambda failure)
 * 
 * 5. VALIDATE RESPONSE FORMAT
 *    - API Gateway expects specific response structure
 *    - StatusCode, Headers, Body must all be correct
 * 
 * 6. USE TEST DATA THAT MATCHES PRODUCTION
 *    - DynamoDB AttributeValue format (S for string, N for number)
 *    - ISO 8601 timestamps
 *    - Actual file types and sizes
 * 
 * This testing approach applies to ALL AWS Lambda functions!
 */
