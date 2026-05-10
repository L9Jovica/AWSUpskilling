using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using LambdaHandlers.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace LambdaHandlers.Handlers
{
    /// <summary>
    /// Lambda handler for uploading media files through API Gateway
    /// 
    /// WORKFLOW:
    /// 1. API Gateway receives HTTP POST with image data
    /// 2. This Lambda function is invoked with the request
    /// 3. Generate a unique Job ID (GUID)
    /// 4. Upload image to S3 input bucket
    /// 5. Create DynamoDB record with job metadata
    /// 6. Return Job ID to the user
    /// 
    /// WHY LAMBDA?
    /// - No servers to manage (serverless)
    /// - Automatically scales with traffic
    /// - Pay only for execution time
    /// - Integrates seamlessly with API Gateway, S3, and DynamoDB
    /// </summary>
    public class ImageUploadHandler
    {
        // AWS SDK clients - these interact with AWS services
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonDynamoDB _dynamoDbClient;
        
        // Configuration from environment variables (set by CDK)
        private readonly string _inputBucketName;
        private readonly string _dynamoDbTableName;
        
        /// <summary>
        /// Default constructor - AWS Lambda uses this
        /// Creates AWS service clients using default credentials
        /// </summary>
        public ImageUploadHandler()
        {
            // Create S3 client - this is how we upload files to S3
            _s3Client = new AmazonS3Client();
            
            // Create DynamoDB client - this is how we write metadata
            _dynamoDbClient = new AmazonDynamoDBClient();
            
            // Read configuration from environment variables
            // CDK will set these when deploying the Lambda function
            _inputBucketName = Environment.GetEnvironmentVariable("INPUT_BUCKET_NAME") 
                ?? throw new InvalidOperationException("INPUT_BUCKET_NAME environment variable is not set");
                
            _dynamoDbTableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") 
                ?? throw new InvalidOperationException("DYNAMODB_TABLE_NAME environment variable is not set");
        }
        
        /// <summary>
        /// Constructor for dependency injection (used in tests)
        /// </summary>
        public ImageUploadHandler(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDbClient, 
            string inputBucketName, string dynamoDbTableName)
        {
            _s3Client = s3Client;
            _dynamoDbClient = dynamoDbClient;
            _inputBucketName = inputBucketName;
            _dynamoDbTableName = dynamoDbTableName;
        }
        
        /// <summary>
        /// Lambda function handler - this is the entry point
        /// 
        /// WHAT IS APIGatewayProxyRequest?
        /// - Contains HTTP request details (headers, body, query parameters)
        /// - Provided by API Gateway when it invokes this Lambda
        /// 
        /// WHAT IS ILambdaContext?
        /// - Provides runtime information (request ID, remaining time, etc.)
        /// - Used for logging and monitoring
        /// 
        /// WHAT IS APIGatewayProxyResponse?
        /// - HTTP response that API Gateway will return to the client
        /// - Contains status code, headers, and body
        /// </summary>
        public async Task<APIGatewayProxyResponse> HandleUploadAsync(
            APIGatewayProxyRequest request, 
            ILambdaContext context)
        {
            try
            {
                // Log the incoming request for debugging
                context.Logger.LogInformation($"Upload request received. Request ID: {context.AwsRequestId}");
                
                // ===== STEP 1: PARSE AND VALIDATE REQUEST =====
                // The request body contains JSON with image data
                var uploadRequest = ParseUploadRequest(request.Body, context);
                
                // ===== STEP 2: GENERATE UNIQUE JOB ID =====
                // GUID (Globally Unique Identifier) ensures no collisions
                var jobId = Guid.NewGuid().ToString();
                context.Logger.LogInformation($"Generated Job ID: {jobId}");
                
                // ===== STEP 3: UPLOAD IMAGE TO S3 =====
                // S3 key structure: jobs/{jobId}/{originalFileName}
                // This organizes files by job for easy management
                var s3Key = $"jobs/{jobId}/{uploadRequest.FileName}";
                
                await UploadToS3Async(
                    uploadRequest.ImageData, 
                    s3Key, 
                    uploadRequest.ContentType,
                    context);
                
                context.Logger.LogInformation($"Image uploaded to S3: {s3Key}");
                
                // ===== STEP 4: CREATE DYNAMODB RECORD =====
                // Store job metadata so we can track processing status
                var metadata = new ProcessingMetadata
                {
                    JobId = jobId,
                    Status = ProcessingStatus.Pending.ToString(),
                    UploadedAt = DateTime.UtcNow.ToString("o"), // ISO 8601 format
                    OriginalFileName = uploadRequest.FileName,
                    FileSize = uploadRequest.ImageData.Length,
                    FileType = uploadRequest.ContentType,
                    InputS3Key = s3Key
                };
                
                await SaveMetadataToDynamoDbAsync(metadata, context);
                
                context.Logger.LogInformation($"Metadata saved to DynamoDB for Job ID: {jobId}");
                
                // ===== STEP 5: RETURN SUCCESS RESPONSE =====
                // Return Job ID to the client so they can check status later
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200, // HTTP 200 OK
                    Headers = new Dictionary<string, string>
                    {
                        { "Content-Type", "application/json" },
                        { "Access-Control-Allow-Origin", "*" } // Enable CORS for browser clients
                    },
                    Body = JsonSerializer.Serialize(new
                    {
                        jobId = jobId,
                        message = "Image uploaded successfully",
                        status = "pending"
                    })
                };
            }
            catch (Exception ex)
            {
                // Log the error for troubleshooting
                context.Logger.LogError($"Error processing upload: {ex.Message}");
                context.Logger.LogError($"Stack trace: {ex.StackTrace}");
                
                // Return error response to client
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500, // HTTP 500 Internal Server Error
                    Headers = new Dictionary<string, string>
                    {
                        { "Content-Type", "application/json" },
                        { "Access-Control-Allow-Origin", "*" }
                    },
                    Body = JsonSerializer.Serialize(new
                    {
                        error = "Failed to process upload",
                        message = ex.Message
                    })
                };
            }
        }
        
        /// <summary>
        /// Parses the upload request from JSON
        /// 
        /// EXPECTED JSON FORMAT:
        /// {
        ///   "fileName": "photo.jpg",
        ///   "contentType": "image/jpeg",
        ///   "imageData": "base64_encoded_image_data_here"
        /// }
        /// </summary>
        private UploadRequest ParseUploadRequest(string requestBody, ILambdaContext context)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                throw new ArgumentException("Request body is empty");
            }
            
            try
            {
                var jsonDocument = JsonDocument.Parse(requestBody);
                var root = jsonDocument.RootElement;
                
                // Extract required fields from JSON
                var fileName = root.GetProperty("fileName").GetString() 
                    ?? throw new ArgumentException("fileName is required");
                    
                var contentType = root.GetProperty("contentType").GetString() 
                    ?? throw new ArgumentException("contentType is required");
                    
                var imageDataBase64 = root.GetProperty("imageData").GetString() 
                    ?? throw new ArgumentException("imageData is required");
                
                // Convert base64 string to byte array
                // Base64 is used to encode binary data (images) as text for JSON transport
                var imageData = Convert.FromBase64String(imageDataBase64);
                
                context.Logger.LogInformation($"Parsed upload request: {fileName}, {contentType}, {imageData.Length} bytes");
                
                return new UploadRequest
                {
                    FileName = fileName,
                    ContentType = contentType,
                    ImageData = imageData
                };
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Invalid JSON in request body: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Uploads image data to S3
        /// 
        /// WHY S3?
        /// - Durable storage (won't lose your data)
        /// - Scalable (handles files of any size)
        /// - Cheap (~$0.023 per GB per month)
        /// - Integrates with other AWS services
        /// </summary>
        private async Task UploadToS3Async(byte[] imageData, string s3Key, string contentType, ILambdaContext context)
        {
            using var memoryStream = new MemoryStream(imageData);
            
            var putRequest = new PutObjectRequest
            {
                BucketName = _inputBucketName,
                Key = s3Key,
                InputStream = memoryStream,
                ContentType = contentType,
                
                // Metadata can be retrieved later without downloading the entire file
                Metadata =
                {
                    ["uploaded-by"] = "ImageUploadHandler",
                    ["uploaded-at"] = DateTime.UtcNow.ToString("o")
                }
            };
            
            // Upload to S3 (asynchronous operation)
            var response = await _s3Client.PutObjectAsync(putRequest);
            
            // Check if upload succeeded
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"Failed to upload to S3. Status: {response.HttpStatusCode}");
            }
        }
        
        /// <summary>
        /// Saves job metadata to DynamoDB
        /// 
        /// WHY DYNAMODB?
        /// - Fast lookups by Job ID (single-digit millisecond latency)
        /// - Scales automatically
        /// - Serverless (no servers to manage)
        /// - Pay per request
        /// </summary>
        private async Task SaveMetadataToDynamoDbAsync(ProcessingMetadata metadata, ILambdaContext context)
        {
            // Convert our C# object to DynamoDB format
            // DynamoDB uses AttributeValue objects to represent data
            var item = new Dictionary<string, AttributeValue>
            {
                ["JobId"] = new AttributeValue { S = metadata.JobId },
                ["Status"] = new AttributeValue { S = metadata.Status },
                ["UploadedAt"] = new AttributeValue { S = metadata.UploadedAt },
                ["OriginalFileName"] = new AttributeValue { S = metadata.OriginalFileName },
                ["FileSize"] = new AttributeValue { N = metadata.FileSize.ToString() },
                ["FileType"] = new AttributeValue { S = metadata.FileType },
                ["InputS3Key"] = new AttributeValue { S = metadata.InputS3Key }
            };
            
            var putRequest = new PutItemRequest
            {
                TableName = _dynamoDbTableName,
                Item = item
            };
            
            // Write to DynamoDB (asynchronous operation)
            var response = await _dynamoDbClient.PutItemAsync(putRequest);
            
            // Check if write succeeded
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"Failed to save to DynamoDB. Status: {response.HttpStatusCode}");
            }
        }
        
        /// <summary>
        /// Helper class to represent the parsed upload request
        /// </summary>
        private class UploadRequest
        {
            public string FileName { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public byte[] ImageData { get; set; } = Array.Empty<byte>();
        }
    }
}
