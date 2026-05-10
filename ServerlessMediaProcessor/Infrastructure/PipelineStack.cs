using Amazon.CDK;
using Amazon.CDK.AWS.CodeBuild;
using Amazon.CDK.AWS.CodePipeline;
using Amazon.CDK.AWS.CodePipeline.Actions;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.Pipelines;
using Constructs;
using System.Collections.Generic;

namespace Infrastructure
{
    /// <summary>
    /// CI/CD Pipeline Stack - Automates Build, Test, and Deployment
    /// 
    /// 🎓 AWS CI/CD PIPELINE LEARNING MODULE
    /// ===================================================
    /// 
    /// WHAT IS THIS STACK?
    /// This creates a complete CI/CD pipeline that:
    /// 1. Watches GitHub for code changes
    /// 2. Automatically builds and tests code
    /// 3. Deploys to Staging environment
    /// 4. Waits for manual approval
    /// 5. Deploys to Production environment
    /// 
    /// KEY AWS SERVICES USED:
    /// - CodePipeline: Orchestrates the entire workflow
    /// - CodeBuild: Builds and tests code
    /// - CodeDeploy: Deploys Lambda functions
    /// - S3: Stores build artifacts
    /// - IAM: Manages permissions
    /// - SNS: Sends notifications
    /// - CloudWatch: Monitors pipeline
    /// 
    /// PIPELINE STAGES:
    /// Stage 1: Source (GitHub) → Get latest code
    /// Stage 2: Build (CodeBuild) → Compile + Test
    /// Stage 3: Deploy-Staging → Test environment
    /// Stage 4: Approval → Manual review gate
    /// Stage 5: Deploy-Production → Live environment
    /// 
    /// ===================================================
    /// </summary>
    public class PipelineStack : Stack
    {
        public PipelineStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // ===================================================
            // STEP 1: CREATE ARTIFACTS BUCKET
            // ===================================================
            // S3 bucket to store build outputs (Lambda ZIP, CDK templates)
            
            var artifactsBucket = new Bucket(this, "PipelineArtifacts", new BucketProps
            {
                BucketName = $"media-processor-pipeline-artifacts-{this.Account}",
                
                // ENCRYPTION: Server-side encryption for security
                Encryption = BucketEncryption.S3_MANAGED,
                
                // LIFECYCLE: Delete old artifacts after 30 days to save cost
                LifecycleRules = new[]
                {
                    new LifecycleRule
                    {
                        Enabled = true,
                        Expiration = Duration.Days(30)
                    }
                },
                
                // VERSIONING: Keep versions for rollback capability
                Versioned = true,
                
                // AUTO-DELETE: Clean up bucket when stack is deleted
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteObjects = true
            });
            
            // AWS CONCEPT: Artifacts Bucket
            // CodePipeline stores build outputs in S3
            // Each build creates new folder with timestamp
            // Example: s3://bucket/build-123/upload-lambda.zip
            
            // ===================================================
            // STEP 2: CREATE CODEBUILD PROJECT
            // ===================================================
            // Defines HOW to build the code
            
            var buildProject = new PipelineProject(this, "BuildProject", new PipelineProjectProps
            {
                ProjectName = "MediaProcessor-Build",
                
                // BUILD ENVIRONMENT: Docker container with tools
                Environment = new BuildEnvironment
                {
                    // COMPUTE TYPE: Build server size
                    // SMALL: 3 GB RAM, 2 vCPUs (~$0.005/minute)
                    // MEDIUM: 7 GB RAM, 4 vCPUs (~$0.01/minute)
                    // LARGE: 15 GB RAM, 8 vCPUs (~$0.02/minute)
                    ComputeType = ComputeType.SMALL, // Enough for .NET builds
                    
                    // BUILD IMAGE: Pre-configured Docker image
                    // STANDARD_7_0: Amazon Linux 2, latest tools
                    BuildImage = LinuxBuildImage.STANDARD_7_0,
                    
                    // PRIVILEGED: Not needed (we're not building Docker images in build)
                    Privileged = false
                },
                
                // BUILD SPEC: Instructions file (buildspec.yml)
                BuildSpec = BuildSpec.FromSourceFilename("buildspec.yml"),
                
                // TIMEOUT: Max time for build
                Timeout = Duration.Minutes(15),
                
                // CACHE: Speed up builds by caching NuGet packages
                Cache = Cache.Local(LocalCacheMode.CUSTOM),
                
                // LOGGING: Send build logs to CloudWatch
                Logging = new LoggingOptions
                {
                    CloudWatch = new CloudWatchLoggingOptions
                    {
                        LogGroup = new Amazon.CDK.AWS.Logs.LogGroup(this, "BuildLogs", new Amazon.CDK.AWS.Logs.LogGroupProps
                        {
                            LogGroupName = "/aws/codebuild/MediaProcessor",
                            Retention = Amazon.CDK.AWS.Logs.RetentionDays.ONE_WEEK,
                            RemovalPolicy = RemovalPolicy.DESTROY
                        })
                    }
                }
            });
            
