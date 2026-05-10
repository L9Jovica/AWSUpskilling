using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using System.Collections.Generic;
using Constructs;

namespace Infrastructure
{
    /// <summary>
    /// Infrastructure Stack - Defines all AWS resources for the Serverless Media Processor
    /// This class uses AWS CDK to define infrastructure as code in C#
    /// </summary>
    public class InfrastructureStack : Stack
    {
        // Public properties to expose resources to other parts of the stack
        public IBucket InputBucket { get; private set; } = null!;
        public IBucket OutputBucket { get; private set; } = null!;
        public ITable JobsTable { get; private set; } = null!;
        public IFunction UploadFunction { get; private set; } = null!;
        public IFunction ProcessingFunction { get; private set; } = null!;
        public IFunction StatusQueryFunction { get; private set; } = null!;
        public RestApi RestApi { get; private set; } = null!;
        public IVpc Vpc { get; private set; } = null!;
        public ICluster EcsCluster { get; private set; } = null!;
        public IApplicationLoadBalancer AdminDashboardAlb { get; private set; } = null!;
        
        /// <summary>
        /// Constructor - Called by CDK to create the stack
        /// </summary>
        /// <param name="scope">The parent construct (usually the App)</param>
        /// <param name="id">Unique identifier for this stack</param>
        /// <param name="props">Stack configuration properties</param>
        public InfrastructureStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // ==========================================
            // PHASE 0: VPC NETWORKING (Foundation)
            // ==========================================
            
            CreateVpc();
            
            // ==========================================
            // PHASE 1: S3 BUCKETS
            // ==========================================
            
            CreateS3Buckets();
            
            // ==========================================
            // PHASE 2: DYNAMODB TABLE
            // ==========================================
            
            CreateDynamoDBTable();
            
            // ==========================================
            // PHASE 3: LAMBDA FUNCTIONS + API GATEWAY
            // ==========================================
            
            CreateUploadLambdaAndApi();
            
            // ==========================================
            // PHASE 4: STATUS QUERY LAMBDA
            // ==========================================
            
            CreateStatusQueryLambda();
            
            // ==========================================
            // PHASE 5: PROCESSING LAMBDA + EVENTBRIDGE
            // ==========================================
            
            CreateProcessingLambdaAndEventBridge();
            
            // ==========================================
            // PHASE 6: ECS CLUSTER (Admin Dashboard)
            // ==========================================
            
            CreateEcsClusterAndAdminDashboard();
            
            // ==========================================
            // PHASE 7: SNS/SQS (User Notifications)
            // ==========================================
            
            CreateNotificationSystem();
        }
        
        /// <summary>
        /// Creates S3 buckets for storing input and output media files
        /// 
        /// WHY S3?
        /// - Highly durable object storage (99.999999999% durability)
        /// - Automatically scales to handle any amount of data
        /// - Pay only for what you use
        /// - Integrates seamlessly with other AWS services
        /// 
        /// KEY CONCEPTS:
        /// - Bucket: Container for objects (files)
        /// - Object: File + metadata
        /// - Bucket names must be globally unique across ALL of AWS
        /// </summary>
        private void CreateS3Buckets()
        {
            // -------------------------------------------
            // INPUT BUCKET - Stores original uploaded images
            // -------------------------------------------
            InputBucket = new Bucket(this, "MediaInputBucket", new BucketProps
            {
                // Bucket names must be globally unique
                // Using a suffix to avoid conflicts (you could use GUID or timestamp)
                BucketName = $"media-processor-input-jsavic-{this.Account}",
                
                // VERSIONING: Disabled (not needed for this use case)
                // If enabled, S3 keeps all versions of an object
                Versioned = false,
                
                // PUBLIC ACCESS: Blocked for security
                // This prevents anyone from accessing files without proper authentication
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                
                // ENCRYPTION: Server-side encryption with S3-managed keys
                // Data is encrypted at rest using AES-256
                Encryption = BucketEncryption.S3_MANAGED,
                
                // REMOVAL POLICY: What happens when we delete the CDK stack?
                // DESTROY = Delete the bucket and all its contents (good for dev/learning)
                // RETAIN = Keep the bucket even after stack deletion (good for production)
                RemovalPolicy = RemovalPolicy.DESTROY,
                
                // AUTO DELETE OBJECTS: When stack is deleted, remove all objects first
                // Required when using RemovalPolicy.DESTROY
                // Without this, CloudFormation cannot delete a non-empty bucket
                AutoDeleteObjects = true,
                
                // LIFECYCLE RULES: Automatically manage object lifecycle
                // This rule deletes objects after 7 days to save costs
                LifecycleRules = new[]
                {
                    new LifecycleRule
                    {
                        // Human-readable description
                        Id = "DeleteOldInputFilesAfter7Days",
                        
                        // Enable this rule
                        Enabled = true,
                        
                        // Delete objects after 7 days
                        // This keeps costs low during learning
                        Expiration = Duration.Days(7)
                    }
                }
            });
            
            // Add CloudFormation output to display bucket name after deployment
            // This makes it easy to find the bucket name in AWS Console
            new CfnOutput(this, "InputBucketName", new CfnOutputProps
            {
                Value = InputBucket.BucketName,
                Description = "S3 bucket for input media files"
            });
            
            // -------------------------------------------
            // OUTPUT BUCKET - Stores processed images
            // -------------------------------------------
            OutputBucket = new Bucket(this, "MediaOutputBucket", new BucketProps
            {
                BucketName = $"media-processor-output-jsavic-{this.Account}",
                Versioned = false,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = BucketEncryption.S3_MANAGED,
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteObjects = true,
                
                // Output files can be kept longer (30 days)
                LifecycleRules = new[]
                {
                    new LifecycleRule
                    {
                        Id = "DeleteOldOutputFilesAfter30Days",
                        Enabled = true,
                        Expiration = Duration.Days(30)
                    }
                }
            });
            
            new CfnOutput(this, "OutputBucketName", new CfnOutputProps
            {
                Value = OutputBucket.BucketName,
                Description = "S3 bucket for processed media files"
            });
            
            // ==========================================
            // SUCCESS!
            // ==========================================
            // At this point, we've defined two S3 buckets in code
            // When we run 'cdk deploy', CloudFormation will:
            // 1. Create the buckets in your AWS account
            // 2. Apply all the configurations (encryption, lifecycle, etc.)
            // 3. Output the bucket names for easy reference
            // 
            // COST: S3 is very affordable
            // - First 5GB: FREE (per month)
            // - After that: ~$0.023 per GB per month
            // - Requests: ~$0.005 per 1,000 PUT requests
            // ==========================================
        }
        
