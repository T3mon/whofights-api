using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WhoFights.Data;
using WhoFights.Email;
using WhoFights.Notifications;
using WhoFights.Notifications.Channels;

// One-shot, like WhoFights.Sync: work out what's due, deliver it, exit.
// Render's Cron Job scheduler runs this every half hour - often enough
// that "1 hour before the fight" reminders land on time once they exist,
// and cheap because the process only lives for the seconds it needs.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

var notificationOptions = builder.Configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>()
    ?? throw new InvalidOperationException("Notifications configuration section is missing.");
builder.Services.AddSingleton(Options.Create(notificationOptions));

// Channels: one registration per delivery route. Telegram and push go here
// when they arrive; NotificationRules decides which kinds each may carry.
builder.Services.AddResendEmail(builder.Configuration);
builder.Services.AddScoped<INotificationChannel, EmailChannel>();
builder.Services.AddScoped<NotificationRunner>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

try
{
    await scope.ServiceProvider.GetRequiredService<NotificationRunner>().RunAsync();
    return 0;
}
catch (Exception ex)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogError(ex, "Notification run failed");
    return 1;
}
