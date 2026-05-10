using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using LambdaHandlers.Models;

namespace LambdaHandlers.Handlers
{
    /// <summary>
    /// IMAGE PROCESSING LAMBDA HANDLER
    /// 
    /// PURPOSE:
    /// This Lambda function is triggered automatically when a new image is uploaded to S3.
    /// It processes the image (resize, transform) and saves the result to the output bucket.
    /// 
    /// TRIGGER:
    /// EventBridge rule detects S3 PutObject events in the input bucket
    /// 
    /// WORKFLOW:
    /// 1. S3 event notification arrives (image uploaded to input bucket)
    /// 2. Lambda downloads the original image from S3
    /// 3. Lambda processes the image (resize to 800x600, convert to JPEG)
    /// 4. Lambda simulates long processing (30-40 seconds) for learning purposes
    /// 5. Lambda uploads processed image to output bucket
    /// 6. Lambda updates DynamoDB with processing results
    /// 
    /// WHY EVENT-DRIVEN?
    /// - Automatic: No manual trigger needed, happens when upload occurs
    /// - Scalable: Multiple uploads = multiple Lambda instances automatically
    /// - Decoupled: Upload Lambda doesn't know about processing Lambda
    /// - Cost-effective: Only runs when there's work to do
    /// </summary>
    public class ImageProcessingHandler
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly string _inputBucketName;
        private readonly string _outputBucketName;
        private readonly string _dynamoDbTableName;

        /// <summary>
        /// Default constructor - Lambda runtime uses this
        /// Reads configuration from environment variables
        /// </summary>
        public ImageProcessingHandler()
        {
            _s3Client = new AmazonS3Client();
            _dynamoDbClient = new AmazonDynamoDBClient();
            
            // These environment variables are set by CDK during deployment
            _inputBucketName = Environment.GetEnvironmentVariable("INPUT_BUCKET_NAME") 
                ?? throw new Exception("INPUT_BUCKET_NAME environment variable not set");
            _outputBucketName = Environment.GetEnvironmentVariable("OUTPUT_BUCKET_NAME") 
                ?? throw new Exception("OUTPUT_BUCKET_NAME environment variable not set");
            _dynamoDbTableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") 
                ?? throw new Exception("DYNAMODB_TABLE_NAME environment variable not set");
        }

        /// <summary>
        /// Constructor for dependency injection (useful for testing)
        /// </summary>
        public ImageProcessingHandler(
            IAmazonS3 s3Client,
            IAmazonDynamoDB dynamoDbClient,
            string inputBucketName,
            string outputBucketName,
            string dynamoDbTableName)
        {
            _s3Client = s3Client;
            _dynamoDbClient = dynamoDbClient;
            _inputBucketName = inputBucketName;
            _outputBucketName = outputBucketName;
            _dynamoDbTableName = dynamoDbTableName;
        }

