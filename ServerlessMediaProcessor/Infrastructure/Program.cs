using Amazon.CDK;

namespace Infrastructure
{
    /// <summary>
    /// CDK App Entry Point
    /// This class creates and configures the CDK application and its stacks.
    /// 
    /// 🎓 AWS CDK APPLICATION CONCEPTS:
    /// ================================
    /// An "App" can contain multiple "Stacks"
    /// Each Stack = One CloudFormation stack in AWS
    /// 
    /// OUR STACKS:
    /// 1. InfrastructureStack - Main application infrastructure
    /// 2. PipelineStack - CI/CD pipeline (optional, deploy separately)
    /// 
    /// WHY SEPARATE STACKS?
    /// - Pipeline stack is deployed once, rarely changes
    /// - Infrastructure stack changes frequently (new features)
    /// - Pipeline deploys infrastructure automatically
    /// ================================
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App();
            
            // ==========================================
            // STACK 1: APPLICATION INFRASTRUCTURE
            // ==========================================
            // Contains: Lambda, API Gateway, DynamoDB, S3, VPC, ECS, etc.
            // Deployed by: CI/CD Pipeline (automated)
            // Or manually: cdk deploy MediaProcessorStack-JSavic
            
            new InfrastructureStack(app, "MediaProcessorStack-JSavic", new StackProps
            {
                // Env specifies the AWS account and region for deployment
                // If not specified, CDK uses the environment from AWS CLI configuration
                Env = new Amazon.CDK.Environment
                {
                    // Account and Region will be inferred from AWS CLI configuration
                    // This makes the stack environment-agnostic
                },
                
                // Description appears in CloudFormation console
                Description = "Serverless Media Processor - Main Application Infrastructure"
            });
            
            // ==========================================
            // STACK 2: CI/CD PIPELINE (OPTIONAL)
            // ==========================================
            // Contains: CodePipeline, CodeBuild, S3 artifacts, SNS notifications
            // Deployed by: Manual deployment (one-time setup)
            // Deploy with: cdk deploy PipelineStack-JSavic
            //
            // WHEN TO DEPLOY:
            // - Deploy this ONCE when you want automated CI/CD
            // - Pipeline will then deploy InfrastructureStack automatically
            // - No need to deploy if you prefer manual deployments
            //
            // COMMENTED OUT BY DEFAULT:
            // Uncomment the lines below when you're ready to set up CI/CD
            
            // ✅ ENABLED: Pipeline stack is now active!
            new PipelineStack(app, "PipelineStack-JSavic", new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    // Account and Region will be inferred from AWS CLI
                },
                Description = "CI/CD Pipeline for Media Processor"
            });
            
            // 🎓 AWS CONCEPT: Why commented out?
            // Pipeline stack requires GitHub connection setup first
            // Deploy infrastructure stack first manually
            // Then set up GitHub connection
            // Then uncomment and deploy pipeline stack
            // Pipeline will handle future infrastructure deployments
            
            // ==========================================
            // SYNTHESIZE CLOUDFORMATION TEMPLATES
            // ==========================================
            // This converts the C# CDK code to CloudFormation JSON
            // Output goes to cdk.out/ directory
            
            app.Synth();
            
            // 🎓 AWS CONCEPT: What happens during Synth?
            // 1. CDK validates all constructs
            // 2. Generates CloudFormation templates
            // 3. Creates asset files (Lambda code, Docker images)
            // 4. Writes everything to cdk.out/ directory
            // 5. Ready for deployment with 'cdk deploy'
        }
    }
}

/*
 * ===================================================
 * DEPLOYMENT INSTRUCTIONS
 * ===================================================
 * 
 * OPTION 1: MANUAL DEPLOYMENT (Good for learning)
 * ------------------------------------------------
 * 1. Deploy infrastructure:
 *    > cdk deploy MediaProcessorStack-JSavic
 * 
 * 2. Make code changes, redeploy:
 *    > cdk deploy MediaProcessorStack-JSavic
 * 
 * 
 * OPTION 2: CI/CD PIPELINE (Production-ready)
 * --------------------------------------------
 * 1. Deploy infrastructure manually first time:
 *    > cdk deploy MediaProcessorStack-JSavic
 * 
 * 2. Set up GitHub connection (see CICD-SETUP-GUIDE.md)
 * 
 * 3. Uncomment PipelineStack code above
 * 
 * 4. Deploy pipeline:
 *    > cdk deploy PipelineStack-JSavic
 * 
 * 5. Push code to GitHub:
 *    > git push origin main
 * 
 * 6. Pipeline automatically:
 *    - Builds code
 *    - Runs tests
 *    - Deploys to staging
 *    - Waits for approval
 *    - Deploys to production
 * 
 * 7. Future deployments:
 *    - Just push to GitHub!
 *    - Pipeline handles everything
 * 
 * ===================================================
 * WHICH OPTION TO CHOOSE?
 * ===================================================
 * 
 * Choose MANUAL if:
 * ✅ Learning AWS (want to see each step)
 * ✅ Small project (few deployments)
 * ✅ Solo developer (no team coordination)
 * 
 * Choose CI/CD if:
 * ✅ Production application
 * ✅ Team of developers
 * ✅ Frequent deployments
 * ✅ Want automated testing
 * ✅ Need deployment audit trail
 * 
 * ===================================================
 */
