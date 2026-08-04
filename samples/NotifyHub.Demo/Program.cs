using NotifyHub;
using NotifyHub.AspNetCore;
using NotifyHub.Options;

var builder = WebApplication.CreateBuilder(args);

// Reference example: the demo app configures NotifyHub entirely through its own program code -
// no requirement for any particular config source. APNs/FCM are left unconfigured here (no test
// account available) - their channels therefore become silent no-ops automatically, while
// WebPush works right away with no credentials at all.
//
// SMTP comes optionally from User Secrets/environment variables (the "Smtp" section), so real
// credentials never end up in the repo:
//   dotnet user-secrets set "Smtp:Host" "mail.example.com" --project samples/NotifyHub.Demo
//   dotnet user-secrets set "Smtp:Port" "587" --project samples/NotifyHub.Demo
//   dotnet user-secrets set "Smtp:User" "postmaster@example.com" --project samples/NotifyHub.Demo
//   dotnet user-secrets set "Smtp:Password" "..." --project samples/NotifyHub.Demo
//   dotnet user-secrets set "Smtp:FromAddress" "noreply@example.com" --project samples/NotifyHub.Demo
var smtpSection = builder.Configuration.GetSection("Smtp");
var smtpOptions = smtpSection["Host"] is { Length: > 0 }
    ? new SmtpOptions
    {
        Host = smtpSection["Host"]!,
        Port = smtpSection.GetValue("Port", 587),
        User = smtpSection["User"],
        Password = smtpSection["Password"],
        FromAddress = smtpSection["FromAddress"] ?? smtpSection["User"] ?? "noreply@notifyhub.local",
        FromName = smtpSection["FromName"],
        UseSsl = smtpSection.GetValue("UseSsl", true),
    }
    : null;

// Apple's web push service rejects VAPID subjects on non-existent domains (e.g. ".local",
// "localhost") with "403 BadJwtToken" - Chrome/Firefox don't validate this, so the problem only
// surfaces during iPhone testing. Hence a real domain via config instead of a placeholder here;
// override: dotnet user-secrets set "Vapid:Subject" "mailto:you@yourdomain.tld" --project samples/NotifyHub.Demo
var vapidSubject = builder.Configuration["Vapid:Subject"] ?? "mailto:demo@notifyhub.dev";

builder.Services.AddNotifyHub(hub =>
{
    hub.WithVapidSubject(vapidSubject);
    if (smtpOptions is not null)
        hub.WithSmtp(smtpOptions);
});
builder.Services.AddNotifyHubEndpoints();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapNotifyHubEndpoints();

app.Run();