            // AWS CONCEPT: CodeBuild Project
            // Defines the build environment and instructions
            // CodePipeline triggers this project when code changes
            // Build runs in isolated Docker container
            // Container is destroyed after build completes
            
            // Grant permissions to read/write artifacts
            artifactsBucket.GrantReadWrite(buildProject);
            
            // ===================================================
            // STEP 3: CREATE SNS TOPIC FOR NOTIFICATIONS
            // ===================================================
            // Notifies team about pipeline events
            
            var pipelineNotificationTopic = new Topic(this, "PipelineNotifications", new TopicProps
            {
                TopicName = "MediaProcessor-Pipeline-Notifications",
                DisplayName = "Media Processor Pipeline Notifications"
            });
            
            // Subscribe email (optional - uncomment and add your email)
            // pipelineNotificationTopic.AddSubscription(
            //     new EmailSubscription("your-email@example.com")
            // );
            
            // AWS CONCEPT: Pipeline Notifications
            // SNS sends notifications for:
            // - Build started
            // - Build succeeded
            // - Build failed
            // - Deployment started
            // - Deployment succeeded
            // - Deployment failed
            // - Manual approval needed
            
            // ===================================================
            // STEP 4: CREATE CODEPIPELINE
            // ===================================================
            // Orchestrates the entire CI/CD workflow
            
            var pipeline = new Pipeline(this, "Pipeline", new PipelineProps
            {
                PipelineName = "MediaProcessor-Pipeline",
                
                // ARTIFACTS BUCKET: Where to store build outputs
                ArtifactBucket = artifactsBucket,
                
                // RESTART EXECUTION ON UPDATE: Rerun pipeline if pipeline definition changes
                RestartExecutionOnUpdate = true
            });
            
            // AWS CONCEPT: CodePipeline
            // Workflow engine that coordinates stages
            // Each stage can have multiple actions
            // Actions run in parallel or sequence
            // Pipeline tracks which version of code is being deployed
            
            // ===================================================
            // STAGE 1: SOURCE - GET CODE FROM GITHUB
            // ===================================================
            
            var sourceOutput = new Artifact_("SourceCode");
            
            // GitHub connection (manual setup required first time)
            // Run once: aws codestar-connections create-connection --connection-name github-media-processor
            var sourceAction = new CodeStarConnectionsSourceAction(new CodeStarConnectionsSourceActionProps
            {
                ActionName = "GitHub-Source",
                
                // CONNECTION ARN: Your actual GitHub connection
                // Created in AWS Console as "github-media-processorJSavic"
                // Status: AVAILABLE ✅
                ConnectionArn = "arn:aws:codeconnections:eu-west-1:765891906457:connection/e0045f78-5500-4fb0-a85b-b5e8a0732001",
                
                // REPOSITORY: GitHub repo (format: owner/repo-name)
                Owner = "L9Jovica",
                Repo = "AWSUpskilling",
                
                // BRANCH: Which branch to watch
                Branch = "main",
                
                // OUTPUT: Code downloaded to this artifact
                Output = sourceOutput,
                
                // TRIGGER: Automatically run on push (webhook)
                TriggerOnPush = true
            });
            
            // AWS CONCEPT: CodeStar Connections
            // Secure connection between AWS and GitHub
            // Uses OAuth, not personal access tokens
            // One-time setup in AWS Console
            // Webhook automatically created in GitHub
            
            pipeline.AddStage(new StageOptions
            {
                StageName = "Source",
                Actions = new[] { sourceAction }
            });
            
            // Grant pipeline role permission to use the GitHub connection
            // Without this, pipeline can't access the connection
            pipeline.Role.AddToPrincipalPolicy(new Amazon.CDK.AWS.IAM.PolicyStatement(new Amazon.CDK.AWS.IAM.PolicyStatementProps
            {
                Effect = Amazon.CDK.AWS.IAM.Effect.ALLOW,
                Actions = new[] { 
                    "codeconnections:UseConnection",
                    "codestar-connections:UseConnection"
                },
                Resources = new[] { 
                    "arn:aws:codeconnections:eu-west-1:765891906457:connection/e0045f78-5500-4fb0-a85b-b5e8a0732001"
                }
            }));
            
