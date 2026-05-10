using Amazon.CDK;

namespace Infrastructure
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App();
            
            // Main application infrastructure
            new InfrastructureStack(app, "MediaProcessorStack-JSavic", new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    // Account and Region will be inferred from AWS CLI
                },
                Description = "Serverless Media Processor - Main Application Infrastructure"
            });
            
            // CI/CD Pipeline
            new PipelineStack(app, "PipelineStack-JSavic", new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    // Account and Region will be inferred from AWS CLI
                },
                Description = "CI/CD Pipeline for Media Processor"
            });
            
            app.Synth();
        }
    }
}
