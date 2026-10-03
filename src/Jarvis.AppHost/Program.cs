var builder = DistributedApplication.CreateBuilder(args);
var features = JarvisFeatures.From(builder.Configuration);

// One AppHost for both environments: `aspire run` starts local development; publishing writes the production
// Docker Compose file (scripts/deploy/publish-compose.sh).
if (builder.ExecutionContext.IsPublishMode)
    builder.AddProductionDeployment(features);
else
    builder.AddLocalDevelopment(features);

builder.Build().Run();

if (builder.ExecutionContext.IsPublishMode)
    ComposeOutput.ApplyExtraSettings(args, ProductionDeployment.ExtraSettings);
