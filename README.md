# NotifyHub

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

A universal .NET library for push notifications: browser (Web Push/VAPID), Apple (APNs),
Android (FCM), generic webhooks, and email (SMTP) - all through a single, channel-agnostic API.

Send one notification to a user, and NotifyHub delivers it over every channel that user is
subscribed to, at the same time. A user with a registered browser AND an iPhone gets the message
on both, in parallel, from a single method call.

**Not a standalone server.** NotifyHub is a referenceable library, not a hosted service: the
application that references it configures it via constructor/DI, decides how to host it, and owns
its own subscription storage. There is no NotifyHub daemon, no required database, and no vendor
lock-in.

## Table of contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Project structure](#project-structure)
- [Quickstart](#quickstart)
- [Core concepts](#core-concepts)
- [Channel reference](#channel-reference)
  - [Web Push (VAPID)](#web-push-vapid)
  - [Apple Push Notifications (APNs)](#apple-push-notifications-apns)
  - [Firebase Cloud Messaging (FCM)](#firebase-cloud-messaging-fcm)
  - [Webhook](#webhook)
  - [Email (SMTP)](#email-smtp)
- [Custom storage](#custom-storage)
- [HTTP endpoints (`NotifyHub.AspNetCore`)](#http-endpoints-notifyhubaspnetcore)
- [Handling send results](#handling-send-results)
- [Running the demo](#running-the-demo)
- [Testing on a real iPhone](#testing-on-a-real-iphone)
- [Running the test suite](#running-the-test-suite)
- [Known limitations](#known-limitations)
- [Contributing](#contributing)
- [License](#license)

## Features

| Channel | Config required | Notes |
|---|---|---|
| **Web Push (VAPID)** | None | Always on. Key pair generated and persisted automatically on first use. RFC 8291/8292-compliant, works with Chrome, Firefox, and Safari/iOS. |
| **APNs** (native iOS) | p8 key, key ID, team ID, bundle ID | Token-based auth (JWT/ES256), cached and auto-renewed. |
| **FCM** (Android) | Firebase service account JSON, project ID | HTTP v1 API, OAuth2 via self-signed JWT - no Firebase Admin SDK dependency. |
| **Webhook** | None | Always on. POSTs the notification as JSON to any URL - Slack, Discord, Home Assistant, n8n, your own endpoint. |
| **Email (SMTP)** | Host, from-address, optionally credentials | Via MailKit; supports STARTTLS (587/25) and implicit TLS (465). |

Every channel implements the same interface and returns the same result type, so host apps write
one code path regardless of how many channels are in play. A channel that isn't configured is
simply skipped (`SendOutcome.Skipped`) - no exceptions, no special-casing required by the caller.

## Requirements

- .NET 10.0 or later
- ASP.NET Core (only for the optional `NotifyHub.AspNetCore` HTTP endpoints add-on)

## Installation

**Not yet published to NuGet.org.** Until the first release goes out, reference the projects
directly from a checkout of this repo:

```xml
<ItemGroup>
  <ProjectReference Include="..\path\to\NotifyHub\src\NotifyHub\NotifyHub.csproj" />
  <ProjectReference Include="..\path\to\NotifyHub\src\NotifyHub.AspNetCore\NotifyHub.AspNetCore.csproj" />
</ItemGroup>
```

(`NotifyHub.AspNetCore` is optional - only needed if you want the ready-made subscribe/unsubscribe/send
HTTP endpoints instead of calling `NotificationSender` in-process.)

Once published, the packages will be installable the normal way:

```
dotnet add package NotifyHub
dotnet add package NotifyHub.AspNetCore
```

## Project structure

- `src/NotifyHub` - the core library. `NotificationSender`, the five channels
  (`Channels/WebPushChannel`, `ApnsChannel`, `FcmChannel`, `WebhookChannel`, `EmailChannel`),
  option types (`Options/ApnsOptions`, etc.), `VapidKeyProvider` (generates the VAPID key pair
  automatically on first startup - never configured manually).
- `src/NotifyHub.AspNetCore` - optional add-on: ready-to-use minimal API endpoints
  (subscribe/unsubscribe/send) for host apps that want HTTP endpoints instead of pure in-process
  calls.
- `samples/NotifyHub.Demo` - a runnable reference sample with a browser demo page (Web Push
  end-to-end, click-through).
- `tests/NotifyHub.Tests` - xUnit; every external channel is tested against a fake
  `HttpMessageHandler` (no real Apple/Google/SMTP account needed), plus a standalone
  cryptographic round-trip test for the Web Push implementation.

## Quickstart

The minimal setup - just Web Push, no external accounts needed:

```csharp
using NotifyHub;

builder.Services.AddNotifyHub(hub => hub
    .WithVapidSubject("mailto:push@yourapp.com")); // see the iOS/Safari note below
```

```csharp
var sender = app.Services.GetRequiredService<NotificationSender>();
var results = await sender.SendAsync(
    new NotificationMessage { Title = "New message", Body = "You've got mail!", Url = "/inbox" },
    subscriptions); // your own list of subscriptions, loaded from wherever you store them
```

A full setup with every channel configured:

```csharp
using NotifyHub;
using NotifyHub.Options;

builder.Services.AddNotifyHub(hub => hub
    .WithVapidSubject("mailto:push@yourapp.com")
    .WithApns(new ApnsOptions
    {
        KeyPath = "AuthKey_ABC123.p8",
        KeyId = "ABC123",
        TeamId = "TEAMID1234",
        BundleId = "com.yourcompany.yourapp",
    })
    .WithFcm(FcmOptions.FromFile("firebase-service-account.json", "your-firebase-project"))
    .WithSmtp(new SmtpOptions
    {
        Host = "smtp.example.com",
        Port = 587,
        User = "postmaster@example.com",
        Password = "...",
        FromAddress = "noreply@yourapp.com",
    }));
```

A channel without a matching `With...` call is simply disabled (`SendOutcome.Skipped`) - WebPush
needs no configuration and is always active.

> **iOS/Safari note:** Apple's web push service validates the VAPID `sub` claim and rejects
> subjects on non-existent domains (e.g. `.local`, `localhost`) with `403 BadJwtToken`. Chrome and
> Firefox don't perform this check, so the problem only shows up when testing on an iPhone. Always
> pass a real, resolvable domain to `WithVapidSubject`.

## Core concepts

| Type | Purpose |
|---|---|
| `Subscription` | One delivery target for exactly one channel (a browser's push endpoint, a device token, a webhook URL, or an email address). Created via `Subscription.WebPush(...)`, `.Apns(...)`, `.Fcm(...)`, `.Webhook(...)`, or `.Email(...)`. |
| `NotificationMessage` | Channel-independent content: `Title`, `Body`, optional `Url`, `Data` dictionary, `Badge` (APNs badge count), `Sound` (APNs custom sound), `Silent` (background/data-only push - APNs/FCM/WebPush), `ImageUrl` (FCM/WebPush/Webhook). Every field beyond `Title`/`Body` is optional and only used by the channels that understand it. |
| `NotificationSender` | The single entry point. `SendAsync(message, subscriptions, channels?, maxConcurrency?)` fans out to every subscription's channel in parallel and returns one `ChannelSendResult` per subscription. Which users/subscriptions are targeted is entirely up to what you pass in; the optional `channels` allow-list restricts delivery to specific channel types (e.g. WebPush only) without having to filter the subscription list yourself; the optional `maxConcurrency` caps how many sends run at once (useful for very large broadcasts - see below). |
| `ChannelSendResult` / `SendOutcome` | Per-subscription outcome: `Delivered`, `Expired`, `Failed`, or `Skipped`. See [Handling send results](#handling-send-results). |

NotifyHub never persists subscriptions itself - the host app owns that list completely and passes
the currently relevant subset into `SendAsync` on every call. The only thing NotifyHub persists on
its own is the Web Push VAPID key pair (see [Custom storage](#custom-storage)).

**Large broadcasts:** `SendAsync` fans out with unbounded parallelism by default (unchanged,
zero-config behavior) - fine for small/medium subscriber counts. Sending to tens of thousands of
subscriptions at once can exhaust the local HTTP connection pool and trip provider-side rate
limits (APNs/FCM throttle aggressively), which would show up as spurious `SendOutcome.Failed`
results. Pass `maxConcurrency` to cap how many sends are in flight simultaneously:

```csharp
await sender.SendAsync(message, allSubscriptions, maxConcurrency: 200);
```

## Channel reference

### Web Push (VAPID)

Always active, no configuration required beyond `WithVapidSubject(...)`. The VAPID key pair is
generated once, automatically, on first use, and persisted (default: a JSON file - see
[Custom storage](#custom-storage)). Implemented from scratch against RFC 8291 (message encryption)
and RFC 8292 (VAPID JWT/`Authorization` header) - no third-party Web Push library - so it works
correctly against Chrome, Firefox, and Apple's stricter Safari/iOS implementation alike.

```csharp
Subscription.WebPush(endpoint, p256dh, auth); // values come from the browser's PushSubscription
```

### Apple Push Notifications (APNs)

Native iOS/macOS app push via token-based authentication (a JWT signed with a p8 key - Apple's
recommended approach; one key covers every app on the team). This is a different mechanism from
Web Push: it requires a device token issued to a native app, not a browser subscription.

```csharp
hub.WithApns(new ApnsOptions
{
    KeyPath = "AuthKey_ABC123.p8", // downloaded once from the Apple Developer portal
    KeyId = "ABC123",
    TeamId = "TEAMID1234",
    BundleId = "com.yourcompany.yourapp",
    UseSandbox = false, // true for development-signed builds
});

Subscription.Apns(deviceToken);
```

Without `ApnsOptions`, the channel is a silent no-op. Expired/uninstalled tokens are reported as
`SendOutcome.Expired` (HTTP 410, `BadDeviceToken`, `DeviceTokenNotForTopic`).

`NotificationMessage.Data`/`Url` are sent as top-level keys alongside `"aps"` (Apple's convention
for custom payload data), `Badge` maps to `aps.badge`, `Sound` overrides the default notification
sound, and `Silent: true` sends a background push (`aps.content-available: 1`, no `alert`/`sound`,
`apns-push-type: background`, `apns-priority: 5`) for silent data sync instead of a visible alert.

### Firebase Cloud Messaging (FCM)

Android (and any platform reachable through Firebase) push via the FCM HTTP v1 API, authenticated
with a self-signed JWT built from your Firebase service account - no Firebase Admin SDK
dependency required.

```csharp
hub.WithFcm(FcmOptions.FromFile("firebase-service-account.json", "your-firebase-project"));
// or, if you load the JSON yourself (secret store, env var, ...):
hub.WithFcm(new FcmOptions { ServiceAccountJson = json, ProjectId = "your-firebase-project" });

Subscription.Fcm(deviceToken);
```

Without `FcmOptions`, the channel is a silent no-op. `UNREGISTERED`/`NOT_FOUND` responses are
reported as `SendOutcome.Expired`.

`NotificationMessage.ImageUrl` maps to `notification.image`. `Silent: true` sends a data-only
message (the `notification` key is omitted entirely - only `Data` is delivered), for background
sync without a visible notification. `Badge`/`Sound` are APNs-specific and not applicable here.

### Webhook

Always active, no configuration required. POSTs the notification to any URL - useful for Slack,
Discord, Home Assistant, n8n, or your own service.

```csharp
Subscription.Webhook("https://your-service.example.com/hooks/notify");
```

By default the body is NotifyHub's own generic shape (`{ title, body, url, data, image, badge, sound, silent }`),
which Home Assistant/n8n/your own endpoints can read directly. **Slack and Discord expect their own shape and
reject the generic one** - pass `format` to match the target:

```csharp
Subscription.Webhook("https://hooks.slack.com/services/...", format: WebhookPayloadFormat.Slack);   // { text }
Subscription.Webhook("https://discord.com/api/webhooks/...", format: WebhookPayloadFormat.Discord); // { content }
```

Two more opt-in parameters, both off by default:

```csharp
Subscription.Webhook(
    "https://your-service.example.com/hooks/notify",
    secret: "shared-secret",                                     // adds X-NotifyHub-Signature: sha256=<hmac>
    headers: new Dictionary<string, string> { ["Authorization"] = "Bearer ..." });
```

- `secret` signs the raw request body with HMAC-SHA256 and adds it as an
  `X-NotifyHub-Signature: sha256=<hex>` header, so the receiver can verify the call really came
  from NotifyHub and wasn't tampered with.
- `headers` sends arbitrary extra HTTP headers with every request - e.g. an `Authorization` token
  required by a custom endpoint.

A 404/410 response is reported as `SendOutcome.Expired`, so the host app can clean up dead
endpoints the same way it cleans up expired push tokens.


### Email (SMTP)

A universal fallback channel. Uses [MailKit](https://github.com/jstedfast/MailKit) rather than
`System.Net.Mail.SmtpClient`, because the latter does not reliably support implicit TLS (port
465) - MailKit correctly handles both STARTTLS (typically port 587 or 25) and SSL-on-connect
(typically port 465) automatically.

```csharp
hub.WithSmtp(new SmtpOptions
{
    Host = "smtp.example.com",
    Port = 587,       // 587/25 = STARTTLS, 465 = implicit TLS - both work
    User = "postmaster@example.com",
    Password = "...",
    FromAddress = "noreply@yourapp.com",
    FromName = "Your App",
    UseSsl = true,
});

Subscription.Email("recipient@example.com");
```

Without `SmtpOptions`, the channel is a silent no-op. Two things worth knowing when pointing this
at a real mail server:

- **Sender authorization:** some servers reject a `FromAddress` that isn't the authenticated
  account or an explicitly allowed alias (`5.5.4 not allowed to send from this address`). Use the
  authenticated mailbox as `FromAddress`, or configure a send-as alias on the server.
- **No expiry detection:** unlike push tokens, email addresses don't fail synchronously - a
  permanently invalid address only shows up later via an asynchronous bounce, outside this send
  call. `SendOutcome.Expired` is therefore never returned for email.

## Custom storage

NotifyHub stores nothing on its own except the Web Push VAPID key pair. The default is
`FileVapidKeyStore`, a JSON file in the working directory - stable across restarts, so existing
browser subscriptions keep working (a purely in-memory store would generate a new key pair on
every restart and silently break every subscription created before it). For your own storage
(e.g. an existing DB), implement `IVapidKeyStore` and wire it up:

```csharp
public interface IVapidKeyStore
{
    Task<VapidKeys?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(VapidKeys keys, CancellationToken ct = default);
}
```

```csharp
services.AddNotifyHub(hub => hub.WithVapidKeyStore(new MyDbBackedVapidKeyStore(dbContext)));
```

Subscriptions themselves (who gets what) are managed entirely by the host app -
`NotificationSender` simply receives them as a list on every `SendAsync` call. If you use the
`NotifyHub.AspNetCore` add-on, see [`ISubscriptionStore`](#http-endpoints-notifyhubaspnetcore)
below for the equivalent extension point on that side.

## HTTP endpoints (`NotifyHub.AspNetCore`)

If you'd rather expose HTTP endpoints than call `NotificationSender` in-process, add the
`NotifyHub.AspNetCore` package:

```csharp
services.AddNotifyHub(hub => hub.WithVapidSubject("mailto:push@yourapp.com"));
services.AddNotifyHubEndpoints(); // default: InMemorySubscriptionStore (does not survive a restart)

app.MapNotifyHubEndpoints(); // mounted at /notifyhub by default
```

| Method | Route | Body | Description |
|---|---|---|---|
| `GET` | `/notifyhub/vapid-public-key` | - | Returns `{ publicKey }` - pass this to `PushManager.subscribe()` as the `applicationServerKey`. |
| `POST` | `/notifyhub/subscriptions` | `{ userId, channel, ... }` | Registers or updates a subscription for a user. `channel` is the numeric `NotificationChannel` value (0=WebPush, 1=Apns, 2=Fcm, 3=Webhook, 4=Email); the remaining fields depend on the channel (`endpoint`/`p256dh`/`auth`, `deviceToken`, `url`, or `emailAddress`). |
| `DELETE` | `/notifyhub/subscriptions/{id}` | - | Removes a subscription by its server-assigned ID. |
| `GET` | `/notifyhub/subscriptions?userId=...` | - | Lists a user's subscriptions (ID, channel, creation time). |
| `POST` | `/notifyhub/notifications/send` | `{ userId?, userIds?, broadcast, title, body, url?, data?, channels?, badge?, sound?, silent?, imageUrl?, maxConcurrency? }` | Sends to one user's subscriptions, a specific list of users' (`userIds`), or to everyone if `broadcast` is true. Optional `channels` (e.g. `[0]` for WebPush only) restricts delivery to just those channel types - omit it to send across every channel the target(s) are subscribed to, as before. `badge`/`sound`/`silent`/`imageUrl` map to the matching `NotificationMessage` fields (see [Core concepts](#core-concepts)); `maxConcurrency` caps parallel sends for large broadcasts. Automatically deletes subscriptions that come back `Expired`. |

Change the route prefix with `app.MapNotifyHubEndpoints("/my-prefix")`.

For production use, implement `ISubscriptionStore` against your own DB and wire it up:

```csharp
services.AddNotifyHubEndpoints(endpoints => endpoints.WithSubscriptionStore(new MyDbSubscriptionStore(dbContext)));
```

### Securing the endpoints

By default, none of the mapped endpoints require authentication - just like `AddNotifyHub(...)`
itself, this works out of the box with zero required configuration. Since
`MapNotifyHubEndpoints()` returns its own `RouteGroupBuilder`, requiring auth on every one of
these endpoints (without affecting the rest of your app's routes) is a single extra call:

```csharp
app.MapNotifyHubEndpoints().RequireAuthorization();
```

This needs your app to have authentication/authorization configured as usual
(`AddAuthentication()`/`AddAuthorization()` + `UseAuthentication()`/`UseAuthorization()`). Any
other route group convention (`RequireCors(...)`, `RequireRateLimiting(...)`, etc.) can be chained
the same way. Without it, anyone who can reach these endpoints can subscribe/unsubscribe any user
ID and trigger sends (including broadcast) - fine for local prototyping, but you should add
`RequireAuthorization()` (or equivalent) before exposing this to the internet.

## Handling send results

`SendAsync` returns one `ChannelSendResult` per subscription, in the same order:

```csharp
var results = await sender.SendAsync(message, subscriptions);

foreach (var (subscription, result) in subscriptions.Zip(results))
{
    switch (result.Outcome)
    {
        case SendOutcome.Delivered:
            break; // nothing to do
        case SendOutcome.Expired:
            await myStore.DeleteAsync(subscription.Id); // provider says this target is gone for good
            break;
        case SendOutcome.Failed:
            logger.LogWarning("Notification failed for {Id}: {Error}", subscription.Id, result.Error);
            break; // transient - the subscription is still valid, consider a retry
        case SendOutcome.Skipped:
            break; // channel not configured - expected, not an error
    }
}
```

## Running the demo

```
dotnet run --project samples/NotifyHub.Demo
```

Then open `http://localhost:5000` (or whichever URL is printed), click "Subscribe to push"
(grant the browser permission), send a test message - the notification appears as a native
browser notification. The same page also has a form to subscribe an email address, if you've
configured `Vapid:Subject`/`Smtp:*` via configuration (see comments in
`samples/NotifyHub.Demo/Program.cs`).

The page also has a Webhook form, prefilled with the demo's own built-in `/demo/webhook-sink`
endpoint - subscribe, send a test message, and the received call shows up live in the "Webhook
log" panel at the bottom of the page. No external account or tool (webhook.site, ngrok, ...)
needed to try out the Webhook channel end-to-end.

## Testing on a real iPhone

Safari's Web Push implementation has two hard requirements that plain `localhost` testing can't
satisfy:

1. The page must be served over a **trusted HTTPS** connection (a self-signed cert or a plain LAN
   HTTP URL is not enough).
2. The page must be **added to the home screen** and opened from that icon - subscribing from a
   normal Safari tab fails with "push is not supported by this browser".

For local development, the fastest way to get a trusted HTTPS URL pointing at your machine is a
tunnel such as [ngrok](https://ngrok.com):

```
ngrok http 5080
```

Open the resulting `https://...ngrok-free.dev` URL on the iPhone, add it to the home screen via
Safari's share sheet, then open it from the home screen icon before subscribing.

## Running the test suite

```
dotnet test
```

Every external channel (WebPush, APNs, FCM, webhook) is tested against a fake
`HttpMessageHandler` - no real Apple/Google/SMTP account is ever needed to run the tests. The Web
Push implementation additionally has a standalone cryptographic test that independently rebuilds
the RFC 8291 decryption path (without reusing any NotifyHub code) to verify the encrypted payload
round-trips correctly, and that the VAPID JWT signature verifies against its own public key.

## Known limitations

- APNs/FCM/SMTP are only tested against fakes in this repository (no real provider accounts
  available in this development environment). The channels are built to the same pattern as a
  production-proven APNs sender and will work without any code changes once real credentials (p8
  key, Firebase service account, SMTP credentials) are supplied. Web Push has been verified
  end-to-end against real Chrome and Safari/iOS clients.
- `InMemorySubscriptionStore` (the default for `NotifyHub.AspNetCore`) does not survive a
  restart - it's meant for trying things out only; implement `ISubscriptionStore` for production.
- The VAPID key file store (`FileVapidKeyStore`) assumes a single instance/shared file system;
  for multi-instance deployments, implement `IVapidKeyStore` against a shared store instead.

## Contributing

Issues and pull requests are welcome. Please run `dotnet test` before submitting a change, and
keep new channels/behavior covered by tests against a fake `HttpMessageHandler` rather than a
live provider account.

## License

MIT - see [LICENSE](LICENSE).