        /// <summary>
        /// Creates DynamoDB table for storing media processing job metadata
        /// 
        /// WHY DYNAMODB?
        /// - Fully managed NoSQL database (no servers to maintain)
        /// - Automatically scales to handle any amount of traffic
        /// - Single-digit millisecond latency
        /// - Perfect for key-value lookups (get job by JobId)
        /// - Pay only for what you use
        /// 
        /// KEY CONCEPTS:
        /// - Table: Collection of items (like a table in SQL)
        /// - Item: A single record (like a row in SQL)
        /// - Attribute: A field in an item (like a column in SQL)
        /// - Primary Key: Unique identifier for each item
        ///   * Partition Key (required): Used to distribute data across partitions
        ///   * Sort Key (optional): Allows multiple items with same partition key
        /// 
        /// OUR DATA MODEL:
        /// Each job item will contain:
        /// - JobId (String) - Unique ID (UUID/GUID)
        /// - Status (String) - pending, processing, completed, failed
        /// - UploadedAt (String) - ISO timestamp
        /// - ProcessingStartedAt (String) - ISO timestamp (optional)
        /// - CompletedAt (String) - ISO timestamp (optional)
        /// - OriginalFileName (String) - Original file name
        /// - FileSize (Number) - Size in bytes
        /// - FileType (String) - MIME type (image/jpeg, etc.)
        /// - InputS3Key (String) - S3 key for input file
        /// - OutputS3Key (String) - S3 key for processed file (optional)
        /// - ProcessedWidth (Number) - Width after processing (optional)
        /// - ProcessedHeight (Number) - Height after processing (optional)
        /// - ErrorMessage (String) - Error details if failed (optional)
        /// </summary>
        private void CreateDynamoDBTable()
        {
            // -------------------------------------------
            // MEDIA PROCESSING JOBS TABLE
            // -------------------------------------------
            JobsTable = new Table(this, "MediaProcessingJobsTable", new TableProps
            {
                // Table name (will appear in AWS Console)
                TableName = "MediaProcessingJobs-JSavic",
                
                // ===== PRIMARY KEY DESIGN =====
                // Partition Key: JobId
                // - This is the only key we need
                // - We'll query jobs by their unique ID
                // - No Sort Key needed (one item per JobId)
                PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute
                {
                    Name = "JobId",
                    Type = AttributeType.STRING  // UUID/GUID format
                },
                
                // ===== BILLING MODE =====
                // PAY_PER_REQUEST vs PROVISIONED:
                // 
                // PAY_PER_REQUEST (ON-DEMAND):
                // - Pay only for actual reads/writes
                // - No capacity planning needed
                // - Automatically scales up/down
                // - Perfect for: Unpredictable traffic, new apps, learning
                // - Cost: ~$1.25 per million writes, ~$0.25 per million reads
                //
                // PROVISIONED:
                // - Pre-specify read/write capacity units
                // - Lower cost if traffic is predictable and consistent
                // - Need to monitor and adjust capacity
                // - Can set up auto-scaling
                // - Perfect for: Predictable traffic, production apps
                //
                // For learning, we'll use PAY_PER_REQUEST
                BillingMode = BillingMode.PAY_PER_REQUEST,
                
                // ===== DATA PROTECTION =====
                // Point-in-Time Recovery (PITR)
                // - Continuous backups for last 35 days
                // - Can restore table to any second in that window
                // - Protects against accidental deletions
                // - Additional cost: ~$0.20 per GB per month
                // For learning: Disabled to save costs
                PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
                {
                    PointInTimeRecoveryEnabled = false
                },
                
                // ===== ENCRYPTION =====
                // Data is encrypted at rest using AWS-managed keys
                // - Encryption happens automatically
                // - No performance impact
                // - FREE (no additional cost)
                Encryption = TableEncryption.AWS_MANAGED,
                
                // ===== CLEANUP POLICY =====
                // What happens when we delete the CDK stack?
                RemovalPolicy = RemovalPolicy.DESTROY,  // Delete the table and all data
                
                // ===== TIME TO LIVE (TTL) =====
                // Automatically delete old items after a specified time
                // - Helps manage costs by removing old data
                // - FREE feature (no additional charges)
                // - We won't enable this for now (data expires via S3 lifecycle rules)
                TimeToLiveAttribute = null,
                
                // ===== STREAMS =====
                // DynamoDB Streams capture changes to items
                // - Can trigger Lambda functions when data changes
                // - Useful for: Real-time notifications, data replication, auditing
                // - We don't need this for our use case
                Stream = null
            });
            
            // Add CloudFormation output for easy reference
            new CfnOutput(this, "JobsTableName", new CfnOutputProps
            {
                Value = JobsTable.TableName,
                Description = "DynamoDB table for media processing jobs",
            });
            
            // ==========================================
            // DATA ACCESS PATTERNS
            // ==========================================
            // Our application will use these operations:
            //
            // 1. CREATE JOB (PutItem):
            //    - Upload Lambda creates a new job
            //    - Write item with JobId and initial status
            //
            // 2. UPDATE STATUS (UpdateItem):
            //    - Processing Lambda updates status to "processing"
            //    - Processing Lambda updates status to "completed" with results
            //
            // 3. QUERY JOB (GetItem):
            //    - Status Lambda retrieves job by JobId
            //    - Returns all job metadata to user
            //
            // All operations use JobId as the key - very efficient!
            // ==========================================
            
            // ==========================================
            // COST ESTIMATION
            // ==========================================
            // Assuming 1,000 jobs per month:
            // - 1,000 writes (upload) = $0.00125
            // - 2,000 updates (processing start + complete) = $0.0025
            // - 1,000 reads (status checks) = $0.00025
            // - Storage: ~1 KB per item = 1 MB = $0.00025
            // TOTAL: ~$0.005 per month (less than 1 cent!)
            //
            // First 25 GB storage: FREE
            // First 25 Write Units: FREE
            // First 25 Read Units: FREE
            // ==========================================
        }
        
        /// <summary>
        /// Creates the Upload Lambda function and API Gateway HTTP API
        /// 
        /// WHY LAMBDA?
        /// AWS Lambda is a "serverless" compute service. This means:
        /// - You write code, AWS runs it - no servers to manage
        /// - Automatic scaling: 1 request or 1 million requests, Lambda handles it
        /// - Pay per use: Only charged when code is actually running
        /// - Built-in high availability across multiple data centers
        /// 
        /// HOW LAMBDA PRICING WORKS:
        /// You're charged based on:
        /// 1. Number of requests (first 1 million/month are FREE)
        /// 2. Duration (time your code runs) + Memory allocated
        /// Example: 512MB RAM, 1 second execution = $0.0000083 per request
        /// 
        /// WHY API GATEWAY?
        /// API Gateway is AWS's managed API service:
        /// - Creates RESTful HTTP endpoints
        /// - Handles all HTTP protocol details for you
        /// - Automatic SSL/TLS (HTTPS)
        /// - Request throttling and rate limiting
        /// - Integrates directly with Lambda (no glue code needed)
        /// 
        /// HOW THEY WORK TOGETHER:
        /// 1. User sends: POST https://api-url.com/upload with image data
        /// 2. API Gateway receives the HTTP request
        /// 3. API Gateway invokes your Lambda function
        /// 4. Lambda processes the request (uploads to S3, saves to DynamoDB)
        /// 5. Lambda returns response
        /// 6. API Gateway sends HTTP response back to user
        /// </summary>
        private void CreateUploadLambdaAndApi()
        {
            // ===================================================
            // STEP 1: CREATE THE LAMBDA FUNCTION
            // ===================================================
            
            // WHAT IS A LAMBDA FUNCTION CONSTRUCT?
            // This CDK construct creates:
            // - The Lambda function itself
            // - An IAM role with permissions
            // - CloudWatch log group for logs
            // - All necessary configurations
            
            UploadFunction = new Function(this, "ImageUploadFunction", new FunctionProps
            {
                // RUNTIME: The programming environment
                // We're using .NET 8 which runs on Amazon Linux 2023
                Runtime = Runtime.DOTNET_8,
                
                // HANDLER: Tells Lambda which method to call
                // Format: AssemblyName::Namespace.ClassName::MethodName
                // Lambda will call: LambdaHandlers.Handlers.ImageUploadHandler::HandleUploadAsync
                Handler = "LambdaHandlers::LambdaHandlers.Handlers.ImageUploadHandler::HandleUploadAsync",
                
                // CODE: Where is the Lambda code?
                // In CI/CD, we use pre-built code from CodeBuild (no Docker bundling needed)
                // The buildspec.yml publishes to this directory
                Code = Code.FromAsset("../LambdaHandlers/bin/Release/net8.0/publish"),
                
                // FUNCTION NAME: Human-readable name in AWS Console
                
                // MEMORY: How much RAM to allocate (128 MB to 10,240 MB)
                // More memory = More CPU power proportionally
                // 512 MB is a good balance for image uploads
                // WHY? Enough for JSON parsing + S3 upload, not wasteful
                MemorySize = 512,
                
                // TIMEOUT: Maximum execution time (1 second to 15 minutes)
                // If function runs longer, Lambda kills it
                // 30 seconds is generous for upload operations
                // WHY? Network upload to S3 + DynamoDB write should be <5 seconds
                //      30 seconds provides safety margin for slow networks
                Timeout = Duration.Seconds(30),
                
                // ENVIRONMENT VARIABLES: Configuration passed to Lambda
                // Our code reads these to know which resources to use
                // This is better than hardcoding because:
                // - Easy to change without redeploying code
                // - Different environments can use different resources
                Environment = new Dictionary<string, string>
                {
                    ["INPUT_BUCKET_NAME"] = InputBucket.BucketName,
                    ["DYNAMODB_TABLE_NAME"] = JobsTable.TableName
                },
                
                // DESCRIPTION: Appears in AWS Console
                Description = "Handles media file uploads from API Gateway",
                
                // LOG RETENTION: How long to keep CloudWatch logs
                // 7 days for development (saves money)
                // Production might use 30, 90, or 365 days
                LogRetention = RetentionDays.ONE_WEEK
            });
            
            // ===================================================
            // STEP 2: GRANT PERMISSIONS TO LAMBDA
            // ===================================================
            
            // SECURITY IN AWS: Everything is deny-by-default
            // Lambda needs explicit permission to access S3 and DynamoDB
            
            // GRANT S3 WRITE ACCESS
            // This adds an IAM policy allowing Lambda to upload files
            // Behind the scenes, CDK creates a policy like:
            // {
            //   "Effect": "Allow",
            //   "Action": "s3:PutObject",
            //   "Resource": "arn:aws:s3:::bucket-name/*"
            // }
            InputBucket.GrantWrite(UploadFunction);
            
            // GRANT DYNAMODB WRITE ACCESS
            // This allows Lambda to create new items in the table
            // Behind the scenes: Allows "dynamodb:PutItem" action
            JobsTable.GrantWriteData(UploadFunction);
            
            // ===================================================
            // STEP 3: CREATE API GATEWAY REST API
            // ===================================================
            
            // REST API: Traditional but fully-featured API Gateway
            // Includes request/response transformation, validation, and more
            RestApi = new RestApi(this, "MediaProcessorApi", new RestApiProps
            {
                RestApiName = "MediaProcessorApi-JSavic",
                Description = "REST API for serverless media processing",
                
                // CORS: Allow browser clients from any origin
                // This adds the necessary Access-Control-Allow-* headers
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = Cors.ALL_ORIGINS,     // ["*"]
                    AllowMethods = Cors.ALL_METHODS,     // ["GET", "POST", "PUT", etc.]
                    AllowHeaders = new[] { "Content-Type", "Authorization", "X-Api-Key" }
                }
            });
            
