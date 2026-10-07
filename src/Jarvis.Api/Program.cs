using System.Security.Claims;
using Jarvis.Api.Endpoints;
using Jarvis.Api.Errors;
using Jarvis.Api.Hosting;
using Jarvis.Api.Realtime;
using Jarvis.Api.Security;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence;
using Jarvis.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Sentry;

if (args is ["migrate"])
{
    Environment.ExitCode = await MigrationCommand.RunAsync(args);
    return;
}

if (args is ["voice-mcp"])
{
    await Jarvis.Api.Realtime.VoiceMcpStdio.RunAsync();
    return;
}

if (args is ["mcp-runner"])
{
    await Jarvis.Api.McpRunner.McpRunnerHost.RunAsync([]);
    return;
}

JarvisSentry.ShouldCaptureException = exception => JarvisSentryExceptions.ShouldCapture(exception);

var builder = WebApplication.CreateBuilder(args);
builder.ValidateProductionConfiguration();

var accountTokens = AccountTokenOptions.From(builder.Configuration, builder.Environment.IsDevelopment());
builder.AddServiceDefaults();
builder.Services.AddJarvisAuthentication(accountTokens, builder.Environment.IsDevelopment());
builder.Services.AddJarvisApi(builder.Configuration, accountTokens);
builder.Services.AddJarvisRateLimiting(builder.Configuration);

var app = builder.Build();

ApiProblemResults.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:ApplyMigrationsAtStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JarvisDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseJarvisRateLimiting();
app.UseExceptionHandler();
app.UseJarvisApiProblemResponses();
app.UseJarvisWebClient();
app.UseCors();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var subject = context.User.FindFirstValue("sub")
        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(subject, out var ownerId))
        SentrySdk.ConfigureScope(scope => scope.User.Id = ownerId.ToString("D"));
    await next(context);
});
app.UseAuthorization();
app.MapDefaultEndpoints();
var hub = app.MapHub<JarvisEventsHub>("/hubs/events");
if (!app.Environment.IsDevelopment()) hub.RequireAuthorization();

var api = app.MapGroup("/api/v1");
if (!app.Environment.IsDevelopment()) api.RequireAuthorization();
api.MapAccountAuth();
api.MapConversationEndpoints();
api.MapApprovalEndpoints();
api.MapAutomationEndpoints(app.Logger);
api.MapOwnerAutomationEndpoints(app.Logger);
api.MapAutomationStudioEndpoints(app.Logger);
api.MapIntegrationEndpoints();
api.MapMcpCatalogEndpoints();
api.MapNotificationEndpoints();
api.MapVoiceEndpoints(app.Logger);
api.MapFileEndpoints(app.Logger);
api.MapMemoryEndpoints(app.Logger);
api.MapJournalEndpoints(app.Logger);
api.MapWeeklyReviewEndpoints();
api.MapProjectEndpoints();
api.MapExpenseEndpoints(app.Logger);
api.MapHabitEndpoints(app.Logger);
api.MapModelSettingsEndpoints(app.Logger);
api.MapSkillEndpoints(app.Logger);
api.MapPersonaEndpoints();
api.MapProfileEndpoints();
api.MapCollectionEndpoints();
api.MapLearningEndpoints(app.Logger);
api.MapUsageEndpoints();
api.MapKnowledgeGraphEndpoints();
api.MapPeopleEndpoints(app.Logger);
api.MapPeopleRadarEndpoints(app.Logger);
api.MapChannelEndpoints(app.Logger);
api.MapWhatsAppAssistantEndpoints(app.Logger);
api.MapSurfaceEndpoints();
api.MapA2AManagementEndpoints();
api.MapDeviceEndpoints();
api.MapBrowserEndpoints();
api.MapPersonalAssistantEndpoints();
api.MapSearchEndpoints();
api.MapPlannerEndpoints();
api.MapTimelineEndpoints();
api.MapInboxEndpoints(app.Logger);
api.MapFinanceEndpoints(app.Logger);
api.MapAccountEndpoints(app.Logger);
api.MapPortfolioEndpoints(app.Logger);
api.MapLibraryEndpoints(app.Logger);
api.MapModeEndpoints(app.Logger);
api.MapRoutineEndpoints(app.Logger);
api.MapImprovementEndpoints();
api.MapDecisionEndpoints(app.Logger);
api.MapMissionEndpoints(app.Logger);

app.MapA2AProtocol();

app.Run();
