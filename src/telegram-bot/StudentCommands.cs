using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotKit;
using TelegramBotKit.Commands;
using TelegramBotKit.DependencyInjection;
using TelegramBotKit.Fallbacks;
using TelegramBotKit.Messaging;
using TelegramBotKit.Middleware;

public static class StudentCommands
{
    public static IServiceCollection AddStudentCommands(this IServiceCollection services)
    {
        services.AddScoped<StudentConversation>();
        // Explicit registrations are provided by TelegramBotKit; no separate text router.
        services.AddMessageCommand<StartCommand>("/start");
        services.AddMessageCommand<CancelCommand>("/cancel");
        services.AddMessageCommand<CancelCommand>("/help");
        services.AddMessageCommand<ProfileCommand>("/profile");
        services.AddMessageCommand<DailyCommand>("/daily");
        services.AddMessageCommand<TasksCommand>("/tasks");
        services.AddTextCommand<ProfileCommand>(["Профиль"], false);
        services.AddTextCommand<DailyCommand>(["Задания на сегодня", "Продолжить урок"], false);
        services.AddTextCommand<TasksCommand>(["Виды заданий"], false);
        services.AddCallbackCommand<NavigationCallback>("nav");
        services.AddCallbackCommand<KindCallback>("kind");
        services.AddCallbackCommand<ReviewCallback>("review");
        services.AddCallbackCommand<ExerciseAnswerCallback>("answer");
        services.AddCallbackCommand<ReviewAnswerCallback>("vote");
        services.AddScoped<IDefaultMessageHandler>(sp => sp.GetRequiredService<StudentConversation>());
        services.AddScoped<IDefaultCallbackHandler, UnknownCallback>();
        return services;
    }
}

public sealed class StartCommand(StudentConversation conversation) : IMessageCommand
{ public Task HandleAsync(Message message, BotContext ctx) => conversation.StartAsync(message, ctx); }
public sealed class CancelCommand(StudentConversation conversation) : IMessageCommand
{ public Task HandleAsync(Message message, BotContext ctx) => conversation.MenuAsync(ctx); }
public sealed class ProfileCommand(StudentConversation conversation) : IMessageCommand, ITextCommand
{ public Task HandleAsync(Message message, BotContext ctx) => conversation.ProfileAsync(ctx); }
public sealed class DailyCommand(StudentConversation conversation) : IMessageCommand, ITextCommand
{ public Task HandleAsync(Message message, BotContext ctx) => conversation.DailyAsync(ctx); }
public sealed class TasksCommand(StudentConversation conversation) : IMessageCommand, ITextCommand
{ public Task HandleAsync(Message message, BotContext ctx) => conversation.TasksAsync(ctx); }
public sealed class NavigationCallback(StudentConversation conversation) : ICallbackCommand
{ public Task HandleAsync(CallbackQuery query, string[] args, BotContext ctx) => conversation.NavigateAsync(query, args, ctx); }
public sealed class KindCallback(StudentConversation conversation) : ICallbackCommand
{ public Task HandleAsync(CallbackQuery query, string[] args, BotContext ctx) => conversation.IssueAsync(query, args, ctx); }
public sealed class ReviewCallback(StudentConversation conversation) : ICallbackCommand
{ public Task HandleAsync(CallbackQuery query, string[] args, BotContext ctx) => conversation.ReviewAsync(query, args, ctx); }
public sealed class ExerciseAnswerCallback(StudentConversation conversation) : ICallbackCommand
{ public Task HandleAsync(CallbackQuery query, string[] args, BotContext ctx) => conversation.ExerciseAnswerAsync(query, args, ctx); }
public sealed class ReviewAnswerCallback(StudentConversation conversation) : ICallbackCommand
{ public Task HandleAsync(CallbackQuery query, string[] args, BotContext ctx) => conversation.ReviewAnswerAsync(query, args, ctx); }
public sealed class UnknownCallback : IDefaultCallbackHandler
{
    public Task HandleAsync(CallbackQuery query, BotContext ctx) => ctx.Sender.AnswerCallback(query.Id,
        new AnswerCallback { Text = "Кнопка устарела. Откройте /tasks." }, ctx.CancellationToken);
}

public sealed class PrivateStudentChat : IUpdateMiddleware
{
    public Task InvokeAsync(BotContext ctx, BotContextDelegate next)
    {
        var update = ctx.Update;
        var allowed = update.Message is { Chat.Type: ChatType.Private, From.IsBot: false } ||
            update.CallbackQuery is { Message.Chat.Type: ChatType.Private, From.IsBot: false } callback && callback.From.Id == callback.Message.Chat.Id;
        return allowed ? next(ctx) : Task.CompletedTask;
    }
}
