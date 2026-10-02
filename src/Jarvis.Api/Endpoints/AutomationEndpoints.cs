using Jarvis.Api.Errors;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;

namespace Jarvis.Api.Endpoints;

internal static class AutomationEndpoints
{
    public static RouteGroupBuilder MapAutomationEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        MapReminders(api, logger);
        MapWatches(api, logger);
        MapTasks(api, logger);
        MapBriefings(api);
        return api;
    }

    private static void MapReminders(RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/reminders", async (IReminderService reminders, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await reminders.ListAsync(currentUser.OwnerId, ct)).Select(reminder => reminder.ToDto())))
            .WithName("ListReminders");

        api.MapGet("/reminders/{id:guid}", async (Guid id, IReminderService reminders,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var reminder = await reminders.GetAsync(id, currentUser.OwnerId, ct);
            return reminder is null ? Results.NotFound() : Results.Ok(reminder.ToDto());
        }).WithName("GetReminder");

        api.MapPost("/reminders", async (IReminderService reminders, ICurrentUser currentUser, ReminderRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300)
                return EndpointHelpers.Invalid("title", "Title must contain 1 to 300 characters.");
            ReminderPlace? place = null;
            if (request.Place is { } placeRequest)
            {
                if (placeRequest.Latitude is not double latitude || placeRequest.Longitude is not double longitude)
                    return EndpointHelpers.Invalid("place", "A place needs a latitude and longitude.");
                if (string.IsNullOrWhiteSpace(placeRequest.Name))
                    return EndpointHelpers.Invalid("place", "A place needs a name.");
                if (request.Recurrence is { } recurrence && recurrence != Reminder.RecurrenceNone)
                    return EndpointHelpers.Invalid("recurrence", "A place reminder repeats per visit, not on a schedule.");
                place = new ReminderPlace(placeRequest.Name, latitude, longitude, placeRequest.RadiusMeters ?? 150,
                    placeRequest.Trigger ?? Reminder.LocationArrive, placeRequest.Repeats);
            }
            else if (request.DueAt is null)
            {
                return EndpointHelpers.Invalid("dueAt", "A reminder needs a time or a place.");
            }

            try
            {
                var reminder = await reminders.CreateAsync(currentUser.OwnerId, new CreateReminderRequest(
                    request.Title ?? string.Empty, request.DueAt ?? DateTimeOffset.UtcNow, request.Recurrence,
                    request.Weekdays, request.TimeZoneId, request.Until, request.LocalTime, place), ct);
                return Results.Created($"/api/v1/reminders/{reminder.Id}", reminder.ToDto());
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return EndpointHelpers.Invalid("dueAt", exception.Message);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(place is null ? "recurrence" : "place", exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Temporal could not schedule a reminder for user {OwnerId}.", currentUser.OwnerId);
                return ApiProblemResults.DependencyUnavailable("Reminder service is temporarily unavailable.");
            }
        }).WithName("CreateReminder");

        api.MapDelete("/reminders/{id:guid}", async (Guid id, IReminderService reminders, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var reminder = await reminders.CancelAsync(id, currentUser.OwnerId, ct);
            return reminder is null ? Results.NotFound() : Results.Ok(reminder.ToDto());
        }).WithName("CancelReminder");

        api.MapPost("/reminders/{id:guid}/snooze", async (Guid id, SnoozeReminderRequest request,
            IReminderService reminders, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var until = request.Until ?? (request.Minutes is int minutes and > 0 and <= 60 * 24 * 366
                ? DateTimeOffset.UtcNow.AddMinutes(minutes)
                : null);
            if (until is null) return EndpointHelpers.Invalid("minutes", "Choose how long to snooze, up to a year.");
            try
            {
                var reminder = await reminders.SnoozeAsync(id, currentUser.OwnerId, until.Value, ct);
                return reminder is null ? Results.NotFound() : Results.Ok(reminder.ToDto());
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return EndpointHelpers.Invalid("until", exception.Message);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("reminder", exception.Message);
            }
        }).WithName("SnoozeReminder");

        api.MapPost("/reminders/{id:guid}/complete", async (Guid id, IReminderService reminders,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var reminder = await reminders.MarkDoneAsync(id, currentUser.OwnerId, ct);
            return reminder is null ? Results.NotFound() : Results.Ok(reminder.ToDto());
        }).WithName("CompleteReminder");
    }

    private static void MapWatches(RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/watches", async (IConditionWatchService watches, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await watches.ListAsync(currentUser.OwnerId, ct)).Select(watch => watch.ToDto())))
            .WithName("ListConditionWatches");

        api.MapGet("/watches/{id:guid}", async (Guid id, IConditionWatchService watches,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var watch = await watches.GetAsync(id, currentUser.OwnerId, ct);
            return watch is null ? Results.NotFound() : Results.Ok(watch.ToDto());
        }).WithName("GetConditionWatch");

        api.MapPost("/watches", async (CreateConditionWatchRequest request, IConditionWatchService watches,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var watch = await watches.CreateAsync(currentUser.OwnerId, request, ct);
                return Results.Created($"/api/v1/watches/{watch.Id}", watch.ToDto());
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("watch", exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Temporal could not schedule a condition watch for user {OwnerId}.",
                    currentUser.OwnerId);
                return ApiProblemResults.DependencyUnavailable("Condition watch service is temporarily unavailable.");
            }
        }).WithName("CreateConditionWatch");

        api.MapDelete("/watches/{id:guid}", async (Guid id, IConditionWatchService watches,
                ICurrentUser currentUser, CancellationToken ct) =>
            await watches.CancelAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("CancelConditionWatch");
    }

    private static void MapTasks(RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/tasks", async (IJarvisTaskService tasks, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await tasks.ListAsync(currentUser.OwnerId, ct)).Select(task => task.ToDto())))
            .WithName("ListTasks");

        api.MapGet("/tasks/{id:guid}", async (Guid id, IJarvisTaskRepository tasks,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var task = await tasks.GetTaskAsync(id, currentUser.OwnerId, ct);
            return task is null ? Results.NotFound() : Results.Ok(task.ToDto());
        }).WithName("GetTask");

        api.MapGet("/tasks/{id:guid}/messages", async (Guid id, IJarvisTaskRepository tasks,
            IConversationStore conversations, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var task = await tasks.GetTaskAsync(id, currentUser.OwnerId, ct);
            if (task is null) return Results.NotFound();
            var messages = await conversations.GetMessagesAsync(task.ConversationId, ct);
            return Results.Ok(messages.Select(message => message.ToDto()));
        }).WithName("GetTaskMessages");

        api.MapPost("/tasks", async (CreateJarvisTaskRequest request, IJarvisTaskService tasks,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
                return EndpointHelpers.Invalid("title", "Title must contain 1 to 200 characters.");
            if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 32_000)
                return EndpointHelpers.Invalid("prompt", "Instructions must contain 1 to 32,000 characters.");
            try
            {
                var task = await tasks.CreateAsync(currentUser.OwnerId, request.Title, request.Prompt, ct,
                    request.ProfileId, request.ProjectId);
                return Results.Created($"/api/v1/tasks/{task.Id}", task.ToDto());
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid(exception.ParamName == "projectId" ? "projectId" : "profileId",
                    exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Temporal could not start a task for user {OwnerId}.", currentUser.OwnerId);
                return ApiProblemResults.DependencyUnavailable("Task service is temporarily unavailable.");
            }
        }).WithName("CreateTask");

        api.MapDelete("/tasks/{id:guid}", async (Guid id, IJarvisTaskService tasks,
                ICurrentUser currentUser, CancellationToken ct) =>
            await tasks.CancelAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("CancelTask");
    }

    private static void MapBriefings(RouteGroupBuilder api)
    {
        api.MapGet("/briefings/daily", async (IDailyBriefingService briefings, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var preference = await briefings.GetAsync(currentUser.OwnerId, ct);
            return preference is null
                ? Results.Ok(new DailyBriefingPreferenceRecord(currentUser.OwnerId, false,
                    new TimeOnly(8, 0), "UTC", string.Empty, null, null))
                : Results.Ok(preference);
        }).WithName("GetDailyBriefing");

        api.MapPut("/briefings/daily", async (SaveDailyBriefingRequest request,
            IDailyBriefingService briefings, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try { return Results.Ok(await briefings.SaveAsync(currentUser.OwnerId, request, ct)); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("timeZoneId", exception.Message); }
        }).WithName("SaveDailyBriefing");
    }
}
