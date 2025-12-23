using Microsoft.Extensions.DependencyInjection;
using Rolling.Application.Chat.Contracts;
using Rolling.Application.Chat.Services;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.Services;

namespace Rolling.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IChatService, ChatService>();
        return services;
    }
}