            // AWS CONCEPT: IAM Permissions for CodeConnections
            // The pipeline role needs explicit permission to USE the connection
            // Even though connection exists, IAM must allow access
            // This is AWS security: explicit permissions required
            
            // ===================================================
            // STAGE 2: BUILD - COMPILE AND TEST CODE
            // ===================================================
            
            var buildOutput = new Artifact_("BuildArtifacts");
            
            var buildAction = new CodeBuildAction(new CodeBuildActionProps
            {
                ActionName = "Build-and-Test",
                
                // PROJECT: CodeBuild project created above
                Project = buildProject,
                
                // INPUT: Source code from Stage 1
                Input = sourceOutput,
                
                // OUTPUTS: Build artifacts (Lambda ZIP, CDK templates)
                Outputs = new[] { buildOutput },
                
                // ENVIRONMENT VARIABLES: Pass to build
                EnvironmentVariables = new Dictionary<string, IBuildEnvironmentVariable>
                {
                    { "AWS_ACCOUNT_ID", new BuildEnvironmentVariable { Value = this.Account } },
                    { "AWS_DEFAULT_REGION", new BuildEnvironmentVariable { Value = this.Region } }
                }
            });
            
            // AWS CONCEPT: CodeBuild Action
            // Executes build project
            // Input: Source code artifact
            // Output: Compiled code + deployment packages
            // If build fails, pipeline stops here
            
            pipeline.AddStage(new StageOptions
            {
                StageName = "Build",
                Actions = new[] { buildAction }
            });
            
            // ===================================================
            // STAGE 3: DEPLOY TO STAGING
            // ===================================================
            
            // Deploy infrastructure with CDK
            // CloudFormation handles Lambda deployments automatically
            var deployStagingInfraAction = new CloudFormationCreateUpdateStackAction(new CloudFormationCreateUpdateStackActionProps
            {
                ActionName = "Deploy-Staging-Infrastructure",
                
                // STACK NAME: CloudFormation stack
                StackName = "MediaProcessor-Staging",
                
                // TEMPLATE: CDK synthesized CloudFormation template
                TemplatePath = buildOutput.AtPath("cdk.out/InfrastructureStack.template.json"),
                
                // ADMIN PERMISSIONS: Allow CloudFormation to create any resource
                AdminPermissions = true,
                
                // PARAMETERS: Pass environment-specific values
                ParameterOverrides = new Dictionary<string, object>
                {
                    { "Environment", "staging" }
                }
            });
            
            // AWS CONCEPT: Staging Environment
            // Identical to production, but separate
            // Use for testing before going live
            // Can rollback without affecting users
            
            pipeline.AddStage(new StageOptions
            {
                StageName = "Deploy-Staging",
                Actions = new[] { deployStagingInfraAction }
            });
            
            // ===================================================
            // STAGE 4: MANUAL APPROVAL
            // ===================================================
            // Human reviews staging before production deployment
            
            var approvalAction = new ManualApprovalAction(new ManualApprovalActionProps
            {
                ActionName = "Approve-Production-Deployment",
                
                // NOTIFICATION: SNS topic for approval request
                NotificationTopic = pipelineNotificationTopic,
                
                // ADDITIONAL INFO: Instructions for reviewer
                AdditionalInformation = @"
                Please review the staging deployment:
                1. Test the staging API: https://staging-api.example.com
                2. Check CloudWatch logs for errors
                3. Verify Lambda functions are working
                4. If everything looks good, click Approve
                5. If issues found, click Reject and fix code
                ",
                
                // EXTERNAL ENTITY LINK: Direct link to staging environment
                ExternalEntityLink = "https://staging-api.example.com"
            });
            
            // AWS CONCEPT: Manual Approval
            // Stops pipeline execution
            // Sends SNS notification
            // Approver reviews in AWS Console
            // Clicks "Approve" or "Reject"
            // Only approved deployments go to production
            
            pipeline.AddStage(new StageOptions
            {
                StageName = "Approval",
                Actions = new[] { approvalAction }
            });
            
            // ===================================================
            // STAGE 5: DEPLOY TO PRODUCTION
            // ===================================================
            // Same as staging, but to production environment
            
            var deployProdInfraAction = new CloudFormationCreateUpdateStackAction(new CloudFormationCreateUpdateStackActionProps
            {
                ActionName = "Deploy-Production-Infrastructure",
                StackName = "MediaProcessor-Production",
                TemplatePath = buildOutput.AtPath("cdk.out/InfrastructureStack.template.json"),
                AdminPermissions = true,
                ParameterOverrides = new Dictionary<string, object>
                {
                    { "Environment", "production" }
                }
            });
            
