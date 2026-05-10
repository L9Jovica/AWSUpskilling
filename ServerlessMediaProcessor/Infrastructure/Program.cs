using Amazon.CDK;

namespace Infrastructure
{
    /// <summary>
    /// TEMPORARY: Minimal CDK App for deploying ONLY PipelineStack
    /// This avoids Docker bundling issues with InfrastructureStack
    /// Once pipeline is deployed, restore Program.cs.backup
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App();
            
            // ONLY PipelineStack - No Docker needed!
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