            // ===================================================
            // STEP 4: CREATE LAMBDA INTEGRATION
            // ===================================================
            
            // INTEGRATION: Tells API Gateway how to call Lambda
            // PROXY integration passes the entire HTTP request to Lambda
            var uploadIntegration = new LambdaIntegration(UploadFunction, new LambdaIntegrationOptions
            {
                // PROXY: Lambda receives full HTTP request details
                // Lambda must return properly formatted HTTP response
                Proxy = true,
                
                // TIMEOUT: How long API Gateway waits for Lambda
                // Should be <= Lambda timeout
                Timeout = Duration.Seconds(29)
            });
            
            // ===================================================
            // STEP 5: ADD ROUTE TO API
            // ===================================================
            
            // ROOT RESOURCE: The base path of the API
            // We'll add "upload" under root
            var uploadResource = RestApi.Root.AddResource("upload");
            
            // ADD METHOD: POST /upload → Lambda
            uploadResource.AddMethod("POST", uploadIntegration, new MethodOptions
            {
                // API KEY: Not required for learning (makes testing easier)
                ApiKeyRequired = false,
                
                // REQUEST VALIDATION: API Gateway can validate before calling Lambda
                // For now, we'll let Lambda handle validation
                RequestValidatorOptions = new RequestValidatorOptions
                {
                    ValidateRequestBody = false,
                    ValidateRequestParameters = false
                }
            });
            
            // ===================================================
            // STEP 6: OUTPUT THE API URL
            // ===================================================
            
            new CfnOutput(this, "ApiUrl", new CfnOutputProps
            {
                // The full URL will be: https://{restApiId}.execute-api.{region}.amazonaws.com/prod/upload
                Value = $"{RestApi.Url}upload",
                Description = "REST API Gateway endpoint URL for image upload",
            });
            
            // ===================================================
            // WHAT HAPPENS DURING DEPLOYMENT?
            // ===================================================
            // 1. CDK builds your Lambda code (dotnet publish)
            //    - Compiles your C# code to IL
            //    - Packages dependencies
            //    - Creates deployment package (.zip)
            // 
            // 2. CDK uploads Lambda package to S3
            //    - Uses the CDK bootstrap bucket
            //    - Calculates hash to detect changes
            // 
            // 3. CloudFormation creates Lambda function
            //    - Extracts code from S3
            //    - Sets up execution environment
            //    - Configures permissions
            // 
            // 4. CloudFormation creates API Gateway
            //    - Sets up REST API resource
            //    - Configures routes and methods
            //    - Creates deployment and stage
            // 
            // 5. API Gateway gets permission to invoke Lambda
            //    - CDK automatically adds invoke permission
            //    - This is why integration "just works"
            // 
            // 6. You get a URL like:
            //    https://abc123xyz.execute-api.eu-west-1.amazonaws.com/prod/upload
            
            // ===================================================
            // HOW TO TEST AFTER DEPLOYMENT
            // ===================================================
            // Use curl, Postman, or any HTTP client:
            // 
            // PowerShell example:
            // $base64Image = [Convert]::ToBase64String([IO.File]::ReadAllBytes("test.jpg"))
            // $body = @{
            //     fileName = "test.jpg"
            //     contentType = "image/jpeg"
            //     imageData = $base64Image
            // } | ConvertTo-Json
            // 
            // Invoke-RestMethod -Method POST `
            //     -Uri "YOUR-API-URL/upload" `
            //     -Body $body `
            //     -ContentType "application/json"
            // 
            // Expected response:
            // {
            //   "jobId": "12345678-1234-1234-1234-123456789abc",
            //   "message": "Image uploaded successfully",
            //   "status": "pending"
            // }
            