        /// <summary>
        /// Main Lambda handler method - receives events from EventBridge
        /// 
        /// IMPORTANT: EventBridge Event Format
        /// When S3 sends events to EventBridge, they are wrapped in a CloudWatch Event:
        /// {
        ///   "version": "0",
        ///   "id": "event-id",
        ///   "source": "aws.s3",
        ///   "detail-type": "Object Created",
        ///   "detail": {
        ///     "bucket": { "name": "bucket-name" },
        ///     "object": { "key": "file-key", "size": 123 }
        ///   }
        /// }
        /// 
        /// The S3 details are in the "detail" field as JSON.
        /// </summary>
        public async Task HandleEventBridgeEventAsync(CloudWatchEvent<JsonElement> cloudWatchEvent, ILambdaContext context)
        {
            context.Logger.LogInformation("=== Image Processing Lambda Started ===");
            context.Logger.LogInformation($"Event Source: {cloudWatchEvent.Source}");
            context.Logger.LogInformation($"Detail Type: {cloudWatchEvent.DetailType}");

            try
            {
                // Extract S3 event details from EventBridge event
                var detail = cloudWatchEvent.Detail;
                
                // Parse bucket and object information
                var bucketName = detail.GetProperty("bucket").GetProperty("name").GetString();
                var objectKey = detail.GetProperty("object").GetProperty("key").GetString();
                
                context.Logger.LogInformation($"Processing: Bucket={bucketName}, Key={objectKey}");

                // ===================================================
                // STEP 1: EXTRACT JOB ID FROM S3 KEY
                // ===================================================
                var jobId = ExtractJobIdFromS3Key(objectKey, context);
                if (string.IsNullOrEmpty(jobId))
                {
                    context.Logger.LogWarning($"Could not extract JobId from key: {objectKey}. Skipping.");
                    return;
                }

                context.Logger.LogInformation($"JobId: {jobId}");

                // ===================================================
                // STEP 2: UPDATE STATUS TO "PROCESSING"
                // ===================================================
                await UpdateJobStatus(jobId, ProcessingStatus.Processing, context);

                // ===================================================
                // STEP 3: DOWNLOAD IMAGE FROM S3
                // ===================================================
                context.Logger.LogInformation($"Downloading image from S3...");
                var imageStream = await DownloadImageFromS3Async(bucketName, objectKey, context);

                // ===================================================
                // STEP 4: PROCESS THE IMAGE
                // ===================================================
                context.Logger.LogInformation($"Processing image...");
                var processedImageStream = await ProcessImageAsync(imageStream, context);

                // ===================================================
                // STEP 5: SIMULATE LONG PROCESSING (LEARNING PURPOSE)
                // ===================================================
                context.Logger.LogInformation("Simulating long processing (30-40 seconds)...");
                var delay = new Random().Next(30, 41);
                await Task.Delay(TimeSpan.FromSeconds(delay));
                context.Logger.LogInformation($"Simulated processing completed ({delay} seconds)");

                // ===================================================
                // STEP 6: UPLOAD PROCESSED IMAGE TO OUTPUT BUCKET
                // ===================================================
                var outputKey = $"processed/{jobId}/{Path.GetFileNameWithoutExtension(objectKey)}_processed.jpg";
                context.Logger.LogInformation($"Uploading processed image to: {outputKey}");
                await UploadProcessedImageAsync(processedImageStream, outputKey, context);

                // ===================================================
                // STEP 7: UPDATE DYNAMODB WITH RESULTS
                // ===================================================
                context.Logger.LogInformation("Updating DynamoDB with processing results...");
                await UpdateJobCompletion(jobId, outputKey, 800, 600, context);

                context.Logger.LogInformation($"=== Processing completed successfully for JobId: {jobId} ===");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing EventBridge event: {ex.Message}");
                context.Logger.LogError($"Stack trace: {ex.StackTrace}");
                
                // Try to update job status to Failed
                try
                {
                    var detail = cloudWatchEvent.Detail;
                    var objectKey = detail.GetProperty("object").GetProperty("key").GetString();
                    var jobId = ExtractJobIdFromS3Key(objectKey, context);
                    if (!string.IsNullOrEmpty(jobId))
                    {
                        await UpdateJobStatusWithError(jobId, ex.Message, context);
                    }
                }
                catch (Exception updateEx)
                {
                    context.Logger.LogError($"Failed to update job status: {updateEx.Message}");
                }
                
                throw; // Re-throw so EventBridge knows it failed and can retry
            }
        }

