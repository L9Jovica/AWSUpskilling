using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LambdaHandlers.Models;
using System.Text.Json;

namespace LambdaHandlers.Handlers
{
    /// <summary>
    /// Lambda handler for querying job status from DynamoDB
    /// Endpoint: GET /api/status/{jobId}
    /// </summary>
    public class StatusQueryHandler
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly string _tableName;

        /// <summary>
        /// Default constructor used by Lambda runtime
        /// Initializes AWS clients using environment variables
        /// </summary>
        public StatusQueryHandler()
        {
            _dynamoDbClient = new AmazonDynamoDBClient();
            
            // Get table name from environment variable (set by CDK)
            _tableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") 
                ?? throw new InvalidOperationException("DYNAMODB_TABLE_NAME environment variable not set");
            
            Console.WriteLine($"StatusQueryHandler initialized. Table: {_tableName}");
        }

        /// <summary>
        /// Constructor for testing with dependency injection
        /// </summary>
        public StatusQueryHandler(IAmazonDynamoDB dynamoDbClient, string tableName)
        {
            _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        /// <summary>
        /// Lambda handler method for status query requests
        /// </summary>
        /// <param name="request">API Gateway proxy request with JobId in path parameters</param>
        /// <param name="context">Lambda execution context for logging</param>
        /// <returns>API Gateway proxy response with job status or error</returns>
        public async Task<APIGatewayProxyResponse> HandleStatusQueryAsync(
            APIGatewayProxyRequest request,
            ILambdaContext context)
        {
            context.Logger.LogInformation($"Status query request received. Request ID: {context.AwsRequestId}");

            try
            {
                // Extract JobId from path parameters
                if (request.PathParameters == null || !request.PathParameters.ContainsKey("jobId"))
                {
                    context.Logger.LogError("JobId not found in path parameters");
                    return CreateErrorResponse(400, "Missing jobId in request path");
                }

                string jobId = request.PathParameters["jobId"];
                context.Logger.LogInformation($"Querying status for Job ID: {jobId}");

                // Validate JobId format (basic validation)
                if (string.IsNullOrWhiteSpace(jobId) || jobId.Length < 10)
                {
                    context.Logger.LogError($"Invalid JobId format: {jobId}");
                    return CreateErrorResponse(400, "Invalid jobId format");
                }

                // Query DynamoDB for the job
                var job = await GetJobFromDynamoDbAsync(jobId, context);

                if (job == null)
                {
                    context.Logger.LogWarning($"Job not found: {jobId}");
                    return CreateErrorResponse(404, $"Job not found: {jobId}");
                }

                // Build response based on job status
                var response = BuildJobStatusResponse(job);
                context.Logger.LogInformation($"Status query successful for Job ID: {jobId}, Status: {job.Status}");

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Headers = new Dictionary<string, string>
                    {
                        { "Content-Type", "application/json" },
                        { "Access-Control-Allow-Origin", "*" }, // CORS
                        { "Access-Control-Allow-Methods", "GET, OPTIONS" }
                    },
                    Body = JsonSerializer.Serialize(response, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    })
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing status query: {ex.Message}");
                context.Logger.LogError($"Stack trace: {ex.StackTrace}");
                return CreateErrorResponse(500, "Internal server error while querying job status");
            }
        }