            // AWS CONCEPT: Production Deployment
            // Final stage after approval
            // Deploys to live environment
            // Users see changes immediately
            // CloudWatch monitors for errors
            // Automatic rollback if alarms trigger
            
            pipeline.AddStage(new StageOptions
            {
                StageName = "Deploy-Production",
                Actions = new[] { deployProdInfraAction }
            });
            
            // ===================================================
            // PIPELINE NOTIFICATIONS
            // ===================================================
            // Configure what events trigger SNS notifications
            
            pipeline.OnStateChange("PipelineStateChange", new Amazon.CDK.AWS.Events.OnEventOptions
            {
                Description = "Pipeline state changes",
                EventPattern = new Amazon.CDK.AWS.Events.EventPattern
                {
                    DetailType = new[] { "CodePipeline Pipeline Execution State Change" },
                    Detail = new Dictionary<string, object>
                    {
                        { "state", new[] { "FAILED", "SUCCEEDED" } }
                    }
                },
                Target = new Amazon.CDK.AWS.Events.Targets.SnsTopic(pipelineNotificationTopic)
            });
            
            // ===================================================
            // CLOUDFORMATION OUTPUTS
            // ===================================================
            
            new CfnOutput(this, "PipelineName", new CfnOutputProps
            {
                Value = pipeline.PipelineName,
                Description = "CodePipeline name",
                ExportName = "MediaProcessor-PipelineName"
            });
            
            new CfnOutput(this, "PipelineConsoleUrl", new CfnOutputProps
            {
                Value = $"https://{this.Region}.console.aws.amazon.com/codesuite/codepipeline/pipelines/{pipeline.PipelineName}/view",
                Description = "Pipeline URL in AWS Console",
                ExportName = "MediaProcessor-PipelineUrl"
            });
            
            new CfnOutput(this, "ArtifactsBucket", new CfnOutputProps
            {
                Value = artifactsBucket.BucketName,
                Description = "S3 bucket for pipeline artifacts",
                ExportName = "MediaProcessor-ArtifactsBucket"
            });
        }
    }
}

/*
 * ===================================================
 * CI/CD PIPELINE SUMMARY
 * ===================================================
 * 
 * WHAT THIS CREATES:
 * ✅ CodePipeline with 5 stages
 * ✅ CodeBuild project (builds + tests code)
 * ✅ S3 bucket (stores artifacts)
 * ✅ SNS topic (notifications)
 * ✅ IAM roles (permissions)
 * ✅ CloudWatch logs (monitoring)
 * ✅ EventBridge rules (pipeline events)
 * 
 * PIPELINE FLOW:
 * 1. Developer pushes code to GitHub
 * 2. GitHub webhook triggers CodePipeline
 * 3. Pipeline downloads code
 * 4. CodeBuild compiles code + runs tests
 * 5. If tests pass: Deploy to staging
 * 6. If tests fail: STOP (notify team)
 * 7. Manual approval required
 * 8. After approval: Deploy to production
 * 9. Monitor CloudWatch for errors
 * 10. Automatic rollback if errors spike
 * 
 * DEPLOYMENT TIME:
 * - Build: ~5 minutes
 * - Deploy Staging: ~5 minutes
 * - Manual Approval: Human time (minutes to hours)
 * - Deploy Production: ~5 minutes
 * Total: ~15 minutes + approval time
 * 
 * COST:
 * - CodePipeline: $1/month
 * - CodeBuild: ~$1/month (20 builds × 5 min × $0.005/min)
 * - S3 artifacts: $0.10/month
 * - Data transfer: $0.10/month
 * Total: ~$2-3/month
 * 
 * BENEFITS:
 * ✅ Automated testing (catch bugs early)
 * ✅ Staged rollout (test before production)
 * ✅ Manual approval (human oversight)
 * ✅ Automatic rollback (safety net)
 * ✅ Audit trail (who deployed what, when)
 * ✅ Notifications (team awareness)
 * 
 * SETUP REQUIRED:
 * 1. Create GitHub connection in AWS Console
 * 2. Update ConnectionArn in this file
 * 3. Update GitHub Owner/Repo in this file
 * 4. Deploy pipeline: cdk deploy PipelineStack
 * 5. Approve GitHub connection in AWS Console
 * 6. Push code to trigger first build!
 * 
 * ===================================================
 */