        /// <summary>
        /// Extracts the Job ID from the S3 object key
        /// Expected format: jobs/{jobId}/{filename}
        /// </summary>
        private string ExtractJobIdFromS3Key(string s3Key, ILambdaContext context)
        {
            try
            {
                // Split the key: "jobs/123-456-789/image.png" -> ["jobs", "123-456-789", "image.png"]
                var parts = s3Key.Split('/');
                
                // We expect at least 3 parts: folder, jobId, filename
                if (parts.Length >= 3 && parts[0] == "jobs")
                {
                    return parts[1]; // The Job ID
                }
                
                context.Logger.LogWarning($"S3 key doesn't match expected format: {s3Key}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error extracting JobId from key: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Downloads the image from S3 into a memory stream
        /// 
        /// WHY MEMORY STREAM?
        /// Lambda has temporary disk space (/tmp) but it's limited.
        /// For small images, memory is faster and simpler.
        /// For large files (>100MB), you'd use /tmp directory instead.
        /// </summary>
        private async Task<MemoryStream> DownloadImageFromS3Async(string bucketName, string key, ILambdaContext context)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                using var response = await _s3Client.GetObjectAsync(request);
                var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // Reset position to start for reading
                
                context.Logger.LogInformation($"Downloaded image: {memoryStream.Length} bytes");
                return memoryStream;
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error downloading from S3: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processes the image using ImageSharp library
        /// 
        /// WHAT IS IMAGESHARP?
        /// ImageSharp is a cross-platform 2D graphics library for .NET
        /// It can:
        /// - Load various image formats (PNG, JPEG, GIF, BMP, etc.)
        /// - Resize, crop, rotate images
        /// - Apply filters and effects
        /// - Save in different formats
        /// 
        /// WHY RESIZE TO 800x600?
        /// This is just an example size. In real applications:
        /// - Thumbnails: 150x150
        /// - Mobile: 480x640
        /// - Web: 1920x1080
        /// - You might generate multiple sizes
        /// </summary>
        private async Task<MemoryStream> ProcessImageAsync(MemoryStream inputStream, ILambdaContext context)
        {
            try
            {
                // Load the image from the input stream
                using var image = await Image.LoadAsync(inputStream);
                
                context.Logger.LogInformation($"Original image size: {image.Width}x{image.Height}");

                // Resize the image to 800x600
                // Mode.Max maintains aspect ratio and fits within bounds
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(800, 600),
                    Mode = ResizeMode.Max // Maintains aspect ratio
                }));

                context.Logger.LogInformation($"Resized image to: {image.Width}x{image.Height}");

                // Save the processed image to a new memory stream as JPEG
                var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
                {
                    Quality = 85 // 0-100, 85 is good balance of quality vs size
                });

                outputStream.Position = 0; // Reset for reading
                context.Logger.LogInformation($"Processed image size: {outputStream.Length} bytes");
                
                return outputStream;
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing image: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Uploads the processed image to the output S3 bucket
        /// </summary>
        private async Task UploadProcessedImageAsync(MemoryStream imageStream, string key, ILambdaContext context)
        {
            try
            {
                var request = new PutObjectRequest
                {
                    BucketName = _outputBucketName,
                    Key = key,
                    InputStream = imageStream,
                    ContentType = "image/jpeg",
                    // Add metadata for tracking
                    Metadata =
                    {
                        ["ProcessedBy"] = "MediaProcessor-ProcessingLambda",
                        ["ProcessedAt"] = DateTime.UtcNow.ToString("o")
                    }
                };

                await _s3Client.PutObjectAsync(request);
                context.Logger.LogInformation($"Uploaded processed image to: {_outputBucketName}/{key}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error uploading to S3: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates the job status in DynamoDB to "Processing"
        /// </summary>
        private async Task UpdateJobStatus(string jobId, ProcessingStatus status, ILambdaContext context)
        {
            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = _dynamoDbTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["JobId"] = new AttributeValue { S = jobId }
                    },
                    UpdateExpression = "SET #status = :status, ProcessingStartedAt = :startTime",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status" // "Status" is a reserved word in DynamoDB
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new AttributeValue { S = status.ToString() },
                        [":startTime"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                };

                await _dynamoDbClient.UpdateItemAsync(request);
                context.Logger.LogInformation($"Updated job status to {status} for JobId: {jobId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error updating job status: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates the job in DynamoDB with completion details
        /// </summary>
        private async Task UpdateJobCompletion(string jobId, string outputKey, int width, int height, ILambdaContext context)
        {
            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = _dynamoDbTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["JobId"] = new AttributeValue { S = jobId }
                    },
                    UpdateExpression = "SET #status = :status, CompletedAt = :completedTime, " +
                                      "OutputS3Key = :outputKey, ProcessedWidth = :width, ProcessedHeight = :height",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new AttributeValue { S = ProcessingStatus.Completed.ToString() },
                        [":completedTime"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                        [":outputKey"] = new AttributeValue { S = outputKey },
                        [":width"] = new AttributeValue { N = width.ToString() },
                        [":height"] = new AttributeValue { N = height.ToString() }
                    }
                };

                await _dynamoDbClient.UpdateItemAsync(request);
                context.Logger.LogInformation($"Updated job completion for JobId: {jobId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error updating job completion: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates the job status to Failed with error message
        /// </summary>
        private async Task UpdateJobStatusWithError(string jobId, string errorMessage, ILambdaContext context)
        {
            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = _dynamoDbTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["JobId"] = new AttributeValue { S = jobId }
                    },
                    UpdateExpression = "SET #status = :status, ErrorMessage = :error, CompletedAt = :completedTime",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new AttributeValue { S = ProcessingStatus.Failed.ToString() },
                        [":error"] = new AttributeValue { S = errorMessage },
                        [":completedTime"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                };

                await _dynamoDbClient.UpdateItemAsync(request);
                context.Logger.LogInformation($"Updated job to Failed status for JobId: {jobId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error updating failed job status: {ex.Message}");
                throw;
            }
        }
    }
}