        /// <summary>
        /// Retrieves job details from DynamoDB
        /// </summary>
        private async Task<ProcessingMetadata?> GetJobFromDynamoDbAsync(string jobId, ILambdaContext context)
        {
            try
            {
                var request = new GetItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "JobId", new AttributeValue { S = jobId } }
                    }
                };

                context.Logger.LogInformation($"Querying DynamoDB table: {_tableName} for JobId: {jobId}");
                var response = await _dynamoDbClient.GetItemAsync(request);

                if (response.Item == null || response.Item.Count == 0)
                {
                    context.Logger.LogInformation($"No item found in DynamoDB for JobId: {jobId}");
                    return null;
                }

                context.Logger.LogInformation($"Successfully retrieved item from DynamoDB for JobId: {jobId}");

                // Parse DynamoDB item into ProcessingMetadata
                return ParseDynamoDbItem(response.Item);
            }
            catch (AmazonDynamoDBException ex)
            {
                context.Logger.LogError($"DynamoDB error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Parses DynamoDB item into ProcessingMetadata object
        /// </summary>
        private ProcessingMetadata ParseDynamoDbItem(Dictionary<string, AttributeValue> item)
        {
            var metadata = new ProcessingMetadata
            {
                JobId = item.ContainsKey("JobId") ? item["JobId"].S : string.Empty,
                OriginalFileName = item.ContainsKey("OriginalFileName") ? item["OriginalFileName"].S : string.Empty,
                FileType = item.ContainsKey("FileType") ? item["FileType"].S : string.Empty,
                FileSize = item.ContainsKey("FileSize") && long.TryParse(item["FileSize"].N, out var size) ? size : 0,
                Status = item.ContainsKey("Status") ? item["Status"].S : ProcessingStatus.Pending.ToString(),
                InputS3Key = item.ContainsKey("InputS3Key") ? item["InputS3Key"].S : string.Empty,
                UploadedAt = item.ContainsKey("UploadedAt") ? item["UploadedAt"].S : string.Empty,
                ProcessingStartedAt = item.ContainsKey("ProcessingStartedAt") ? item["ProcessingStartedAt"].S : null,
                CompletedAt = item.ContainsKey("CompletedAt") ? item["CompletedAt"].S : null,
                OutputS3Key = item.ContainsKey("OutputS3Key") ? item["OutputS3Key"].S : null,
                ErrorMessage = item.ContainsKey("ErrorMessage") ? item["ErrorMessage"].S : null
            };

            // Parse processed dimensions
            if (item.ContainsKey("ProcessedWidth") && int.TryParse(item["ProcessedWidth"].N, out var width))
            {
                metadata.ProcessedWidth = width;
            }

            if (item.ContainsKey("ProcessedHeight") && int.TryParse(item["ProcessedHeight"].N, out var height))
            {
                metadata.ProcessedHeight = height;
            }

            return metadata;
        }

        /// <summary>
        /// Builds a user-friendly status response
        /// </summary>
        private object BuildJobStatusResponse(ProcessingMetadata job)
        {
            // Calculate processing duration if applicable
            TimeSpan? processingDuration = null;
            if (!string.IsNullOrEmpty(job.ProcessingStartedAt) && !string.IsNullOrEmpty(job.CompletedAt))
            {
                if (DateTime.TryParse(job.ProcessingStartedAt, out var startTime) && 
                    DateTime.TryParse(job.CompletedAt, out var endTime))
                {
                    processingDuration = endTime - startTime;
                }
            }

            // Base response with common fields
            var response = new Dictionary<string, object>
            {
                { "jobId", job.JobId },
                { "status", job.Status },
                { "fileName", job.OriginalFileName },
                { "fileType", job.FileType },
                { "fileSize", job.FileSize },
                { "uploadedAt", job.UploadedAt ?? "N/A" }
            };

            // Add status-specific fields based on status string
            switch (job.Status)
            {
                case "Pending":
                    response["message"] = "Job is queued for processing";
                    break;

                case "Processing":
                    response["message"] = "Job is currently being processed";
                    response["processingStartedAt"] = job.ProcessingStartedAt ?? "N/A";
                    
                    // Calculate time in processing
                    if (!string.IsNullOrEmpty(job.ProcessingStartedAt) && DateTime.TryParse(job.ProcessingStartedAt, out var startedAt))
                    {
                        var timeInProcessing = DateTime.UtcNow - startedAt;
                        response["timeInProcessing"] = $"{timeInProcessing.TotalSeconds:F1} seconds";
                    }
                    break;

                case "Completed":
                    response["message"] = "Job completed successfully";
                    response["processingStartedAt"] = job.ProcessingStartedAt ?? "N/A";
                    response["completedAt"] = job.CompletedAt ?? "N/A";
                    response["outputFile"] = job.OutputS3Key ?? "N/A";
                    response["processedDimensions"] = $"{job.ProcessedWidth ?? 0}x{job.ProcessedHeight ?? 0}";
                    
                    if (processingDuration.HasValue)
                    {
                        response["processingDuration"] = $"{processingDuration.Value.TotalSeconds:F1} seconds";
                    }
                    break;

                case "Failed":
                    response["message"] = "Job processing failed";
                    response["error"] = job.ErrorMessage ?? "Unknown error";
                    response["processingStartedAt"] = job.ProcessingStartedAt ?? "N/A";
                    response["failedAt"] = job.CompletedAt ?? "N/A";
                    break;
            }

            return response;
        }

        /// <summary>
        /// Creates a standardized error response
        /// </summary>
        private APIGatewayProxyResponse CreateErrorResponse(int statusCode, string message)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = statusCode,
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "Access-Control-Allow-Origin", "*" }, // CORS
                    { "Access-Control-Allow-Methods", "GET, OPTIONS" }
                },
                Body = JsonSerializer.Serialize(new
                {
                    error = message,
                    statusCode = statusCode
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            };
        }
    }
}
