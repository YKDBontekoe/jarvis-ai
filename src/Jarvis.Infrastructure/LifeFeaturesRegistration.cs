using Jarvis.Application.Automations;
using Jarvis.Application.Decisions;
using Jarvis.Application.Expenses;
using Jarvis.Application.Finance;
using Jarvis.Application.Inbox;
using Jarvis.Application.Library;
using Jarvis.Application.Missions;
using Jarvis.Application.Modes;
using Jarvis.Application.Routines;
using Jarvis.Infrastructure.Library;
using Jarvis.Application.Timeline;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Infrastructure;

/// <summary>Registers the timeline, inbox, finance, studio, library, modes, and mission services.</summary>
public static class LifeFeaturesRegistration
{
    public static IServiceCollection AddJarvisLifeFeatures(this IServiceCollection services)
    {
        // Timeline: each source reads one area; the service merges them.
        services.AddScoped<ITimelineSource, JournalTimelineSource>();
        services.AddScoped<ITimelineSource, ExpenseTimelineSource>();
        services.AddScoped<ITimelineSource, HabitTimelineSource>();
        services.AddScoped<ITimelineSource, PeopleTimelineSource>();
        services.AddScoped<ITimelineSource, TaskTimelineSource>();
        services.AddScoped<ITimelineSource, ReminderTimelineSource>();
        services.AddScoped<ITimelineSource, MemoryTimelineSource>();
        services.AddScoped<ITimelineSource, ConversationTimelineSource>();
        services.AddScoped<ITimelineSource, DecisionTimelineSource>();
        services.AddScoped<ITimelineService, TimelineService>();

        // Inbox and commitments ledger.
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<IInboxService, InboxService>();
        services.AddScoped<ICommitmentService, CommitmentService>();

        // Finance autopilot: budgets, subscriptions, forecast, bank import.
        services.AddScoped<IFinanceRepository, FinanceRepository>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<ISubscriptionNegotiationService, SubscriptionNegotiationService>();
        services.AddScoped<IExpenseObserver, BudgetExpenseObserver>();

        // Accounts, transaction ledger, and the stock portfolio. Quotes need Finance:Quotes:ApiKey; without it
        // prices are the ones the owner types in.
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddScoped<IWealthService, WealthService>();
        services.AddHttpClient<Jarvis.Infrastructure.Finance.FinnhubQuoteProvider>(client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IQuoteProvider>(sp =>
        {
            var finnhub = sp.GetRequiredService<Jarvis.Infrastructure.Finance.FinnhubQuoteProvider>();
            return finnhub.IsConfigured ? finnhub : new NullQuoteProvider();
        });

        // Automation studio: webhooks that start event automations.
        services.AddScoped<IAutomationWebhookRepository, AutomationWebhookRepository>();
        services.AddScoped<IAutomationWebhookService, AutomationWebhookService>();

        // Second brain: clipped pages, notes, reports, flashcards, deep research.
        services.AddScoped<ILibraryRepository, LibraryRepository>();
        services.AddScoped<IWebPageFetcher, PublicWebPageFetcher>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IResearchService, ResearchService>();

        // Context modes: stored in the owner's settings, so no tables of their own.
        services.AddScoped<IModeService, ModeService>();

        // Decision journal: predictions with a confidence, resolved later and scored for calibration.
        services.AddScoped<IDecisionRepository, DecisionRepository>();
        services.AddScoped<IDecisionService, DecisionService>();

        // Routine miner: repeated behaviour turned into suggested automations.
        services.AddScoped<IRoutineSuggestionRepository, RoutineSuggestionRepository>();
        services.AddScoped<IRoutineSuggestionService, RoutineSuggestionService>();

        // Mission control: a supervised crew of tasks working on one goal.
        services.AddScoped<IMissionRepository, MissionRepository>();
        services.AddScoped<IMissionService, MissionService>();
        return services;
    }
}