            // ===================================================
            // COST BREAKDOWN FOR THIS SETUP
            // ===================================================
            // Assuming 1,000 uploads per month:
            //
            // LAMBDA:
            // - Free tier: 1M requests/month, 400,000 GB-seconds
            // - Our usage: 1,000 requests × 2 seconds × 512MB = 1,000 GB-seconds
            // - Cost: $0 (within free tier!)
            //
            // API GATEWAY (REST API):
            // - Free tier: 1M requests/month (first 12 months)
            // - After free tier: 1,000 × $0.0000035 = $0.0035
            // - Cost: ~$0.00 for learning
            //
            // S3 UPLOADS:
            // - 1,000 PUT requests × $0.000005 = $0.005
            //
            // DYNAMODB WRITES:
            // - Free tier: 25 write units/sec (enough for millions of writes)
            // - Cost: $0
            //
            // TOTAL: ~$0.01 per month (1 cent) - essentially free for learning!
            // ===================================================
        }
        
        /// <summary>
        /// Creates the Status Query Lambda function and adds it to the API Gateway
        /// 
        /// WHY STATUS QUERY?
        /// The status query endpoint allows users to check the progress of their image processing jobs.
        /// This is essential for asynchronous processing patterns where:
        /// - Upload returns immediately with a Job ID
        /// - Processing happens in the background (can take 30-40 seconds)
        /// - User needs to poll or check when processing is complete
        /// 
        /// PATTERN: Request-Response vs Poll Pattern
        /// - Upload: Request-Response (synchronous, immediate)
        /// - Processing: Fire-and-forget (asynchronous, background)
        /// - Status Query: Poll pattern (check progress repeatedly)
        /// 
        /// This creates a GET /status/{jobId} endpoint
        /// </summary>
        private void CreateStatusQueryLambda()
        {
            // ===================================================
            // STEP 1: CREATE STATUS QUERY LAMBDA FUNCTION
            // ===================================================
            
            StatusQueryFunction = new Function(this, "StatusQueryFunction", new FunctionProps
            {
                // Use .NET 8 runtime
                Runtime = Runtime.DOTNET_8,
                
                // Handler points to our StatusQueryHandler class
                Handler = "LambdaHandlers::LambdaHandlers.Handlers.StatusQueryHandler::HandleStatusQueryAsync",
                
                // Code location with bundling options
                Code = Code.FromAsset("../LambdaHandlers/bin/Release/net8.0/publish"),
                
                // Function name in AWS
                
                // MEMORY: 256 MB is sufficient for DynamoDB queries
                // Status queries are lightweight:
                // - Parse request
                // - Query DynamoDB (single item lookup)
                // - Format JSON response
                // No heavy processing needed
                MemorySize = 256,
                
                // TIMEOUT: 10 seconds is plenty
                // DynamoDB GetItem is very fast (typically <100ms)
                // 10 seconds provides safety margin for:
                // - Cold starts (~500ms for .NET)
                // - Network latency
                // - Rare DynamoDB throttling
                Timeout = Duration.Seconds(10),
                
                // ENVIRONMENT VARIABLES: Only needs DynamoDB table name
                Environment = new Dictionary<string, string>
                {
                    ["DYNAMODB_TABLE_NAME"] = JobsTable.TableName
                },
                
                Description = "Queries DynamoDB to retrieve job processing status",
                
                // CloudWatch log retention
                LogRetention = RetentionDays.ONE_WEEK
            });
            
            // ===================================================
            // STEP 2: GRANT DYNAMODB READ PERMISSIONS
            // ===================================================
            
            // SECURITY: Lambda needs permission to read from DynamoDB
            // This grants GetItem permission (read a single item)
            // Behind the scenes, CDK creates an IAM policy:
            // {
            //   "Effect": "Allow",
            //   "Action": ["dynamodb:GetItem"],
            //   "Resource": "arn:aws:dynamodb:region:account:table/table-name"
            // }
            JobsTable.GrantReadData(StatusQueryFunction);
            
            // ===================================================
            // STEP 3: CREATE API GATEWAY INTEGRATION
            // ===================================================
            
            // LAMBDA INTEGRATION: Connects API Gateway to Lambda
            // Proxy mode means Lambda receives full HTTP request details
            var statusIntegration = new LambdaIntegration(StatusQueryFunction, new LambdaIntegrationOptions
            {
                Proxy = true,
                Timeout = Duration.Seconds(9) // Slightly less than Lambda timeout
            });
            
            // ===================================================
            // STEP 4: ADD STATUS ROUTE TO API
            // ===================================================
            
            // CREATE "status" RESOURCE under root
            // This creates: /status
            var statusResource = RestApi.Root.AddResource("status");
            
            // ADD PATH PARAMETER for jobId
            // This creates: /status/{jobId}
            // The {jobId} is a path variable that gets passed to Lambda
            var statusJobResource = statusResource.AddResource("{jobId}");
            
            // ADD GET METHOD: GET /status/{jobId} → Lambda
            // When user calls: GET /api/status/abc-123-def
            // Lambda receives: {"pathParameters": {"jobId": "abc-123-def"}}
            statusJobResource.AddMethod("GET", statusIntegration, new MethodOptions
            {
                ApiKeyRequired = false,
                
                // REQUEST PARAMETERS: Define path variable
                RequestParameters = new Dictionary<string, bool>
                {
                    // Mark jobId as required in the path
                    ["method.request.path.jobId"] = true
                }
            });
            
            // ===================================================
            // STEP 5: OUTPUT THE STATUS QUERY URL
            // ===================================================
            
            new CfnOutput(this, "StatusQueryUrl", new CfnOutputProps
            {
                // Example URL: https://abc123.execute-api.eu-west-1.amazonaws.com/prod/status/{jobId}
                Value = $"{RestApi.Url}status/{{jobId}}",
                Description = "REST API endpoint to query job status (replace {{jobId}} with actual job ID)",
            });
            
            // ===================================================
            // HOW TO USE THE STATUS ENDPOINT
            // ===================================================
            // 
            // 1. User uploads image → Gets Job ID: "abc-123-def"
            // 2. User polls status:
            //    GET https://api-url/prod/status/abc-123-def
            // 
            // 3. Responses based on status:
            // 
            //    PENDING:
            //    {
            //      "jobId": "abc-123-def",
            //      "status": "Pending",
            //      "message": "Job is queued for processing",
            //      "fileName": "image.jpg",
            //      "uploadedAt": "2026-05-07T10:00:00Z"
            //    }
            // 
            //    PROCESSING:
            //    {
            //      "jobId": "abc-123-def",
            //      "status": "Processing",
            //      "message": "Job is currently being processed",
            //      "fileName": "image.jpg",
            //      "uploadedAt": "2026-05-07T10:00:00Z",
            //      "processingStartedAt": "2026-05-07T10:00:05Z",
            //      "timeInProcessing": "15.3 seconds"
            //    }
            // 
            //    COMPLETED:
            //    {
            //      "jobId": "abc-123-def",
            //      "status": "Completed",
            //      "message": "Job completed successfully",
            //      "fileName": "image.jpg",
            //      "uploadedAt": "2026-05-07T10:00:00Z",
            //      "processingStartedAt": "2026-05-07T10:00:05Z",
            //      "completedAt": "2026-05-07T10:00:40Z",
            //      "outputFile": "processed/abc-123-def/image_processed.jpg",
            //      "processedDimensions": "800x600",
            //      "processingDuration": "35.2 seconds"
            //    }
            // 
            //    FAILED:
            //    {
            //      "jobId": "abc-123-def",
            //      "status": "Failed",
            //      "message": "Job processing failed",
            //      "fileName": "image.jpg",
            //      "uploadedAt": "2026-05-07T10:00:00Z",
            //      "processingStartedAt": "2026-05-07T10:00:05Z",
            //      "failedAt": "2026-05-07T10:00:10Z",
            //      "error": "Invalid image format"
            //    }
            // 
            // ===================================================
            // POLLING BEST PRACTICES
            // ===================================================
            // 
            // Since processing takes 30-40 seconds, clients should:
            // 1. Start polling after upload
            // 2. Poll every 5-10 seconds
            // 3. Stop polling when status is Completed or Failed
            // 4. Use exponential backoff if getting many "Pending" responses
            // 
            // Example polling logic:
            // - Poll immediately after upload
            // - If Pending: Wait 5 seconds, poll again
            // - If Processing: Wait 5 seconds, poll again
            // - If Completed/Failed: Stop polling, show result
            // 
            // ===================================================
            // COST ANALYSIS
            // ===================================================
            // 
            // Assuming 1,000 jobs/month with 5 status checks per job:
            // 
            // LAMBDA INVOCATIONS:
            // - 5,000 requests × $0.0000002 = $0.001
            // - Duration: 256 MB × 50ms × 5,000 = $0.00052
            // - Total Lambda: ~$0.002
            // 
            // API GATEWAY:
            // - 5,000 requests × $0.0000035 = $0.0175
            // 
            // DYNAMODB READS:
            // - 5,000 GetItem × $0.00000025 = $0.00125
            // - (Covered by free tier: 25 read units/sec)
            // 
            // TOTAL: ~$0.02 per month (2 cents)
            // 
            // ===================================================
        }
        
        /// <summary>
        /// Creates the Processing Lambda function and EventBridge rule to trigger it
        /// 
        /// WHY EVENTBRIDGE?
        /// EventBridge is AWS's serverless event bus service. It routes events from sources
        /// to targets. In our case:
        /// - Source: S3 (when files are uploaded)
        /// - Target: Lambda (to process the images)
        /// 
        /// KEY BENEFITS:
        /// - Decoupling: S3 doesn't know about Lambda, they're independent
        /// - Filtering: We can filter which S3 events trigger Lambda
        /// - Multiple targets: One S3 event could trigger multiple Lambdas
        /// - Easy monitoring: All events flow through EventBridge for visibility
        /// 
        /// ALTERNATIVE APPROACHES:
        /// 1. S3 Event Notifications (direct): Simpler but less flexible
        /// 2. S3 → SNS → Lambda: More complex, useful for fan-out
        /// 3. S3 → SQS → Lambda: Better for handling bursts/retries
        /// 
        /// We use EventBridge because it's the modern, recommended approach
        /// </summary>
        private void CreateProcessingLambdaAndEventBridge()
        {
            // ===================================================
            // STEP 1: CREATE THE PROCESSING LAMBDA FUNCTION
            // ===================================================
            
            ProcessingFunction = new Function(this, "ImageProcessingFunction", new FunctionProps
            {
                // RUNTIME: .NET 8 on Amazon Linux 2023
                Runtime = Runtime.DOTNET_8,
                
                // HANDLER: Which method to invoke
                // Format: Assembly::Namespace.Class::Method
                Handler = "LambdaHandlers::LambdaHandlers.Handlers.ImageProcessingHandler::HandleS3EventAsync",
                
                // CODE: Build and package the Lambda code
                Code = Code.FromAsset("../LambdaHandlers/bin/Release/net8.0/publish"),
                
                // FUNCTION NAME: Appears in AWS Console
                
                // MEMORY: Image processing is CPU-intensive
                // More memory = More CPU power in Lambda
                // 1024 MB gives us good processing speed
                MemorySize = 1024,
                
                // TIMEOUT: Processing takes 30-40 seconds + image operations
                // We need at least 60 seconds, let's use 90 for safety
                // Maximum Lambda timeout is 15 minutes (900 seconds)
                Timeout = Duration.Seconds(90),
                
                // ENVIRONMENT VARIABLES: Configuration for the Lambda
                Environment = new Dictionary<string, string>
                {
                    ["INPUT_BUCKET_NAME"] = InputBucket.BucketName,
                    ["OUTPUT_BUCKET_NAME"] = OutputBucket.BucketName,
                    ["DYNAMODB_TABLE_NAME"] = JobsTable.TableName
                },
                
                // DESCRIPTION: Documentation
                Description = "Processes uploaded images (resize, transform) and updates status in DynamoDB",
                
                // LOG RETENTION: Keep logs for 1 week during development
                LogRetention = RetentionDays.ONE_WEEK
            });
            
            // ===================================================
            // STEP 2: GRANT PERMISSIONS TO LAMBDA
            // ===================================================
            
            // GRANT S3 READ ACCESS (input bucket)
            // Lambda needs to download the original image
            InputBucket.GrantRead(ProcessingFunction);
            
            // GRANT S3 WRITE ACCESS (output bucket)
            // Lambda needs to upload the processed image
            OutputBucket.GrantWrite(ProcessingFunction);
            
            // GRANT DYNAMODB READ/WRITE ACCESS
            // Lambda needs to update job status and completion details
            JobsTable.GrantReadWriteData(ProcessingFunction);
            
            // ===================================================
            // STEP 3: CREATE EVENTBRIDGE RULE
            // ===================================================
            
            // WHAT IS AN EVENTBRIDGE RULE?
            // A rule matches incoming events and routes them to targets.
            // Components:
            // - Event pattern: Defines which events to match
            // - Targets: Where to send matching events (our Lambda)
            
            var s3EventRule = new Rule(this, "S3UploadRule", new RuleProps
            {
                RuleName = "MediaProcessor-S3Upload-JSavic",
                Description = "Triggers image processing when files are uploaded to S3 input bucket",
                
                // EVENT PATTERN: Defines which events we want to capture
                // This pattern matches S3 PutObject events in our input bucket
                EventPattern = new EventPattern
                {
                    // EVENT SOURCE: Where the event comes from
                    Source = new[] { "aws.s3" },
                    
                    // DETAIL TYPE: What kind of S3 event
                    // "Object Created" includes PutObject, POST, CompleteMultipartUpload, Copy
                    DetailType = new[] { "Object Created" },
                    
                    // DETAIL: Additional filtering on event details
                    Detail = new Dictionary<string, object>
                    {
                        // BUCKET NAME: Only events from our input bucket
                        ["bucket"] = new Dictionary<string, object>
                        {
                            ["name"] = new[] { InputBucket.BucketName }
                        },
                        
                        // OBJECT KEY: Only process files in "jobs/" prefix
                        // This prevents processing files from other locations
                        ["object"] = new Dictionary<string, object>
                        {
                            ["key"] = new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["prefix"] = "jobs/"
                                }
                            }
                        }
                    }
                },
                
                // TARGETS: What to invoke when event matches
                // We add our Processing Lambda as the target
                Targets = new[]
                {
                    new LambdaFunction(ProcessingFunction, new LambdaFunctionProps
                    {
                        // RETRY POLICY: What if Lambda fails?
                        RetryAttempts = 2,  // Retry up to 2 times
                        MaxEventAge = Duration.Hours(2) // Give up after 2 hours
                    })
                }
            });
            
            // ===================================================
            // STEP 4: ENABLE EVENTBRIDGE FOR S3 BUCKET
            // ===================================================
            
            // IMPORTANT: S3 doesn't send events to EventBridge by default
            // We must explicitly enable EventBridge notifications on the bucket
            InputBucket.EnableEventBridgeNotification();
            
            // ===================================================
            // STEP 5: ADD CLOUDFORMATION OUTPUTS
            // ===================================================
            
            new CfnOutput(this, "ProcessingFunctionName", new CfnOutputProps
            {
                Value = ProcessingFunction.FunctionName,
                Description = "Name of the image processing Lambda function",
            });
            
            new CfnOutput(this, "EventBridgeRuleName", new CfnOutputProps
            {
                Value = s3EventRule.RuleName,
                Description = "Name of the EventBridge rule that triggers processing",
            });
            
            // ===================================================
            // WHAT HAPPENS AT RUNTIME?
            // ===================================================
            // 1. User uploads image via Upload Lambda → S3 input bucket
            // 2. S3 sends "Object Created" event to EventBridge
            // 3. EventBridge matches the event against our rule
            // 4. If matches: EventBridge invokes Processing Lambda
            // 5. Processing Lambda:
            //    - Downloads image from S3
            //    - Resizes/processes it
            //    - Waits 30-40 seconds (simulated delay)
            //    - Uploads result to output bucket
            //    - Updates DynamoDB with completion status
            
            // ===================================================
            // EVENT-DRIVEN ARCHITECTURE BENEFITS
            // ===================================================
            // ✅ SCALABILITY: 1 upload or 1000 uploads, it automatically scales
            // ✅ RELIABILITY: EventBridge retries failed Lambda invocations
            // ✅ DECOUPLING: Upload Lambda doesn't know about Processing Lambda
            // ✅ FLEXIBILITY: Easy to add more processing steps (thumbnails, watermarks, etc.)
            // ✅ COST-EFFECTIVE: Only pay when processing actually happens
            
            // ===================================================
            // MONITORING TIP
            // ===================================================
            // After deployment, check these in AWS Console:
            // 1. EventBridge → Rules → See rule details and metrics
            // 2. Lambda → Functions → ProcessingHandler → Monitor invocations
            // 3. CloudWatch → Log Groups → See processing logs
            // 4. DynamoDB → Tables → Check job status updates
            // ===================================================
        }
        
        /// <summary>
        /// Creates VPC (Virtual Private Cloud) with public and private subnets
        /// 
        /// 🎓 AWS VPC LEARNING MODULE
        /// ===================================================
        /// 
        /// WHAT IS VPC?
        /// VPC = Your own private network in AWS
        /// - Like having your own data center, but virtual
        /// - Complete isolation from other AWS customers
        /// - You control IP addresses, routing, security
        /// 
        /// WHY DO WE NEED VPC?
        /// - ECS Fargate tasks need network connectivity
        /// - Separate public resources (ALB) from private resources (ECS tasks)
        /// - Control traffic flow with security groups
        /// - Follow AWS best practices for security
        /// 
        /// VPC COMPONENTS EXPLAINED:
        /// ===================================================
        /// 
        /// 1. CIDR BLOCK (IP Range)
        ///    - Defines IP address range for VPC
        ///    - Example: 10.0.0.0/16 = 65,536 IP addresses (10.0.0.0 to 10.0.255.255)
        ///    - Uses private IP ranges (10.x, 172.x, 192.168.x)
        /// 
        /// 2. SUBNETS
        ///    - Subdivisions of VPC IP range
        ///    - Each subnet in one Availability Zone (AZ)
        ///    
        ///    PUBLIC SUBNET:
        ///    - Has route to Internet Gateway (IGW)
        ///    - Resources get public IP addresses
        ///    - Use for: Load balancers, bastion hosts
        ///    
        ///    PRIVATE SUBNET:
        ///    - No direct route to internet
        ///    - Uses NAT Gateway for outbound internet (but no inbound)
        ///    - Use for: Application servers, databases
        /// 
        /// 3. INTERNET GATEWAY (IGW)
        ///    - Allows VPC to communicate with internet
        ///    - Attached to VPC
        ///    - Enables inbound and outbound internet traffic
        /// 
        /// 4. NAT GATEWAY
        ///    - Lets private subnet resources access internet
        ///    - Only outbound traffic (for updates, API calls, etc.)
        ///    - Blocks all inbound traffic from internet
        ///    - Located in public subnet
        ///    
        ///    WHY NAT Gateway?
        ///    - ECS tasks need to pull Docker images from internet
        ///    - Applications need to call external APIs
        ///    - But we don't want direct internet access (security)
        /// 
        /// 5. AVAILABILITY ZONES (AZs)
        ///    - Isolated data centers in AWS region
        ///    - Example: eu-west-1a, eu-west-1b, eu-west-1c
        ///    - We create subnets in multiple AZs for high availability
        ///    - If one AZ fails, others continue working
        /// 
        /// 6. ROUTE TABLES
        ///    - Control where network traffic is directed
        ///    - Each subnet has a route table
        ///    
        ///    Public subnet route table:
        ///    - 10.0.0.0/16 → local (VPC)
        ///    - 0.0.0.0/0 → Internet Gateway (internet)
        ///    
        ///    Private subnet route table:
        ///    - 10.0.0.0/16 → local (VPC)
        ///    - 0.0.0.0/0 → NAT Gateway (internet, outbound only)
        /// 
        /// 7. SECURITY GROUPS
        ///    - Virtual firewalls for resources
        ///    - Control inbound and outbound traffic
        ///    - Stateful (if you allow inbound, response is automatically allowed)
        ///    - Applied to: EC2 instances, ECS tasks, RDS databases, etc.
        /// 
        /// NETWORK FLOW EXAMPLE:
        /// ===================================================
        /// User → Internet → Internet Gateway → ALB (public subnet) 
        ///   → ECS Task (private subnet) → NAT Gateway → Internet (for API calls)
        /// 
        /// COST BREAKDOWN:
        /// ===================================================
        /// - VPC: FREE
        /// - Subnets: FREE
        /// - Internet Gateway: FREE
        /// - NAT Gateway: ~$32/month (+ $0.045 per GB processed)
        /// - Route Tables: FREE
        /// - Security Groups: FREE
        /// 
        /// COST TIP: NAT Gateway is the expensive part. For dev/test, 
        /// consider using NAT Instance or VPC endpoints instead.
        /// 
        /// ===================================================
        /// </summary>
        private void CreateVpc()
        {
            // -------------------------------------------
            // CREATE VPC WITH HIGH AVAILABILITY
            // -------------------------------------------
            
            Vpc = new Vpc(this, "MediaProcessorVpc", new VpcProps
            {
                // IP RANGE: 10.0.0.0/16 provides 65,536 IP addresses
                // This is plenty for our use case
                Cidr = "10.0.0.0/16",
                
                // MAX AVAILABILITY ZONES: Use 2 AZs for high availability
                // AWS best practice: Always use at least 2 AZs
                // If one fails, the other keeps running
                MaxAzs = 2,
                
                // SUBNET CONFIGURATION
                SubnetConfiguration = new[]
                {
                    // PUBLIC SUBNETS (one per AZ)
                    // - Has Internet Gateway route
                    // - Resources get public IPs
                    // - Use for: ALB, NAT Gateway
                    new SubnetConfiguration
                    {
                        Name = "Public",
                        SubnetType = SubnetType.PUBLIC,
                        CidrMask = 24, // 10.0.0.0/24 and 10.0.1.0/24 (256 IPs each)
                    },
                    
                    // PRIVATE SUBNETS (one per AZ)
                    // - Has NAT Gateway route
                    // - No public IPs
                    // - Use for: ECS tasks, databases
                    new SubnetConfiguration
                    {
                        Name = "Private",
                        SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                        CidrMask = 24, // 10.0.2.0/24 and 10.0.3.0/24 (256 IPs each)
                    }
                },
                
                // NAT GATEWAYS: One per AZ for high availability
                // - Each private subnet uses NAT Gateway in its AZ
                // - If NAT Gateway fails, only that AZ is affected
                // - More expensive but more reliable
                NatGateways = 1, // Using 1 to save costs (can use 2 for production)
                
                // VPC NAME
                VpcName = "MediaProcessor-VPC",
                
                // DISABLE DEFAULT SECURITY GROUP RESTRICTION
                // This avoids creating a custom resource Lambda that requires S3 asset publishing
                // For CI/CD simplicity, we disable this. In production, you'd want to keep it enabled.
                RestrictDefaultSecurityGroup = false
            });
            
            // -------------------------------------------
            // ADD VPC FLOW LOGS (Optional but recommended)
            // -------------------------------------------
            // Flow Logs capture IP traffic going to/from network interfaces
            // Useful for: Security analysis, troubleshooting, monitoring
            // 
            // Commented out to save costs, but highly recommended for production:
            /*
            new FlowLog(this, "VpcFlowLog", new FlowLogProps
            {
                ResourceType = FlowLogResourceType.FromVpc(Vpc),
                Destination = FlowLogDestination.ToCloudWatchLogs()
            });
            */
            
            // -------------------------------------------
            // CLOUDFORMATION OUTPUT: VPC ID
            // -------------------------------------------
            new CfnOutput(this, "VpcId", new CfnOutputProps
            {
                Value = Vpc.VpcId,
                Description = "VPC ID for MediaProcessor",
            });
            
            // -------------------------------------------
            // WHAT CDK AUTOMATICALLY CREATES:
            // -------------------------------------------
            // ✅ VPC with CIDR 10.0.0.0/16
            // ✅ 2 public subnets (one per AZ): 10.0.0.0/24, 10.0.1.0/24
            // ✅ 2 private subnets (one per AZ): 10.0.2.0/24, 10.0.3.0/24
            // ✅ 1 Internet Gateway (attached to VPC)
            // ✅ 1 NAT Gateway (in public subnet)
            // ✅ Route tables configured correctly
            // ✅ Default network ACLs
            // ✅ Default security group (deny all inbound, allow all outbound)
            
            // -------------------------------------------
            // SECURITY BEST PRACTICES APPLIED:
            // -------------------------------------------
            // ✅ Private subnets for application tier (ECS)
            // ✅ Public subnets only for load balancers
            // ✅ NAT Gateway for secure outbound internet access
            // ✅ No direct internet access to application tier
            // ✅ Multi-AZ deployment for high availability
        }
        
        /// <summary>
        /// Creates ECS Fargate cluster with Admin Dashboard and Application Load Balancer
        /// 
        /// 🎓 AWS ECS FARGATE LEARNING MODULE
        /// ===================================================
        /// 
        /// WHAT IS ECS (Elastic Container Service)?
        /// - AWS managed container orchestration service
        /// - Runs Docker containers at scale
        /// - Like Kubernetes, but AWS-managed and simpler
        /// 
        /// WHAT IS FARGATE?
        /// - Serverless compute engine for containers
        /// - You don't manage servers (EC2 instances)
        /// - Just deploy containers, AWS handles infrastructure
        /// - Pay per second of container runtime
        /// 
        /// ECS vs FARGATE vs EC2:
        /// ===================================================
        /// ECS on EC2:
        /// - You manage EC2 instances
        /// - More control, more complexity
        /// - Cheaper for long-running workloads
        /// 
        /// ECS on FARGATE (What we're using):
        /// - AWS manages infrastructure
        /// - Less control, less complexity
        /// - Pay for what you use
        /// - Great for: Web apps, APIs, scheduled tasks
        /// 
        /// KEY CONCEPTS:
        /// ===================================================
        /// 
        /// 1. CLUSTER
        ///    - Logical grouping of tasks/services
        ///    - Can run multiple services in one cluster
        ///    - Like a namespace for your containers
        /// 
        /// 2. TASK DEFINITION
        ///    - Blueprint for your application
        ///    - Specifies: Docker image, CPU, memory, ports, environment variables
        ///    - Like a Kubernetes Pod spec
        /// 
        /// 3. SERVICE
        ///    - Runs and maintains desired number of tasks
        ///    - Auto-restarts failed tasks
        ///    - Integrates with load balancers
        ///    - Like a Kubernetes Deployment
        /// 
        /// 4. TASK
        ///    - Running instance of task definition
        ///    - Like a Kubernetes Pod
        ///    - Has its own IP address, network interface
        /// 
        /// APPLICATION LOAD BALANCER (ALB):
        /// ===================================================
        /// - Layer 7 load balancer (HTTP/HTTPS)
        /// - Distributes traffic across multiple targets
        /// - Health checks ensure traffic only goes to healthy tasks
        /// - SSL/TLS termination
        /// - Path-based routing (/api → backend, /admin → dashboard)
        /// 
        /// ALB vs NLB vs CLB:
        /// - ALB (Application): HTTP/HTTPS, advanced routing
        /// - NLB (Network): TCP/UDP, ultra-high performance
        /// - CLB (Classic): Legacy, not recommended
        /// 
        /// ADMIN DASHBOARD USE CASE:
        /// ===================================================
        /// - View all processing jobs
        /// - Monitor system health
        /// - Retry failed jobs
        /// - View CloudWatch metrics
        /// - Simple web interface
        /// 
        /// NETWORK ARCHITECTURE:
        /// ===================================================
        /// Internet → ALB (public subnet) → ECS Tasks (private subnet)
        /// 
        /// Security:
        /// - ALB in public subnet (accessible from internet)
        /// - ECS tasks in private subnet (not directly accessible)
        /// - Security group allows traffic only from ALB
        /// 
        /// COST BREAKDOWN:
        /// ===================================================
        /// ALB: ~$16/month (+ $0.008 per LCU-hour)
        /// Fargate: $0.04048/vCPU/hour + $0.004445/GB/hour
        /// Example: 0.25 vCPU + 0.5 GB = ~$7/month (running 24/7)
        /// 
        /// COST TIP: Use smaller task sizes (0.25 vCPU) for low-traffic apps
        /// 
        /// ===================================================
        /// </summary>
        private void CreateEcsClusterAndAdminDashboard()
        {
            // -------------------------------------------
            // CREATE ECS CLUSTER
            // -------------------------------------------
            
            EcsCluster = new Cluster(this, "MediaProcessorCluster", new ClusterProps
            {
                ClusterName = "MediaProcessor-Cluster",
                
                // VPC where cluster will run
                Vpc = Vpc,
                
                // CONTAINER INSIGHTS: CloudWatch monitoring for containers
                // - CPU, memory, network metrics
                // - Costs extra, but very useful for production
                ContainerInsights = true
            });
            
            // -------------------------------------------
            // CREATE FARGATE SERVICE WITH ALB
            // -------------------------------------------
            
            // ApplicationLoadBalancedFargateService is a high-level construct
            // that creates ALB + ECS Service + Task Definition automatically
            var adminDashboard = new ApplicationLoadBalancedFargateService(
                this, 
                "AdminDashboard", 
                new ApplicationLoadBalancedFargateServiceProps
                {
                    // CLUSTER
                    Cluster = EcsCluster,
                    
                    // SERVICE NAME
                    ServiceName = "AdminDashboard",
                    
                    // CPU: 256 = 0.25 vCPU (smallest Fargate size)
                    // Options: 256, 512, 1024, 2048, 4096
                    Cpu = 256,
                    
                    // MEMORY: 512 MB
                    // Must be compatible with CPU (see Fargate documentation)
                    // Valid pairs: 256 CPU → 512-2048 MB
                    MemoryLimitMiB = 512,
                    
                    // DESIRED TASK COUNT: How many tasks to run
                    // 1 = one container running
                    // 2+ = high availability (tasks in different AZs)
                    DesiredCount = 1, // Can increase to 2 for HA
                    
                    // TASK SUBNETS: Where to run tasks
                    // PRIVATE_WITH_EGRESS = private subnets with NAT Gateway
                    // Tasks can access internet (pull images, call APIs) but not accessible from internet
                    TaskSubnets = new SubnetSelection
                    {
                        SubnetType = SubnetType.PRIVATE_WITH_EGRESS
                    },
                    
                    // PUBLIC LOAD BALANCER: ALB in public subnets
                    PublicLoadBalancer = true,
                    
                    // TASK IMAGE OPTIONS: Docker container configuration
                    TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
                    {
                        // DOCKER IMAGE: Using nginx as placeholder
                        // In real implementation, you'd build custom admin dashboard
                        // Example: "youraccount.dkr.ecr.eu-west-1.amazonaws.com/admin-dashboard:latest"
                        Image = ContainerImage.FromRegistry("nginx:alpine"),
                        
                        // CONTAINER PORT: Port app listens on inside container
                        ContainerPort = 80,
                        
                        // ENVIRONMENT VARIABLES: Pass config to container
                        Environment = new Dictionary<string, string>
                        {
                            { "DYNAMODB_TABLE_NAME", JobsTable.TableName },
                            { "AWS_REGION", this.Region },
                            { "API_ENDPOINT", RestApi.Url }
                        },
                        
                        // CONTAINER NAME
                        ContainerName = "admin-dashboard"
                    },
                    
                    // LOAD BALANCER NAME
                    LoadBalancerName = "MediaProcessor-ALB",
                    
                    // HEALTH CHECK: ALB checks if task is healthy
                    // Unhealthy tasks are replaced automatically
                    HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
                    {
                        Command = new[] { "CMD-SHELL", "curl -f http://localhost/ || exit 1" },
                        Interval = Duration.Seconds(30),
                        Timeout = Duration.Seconds(5),
                        Retries = 3,
                        StartPeriod = Duration.Seconds(60)
                    }
                }
            );
            
            // -------------------------------------------
            // GRANT TASK PERMISSIONS TO ACCESS AWS RESOURCES
            // -------------------------------------------
            
            // Task needs to read from DynamoDB
            JobsTable.GrantReadData(adminDashboard.TaskDefinition.TaskRole);
            
            // Task needs to read from S3 (to display images)
            InputBucket.GrantRead(adminDashboard.TaskDefinition.TaskRole);
            OutputBucket.GrantRead(adminDashboard.TaskDefinition.TaskRole);
            
            // -------------------------------------------
            // CONFIGURE SECURITY GROUP FOR ECS TASKS
            // -------------------------------------------
            
            // Allow inbound traffic ONLY from ALB
            // This ensures tasks can't be accessed directly from internet
            adminDashboard.Service.Connections.AllowFrom(
                adminDashboard.LoadBalancer,
                Port.Tcp(80),
                "Allow traffic from ALB"
            );
            
            // -------------------------------------------
            // CONFIGURE AUTO SCALING (Optional)
            // -------------------------------------------
            
            // Auto Scaling automatically adds/removes tasks based on metrics
            var scaling = adminDashboard.Service.AutoScaleTaskCount(new Amazon.CDK.AWS.ApplicationAutoScaling.EnableScalingProps
            {
                MinCapacity = 1, // Minimum tasks
                MaxCapacity = 3  // Maximum tasks
            });
            
            // Scale up when CPU > 70%
            scaling.ScaleOnCpuUtilization("CpuScaling", new CpuUtilizationScalingProps
            {
                TargetUtilizationPercent = 70,
                ScaleInCooldown = Duration.Seconds(60),
                ScaleOutCooldown = Duration.Seconds(60)
            });
            
            // Scale up when memory > 80%
            scaling.ScaleOnMemoryUtilization("MemoryScaling", new MemoryUtilizationScalingProps
            {
                TargetUtilizationPercent = 80,
                ScaleInCooldown = Duration.Seconds(60),
                ScaleOutCooldown = Duration.Seconds(60)
            });
            
            // -------------------------------------------
            // STORE ALB REFERENCE
            // -------------------------------------------
            
            AdminDashboardAlb = adminDashboard.LoadBalancer;
            
            // -------------------------------------------
            // CLOUDFORMATION OUTPUTS
            // -------------------------------------------
            
            new CfnOutput(this, "AdminDashboardUrl", new CfnOutputProps
            {
                Value = $"http://{adminDashboard.LoadBalancer.LoadBalancerDnsName}",
                Description = "Admin Dashboard URL",
            });
            
            new CfnOutput(this, "EcsClusterName", new CfnOutputProps
            {
                Value = EcsCluster.ClusterName,
                Description = "ECS Cluster Name",
            });
            
            // -------------------------------------------
            // WHAT WAS CREATED:
            // -------------------------------------------
            // ✅ ECS Cluster
            // ✅ Fargate Task Definition (nginx container)
            // ✅ ECS Service (maintains 1 task, auto-restarts on failure)
            // ✅ Application Load Balancer (in public subnets)
            // ✅ Target Group (routes traffic to ECS tasks)
            // ✅ Security Groups (ALB → ECS communication)
            // ✅ IAM Roles (task execution role, task role)
            // ✅ CloudWatch Log Group (container logs)
            // ✅ Auto Scaling policies (CPU and memory based)
            
            // -------------------------------------------
            // NEXT STEPS:
            // -------------------------------------------
            // 1. Build custom admin dashboard Docker image
            // 2. Push image to Amazon ECR (Elastic Container Registry)
            // 3. Update task definition to use your image
            // 4. Deploy with `cdk deploy`
            // 5. Access dashboard at ALB DNS name
            
            // -------------------------------------------
            // PRODUCTION ENHANCEMENTS:
            // -------------------------------------------
            // 1. Add HTTPS (certificate from ACM)
            // 2. Add custom domain (Route 53)
            // 3. Add authentication (Cognito or ALB auth)
            // 4. Increase desired count to 2+ for HA
            // 5. Add WAF (Web Application Firewall)
            // 6. Add CloudFront CDN
        }
        
        /// <summary>
        /// Creates SNS/SQS notification system for user notifications
        /// 
        /// 🎓 AWS SNS & SQS LEARNING MODULE
        /// ===================================================
        /// 
        /// WHAT IS SNS (Simple Notification Service)?
        /// - Pub/Sub messaging service
        /// - Publishers send messages to topics
        /// - Subscribers receive messages from topics
        /// - Supports: Email, SMS, HTTP/S, Lambda, SQS
        /// 
        /// WHAT IS SQS (Simple Queue Service)?
        /// - Message queue service
        /// - Stores messages until they're processed
        /// - Guarantees message delivery
        /// - Supports: Dead-letter queues, FIFO queues
        /// 
        /// SNS vs SQS:
        /// ===================================================
        /// SNS (Topic):
        /// - Fan-out: One message → Many subscribers
        /// - Push model: Delivers immediately
        /// - No retention: If subscriber offline, message lost
        /// - Use for: Notifications, alerts, real-time events
        /// 
        /// SQS (Queue):
        /// - Point-to-point: One message → One consumer
        /// - Pull model: Consumer polls for messages
        /// - Retention: Messages stored up to 14 days
        /// - Use for: Async processing, decoupling, buffering
        /// 
        /// SNS + SQS TOGETHER (Fan-out pattern):
        /// ===================================================
        /// SNS Topic → Multiple SQS Queues
        /// - Reliable delivery (SQS stores messages)
        /// - Multiple consumers (via different queues)
        /// - Best of both worlds
        /// 
        /// OUR USE CASE:
        /// ===================================================
        /// Processing Lambda completes → SNS Topic → SQS Queue → Email Worker
        /// 
        /// Flow:
        /// 1. User uploads image
        /// 2. Processing completes
        /// 3. Lambda publishes to SNS: "Job {id} completed"
        /// 4. SNS fans out to:
        ///    - SQS queue (for email worker)
        ///    - Email subscriber (optional)
        ///    - SMS subscriber (optional)
        /// 5. Email worker polls SQS and sends email to user
        /// 
        /// WHY THIS ARCHITECTURE?
        /// ===================================================
        /// ✅ DECOUPLING: Processing Lambda doesn't know about email logic
        /// ✅ RELIABILITY: Messages stored in SQS, not lost if worker offline
        /// ✅ SCALABILITY: Multiple workers can process SQS messages
        /// ✅ FLEXIBILITY: Easy to add more notification types (SMS, Slack, etc.)
        /// 
        /// KEY CONCEPTS:
        /// ===================================================
        /// 
        /// 1. SNS TOPIC
        ///    - Named resource for pub/sub
        ///    - Publishers send messages here
        ///    - Subscribers receive messages from here
        /// 
        /// 2. SNS SUBSCRIPTION
        ///    - Connects subscriber to topic
        ///    - Protocols: Email, SMS, HTTP/S, Lambda, SQS, SMS
        ///    - Filters: Only receive messages matching conditions
        /// 
        /// 3. SQS QUEUE
        ///    - Standard Queue: High throughput, at-least-once delivery, best-effort ordering
        ///    - FIFO Queue: Exactly-once delivery, strict ordering
        ///    - We use Standard for this use case
        /// 
        /// 4. DEAD-LETTER QUEUE (DLQ)
        ///    - Stores messages that fail processing
        ///    - After maxReceiveCount attempts, message moves to DLQ
        ///    - Useful for debugging
        /// 
        /// 5. VISIBILITY TIMEOUT
        ///    - When consumer receives message, it becomes invisible
        ///    - If not deleted in timeout, becomes visible again
        ///    - Prevents duplicate processing
        /// 
        /// MESSAGE FLOW EXAMPLE:
        /// ===================================================
        /// {
        ///   "jobId": "job-123",
        ///   "status": "Completed",
        ///   "fileName": "vacation.jpg",
        ///   "userEmail": "user@example.com",
        ///   "outputUrl": "https://s3.../processed.jpg"
        /// }
        /// 
        /// COST BREAKDOWN:
        /// ===================================================
        /// SNS:
        /// - First 1,000 publishes: FREE
        /// - $0.50 per 1M publishes
        /// - Email: $2 per 100,000 emails
        /// 
        /// SQS:
        /// - First 1M requests: FREE
        /// - $0.40 per 1M requests after
        /// 
        /// Example: 10,000 jobs/month = ~$0.01/month
        /// 
        /// ===================================================
        /// </summary>
        private void CreateNotificationSystem()
        {
            // -------------------------------------------
            // CREATE SNS TOPIC FOR JOB COMPLETION
            // -------------------------------------------
            
            var jobCompletionTopic = new Topic(this, "JobCompletionTopic", new TopicProps
            {
                // TOPIC NAME
                TopicName = "MediaProcessor-JobCompletion",
                
                // DISPLAY NAME (for email/SMS subscriptions)
                DisplayName = "Media Processor Job Completion Notifications"
            });
            
            // -------------------------------------------
            // CREATE DEAD-LETTER QUEUE (DLQ)
            // -------------------------------------------
            // Stores messages that fail processing after max retries
            
            var notificationDlq = new Queue(this, "NotificationDLQ", new QueueProps
            {
                QueueName = "MediaProcessor-Notification-DLQ",
                
                // RETENTION: Keep failed messages for 14 days
                RetentionPeriod = Duration.Days(14)
            });
            
            // -------------------------------------------
            // CREATE SQS QUEUE FOR EMAIL NOTIFICATIONS
            // -------------------------------------------
            
            var emailNotificationQueue = new Queue(this, "EmailNotificationQueue", new QueueProps
            {
                QueueName = "MediaProcessor-EmailNotifications",
                
                // VISIBILITY TIMEOUT: How long message is invisible after being received
                // Should be longer than Lambda function timeout
                // If Lambda fails, message becomes visible again after this timeout
                VisibilityTimeout = Duration.Seconds(300), // 5 minutes
                
                // RETENTION PERIOD: How long messages are kept in queue
                // Messages older than this are deleted
                RetentionPeriod = Duration.Days(4),
                
                // DEAD-LETTER QUEUE: Where failed messages go
                DeadLetterQueue = new DeadLetterQueue
                {
                    Queue = notificationDlq,
                    MaxReceiveCount = 3 // After 3 failed attempts, move to DLQ
                },
                
                // RECEIVE WAIT TIME: Long polling (more efficient than short polling)
                // Consumer waits up to 20 seconds for messages instead of immediate return
                ReceiveMessageWaitTime = Duration.Seconds(20)
            });
            
            // -------------------------------------------
            // SUBSCRIBE SQS QUEUE TO SNS TOPIC
            // -------------------------------------------
            // When message published to SNS, it's automatically sent to SQS
            
            jobCompletionTopic.AddSubscription(new SqsSubscription(emailNotificationQueue, new SqsSubscriptionProps
            {
                // RAW MESSAGE DELIVERY: Send message as-is (without SNS metadata wrapper)
                RawMessageDelivery = true
            }));
            
            // -------------------------------------------
            // OPTIONAL: ADD EMAIL SUBSCRIPTION (for testing)
            // -------------------------------------------
            // Uncomment to receive emails directly (requires email confirmation)
            /*
            jobCompletionTopic.AddSubscription(new EmailSubscription("your-email@example.com"));
            */
            
            // -------------------------------------------
            // GRANT PROCESSING LAMBDA PERMISSION TO PUBLISH TO SNS
            // -------------------------------------------
            
            jobCompletionTopic.GrantPublish(ProcessingFunction);
            
            // -------------------------------------------
            // UPDATE PROCESSING LAMBDA ENVIRONMENT VARIABLES
            // -------------------------------------------
            // Add SNS topic ARN so Lambda knows where to publish
            
            ((Function)ProcessingFunction).AddEnvironment("SNS_TOPIC_ARN", jobCompletionTopic.TopicArn);
            
            // -------------------------------------------
            // CREATE LAMBDA TO PROCESS EMAIL QUEUE (Optional)
            // -------------------------------------------
            // This Lambda polls SQS and sends actual emails
            // For now, we'll just create the infrastructure
            // You can implement the email sender later
            
            /*
            var emailSenderFunction = new Function(this, "EmailSenderFunction", new FunctionProps
            {
                Runtime = Runtime.DOTNET_8,
                Handler = "LambdaHandlers::LambdaHandlers.Handlers.EmailSenderHandler::HandleEmailAsync",
                Code = Code.FromAsset("../LambdaHandlers/bin/Release/net8.0/publish"),
                Timeout = Duration.Seconds(30),
                MemorySize = 256,
                Environment = new Dictionary<string, string>
                {
                    { "SES_FROM_EMAIL", "noreply@yourdomain.com" }
                }
            });
            
            // Grant Lambda permission to read from SQS
            emailNotificationQueue.GrantConsumeMessages(emailSenderFunction);
            
            // Grant Lambda permission to send emails via SES
            emailSenderFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "ses:SendEmail", "ses:SendRawEmail" },
                Resources = new[] { "*" }
            }));
            
            // Configure Lambda to be triggered by SQS
            emailSenderFunction.AddEventSource(new SqsEventSource(emailNotificationQueue, new SqsEventSourceProps
            {
                BatchSize = 10, // Process up to 10 messages at once
                MaxBatchingWindow = Duration.Seconds(5)
            }));
            */
            
            // -------------------------------------------
            // CLOUDFORMATION OUTPUTS
            // -------------------------------------------
            
            new CfnOutput(this, "JobCompletionTopicArn", new CfnOutputProps
            {
                Value = jobCompletionTopic.TopicArn,
                Description = "SNS Topic ARN for job completion notifications",
            });
            
            new CfnOutput(this, "EmailNotificationQueueUrl", new CfnOutputProps
            {
                Value = emailNotificationQueue.QueueUrl,
                Description = "SQS Queue URL for email notifications",
            });
            
            // -------------------------------------------
            // WHAT WAS CREATED:
            // -------------------------------------------
            // ✅ SNS Topic (JobCompletion)
            // ✅ SQS Queue (EmailNotifications)
            // ✅ Dead-Letter Queue (for failed messages)
            // ✅ SNS → SQS subscription
            // ✅ IAM permissions (Lambda → SNS)
            // ✅ Environment variable in Processing Lambda
            
            // -------------------------------------------
            // HOW TO USE:
            // -------------------------------------------
            // In Processing Lambda, after job completes:
            /*
            var snsClient = new AmazonSimpleNotificationServiceClient();
            await snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN"),
                Message = JsonSerializer.Serialize(new {
                    jobId = "job-123",
                    status = "Completed",
                    fileName = "image.jpg",
                    outputUrl = "https://..."
                }),
                Subject = "Media Processing Complete"
            });
            */
            
            // -------------------------------------------
            // MONITORING:
            // -------------------------------------------
            // 1. SNS Console → Topics → View metrics (publishes, delivery rate)
            // 2. SQS Console → Queues → View metrics (messages, age)
            // 3. CloudWatch → Alarms → Set alerts on queue depth
            // 4. DLQ → Check for failed messages
            
            // -------------------------------------------
            // PRODUCTION ENHANCEMENTS:
            // -------------------------------------------
            // 1. Add email sender Lambda (using SES)
            // 2. Add SMS notifications (SNS SMS subscription)
            // 3. Add Slack/Teams webhooks (HTTP subscription)
            // 4. Add message filtering (only notify on failures)
            // 5. Add CloudWatch alarm on DLQ depth
            // 6. Implement retry logic with exponential backoff
        }
    }
}
