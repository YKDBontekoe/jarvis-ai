using Jarvis.Api.Endpoints;
using Jarvis.Api.Errors;
using Jarvis.Api.Hosting;
using Jarvis.Api.Realtime;
using Jarvis.Api.Security;
using Jarvis.Infrastructure.Identity;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

var builder = WebApplication.CreateBuilder(args);
builder.ValidateProductionConfiguration();

var accountTokens = AccountTokenOptions.From(builder.Configuration, builder.Environment.IsDevelopment());
builder.AddServiceDefaults();
builder.Services.AddJarvisAuthentication(accountTokens, builder.Environment.IsDevelopment());
builder.Services.AddJarvisApi(builder.Configuration, accountTokens);

var app = builder.Build();

ApiProblemResults.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:ApplyMigrationsAtStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JarvisDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseExceptionHandler();
app.UseJarvisApiProblemResponses();
app.UseCors();
app.UseAuthentication();
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
api.MapIntegrationEndpoints();
api.MapNotificationEndpoints();
api.MapVoiceEndpoints(app.Logger);
api.MapFileEndpoints(app.Logger);
api.MapMemoryEndpoints(app.Logger);
api.MapJournalEndpoints(app.Logger);
api.MapModelSettingsEndpoints(app.Logger);
api.MapSkillEndpoints(app.Logger);
api.MapPersonaEndpoints();
api.MapProfileEndpoints();
api.MapCollectionEndpoints();
api.MapLearningEndpoints(app.Logger);
api.MapUsageEndpoints();
api.MapKnowledgeGraphEndpoints();
api.MapPeopleEndpoints(app.Logger);
api.MapChannelEndpoints(app.Logger);
api.MapSurfaceEndpoints();
api.MapA2AManagementEndpoints();
api.MapDeviceEndpoints();
api.MapBrowserEndpoints();
api.MapPersonalAssistantEndpoints();
api.MapSearchEndpoints();

app.MapA2AProtocol();

app.Run();
