# CloudPub .NET SDK

Expose local HTTP, HTTPS, TCP, UDP, and other services to the internet through [CloudPub](https://cloudpub.ru) — similar in spirit to ngrok-style tunnels, with a **control WebSocket**, **protobuf protocol**, and optional **ASP.NET Core** integration with fluent builder setup.

This repository ships two NuGet packages:

| Package | When to use |
|--------|-------------|
| **CloudPub.Client** | Console apps, workers, or any .NET Standard 2.1+ library. |
| **CloudPub.AspNet** | ASP.NET Core apps on **.NET 10+** that should auto-publish when the web host starts. |

Both packages share the root namespace **`CloudPub`** (types such as `CloudPubClient`, `CloudPub.Options.CloudPubClientOptions`).

---

## Installation

```bash
dotnet add package CloudPub.Client
```

For ASP.NET Core integration:

```bash
dotnet add package CloudPub.AspNet
```

(`CloudPub.AspNet` references `CloudPub.Client`; you do not need to add the client package separately unless you want an explicit reference.)

---

## Authentication

The agent handshake accepts either:

- **Token** — set `CloudPubClientOptions.Token` (e.g. from environment or user secrets), or  
- **Email + password** — set `Email` and `Password`.

You can also point `ServerUri` at another control plane (default is `https://cloudpub.ru`).

---

## Using the client library

### Minimal example (publish HTTP on localhost)

```csharp
using CloudPub;
using CloudPub.Options;
using CloudPub.Protocol;

CloudPubClientOptions options = new CloudPubClientOptions
{
    Email = "you@example.com",
    Password = "your-password",
    // Or: Token = "your-token",
};

await using CloudPubClient client = new CloudPubClient(options, new CloudPubRules());
await client.ConnectAsync();

Endpoint endpoint = await client.PublishAsync(new CloudPubPublishOptions
{
    Protocol = ProtocolType.Http,
    Address = "8080",              // localhost:8080 (or host:port, URL, etc.)
    Name = "my-app",
    Auth = AuthType.None,
});

Console.WriteLine($"Public URL: {endpoint.Url}");
// … keep running …
await client.UnpublishAsync(endpoint);
```

### Extension helpers (`CloudPubClientExtensions`)

After `ConnectAsync`, use the static extension methods on `ICloudPubClient`:

| Method | Purpose |
|--------|---------|
| `PublishAsync` | Register a new publication from `CloudPubPublishOptions`. |
| `UnpublishAsync` | Remove a publication and mark the endpoint offline. |
| `StopAsync` | Stop traffic without removing registration. |
| `CleanAsync` | Clear all publications on the account. |
| `ListAsync` | List current endpoints. |

### `CloudPubClientOptions` (common properties)

| Property | Description |
|----------|-------------|
| `ServerUri` | Control server (default `https://cloudpub.ru`). WebSocket URL is derived from this. |
| `Timeout` | Handshake / wait timeouts (default 30 seconds). |
| `ResumeEndpointsOnConnect` | When true, the transport may send `EndpointStartAll` after connect. |
| `Token` | Session token after login, or pre-provisioned token. |
| `Email` / `Password` | Account credentials for hello handshake. |
| `AgentId` | Stable agent id; a new GUID is used if empty. |
| `Hwid` | Optional hardware id string. |
| `ClientVersion` | Reported client version string. |

### `CloudPubPublishOptions` (publishing)

| Property | Description |
|----------|-------------|
| `Protocol` | e.g. `Http`, `Https`, `Tcp`, `Udp`, `Ssh`, … |
| `Address` | Port only, `host:port`, path, or full URL — see XML docs on `CreateCleintEndpoint`. |
| `Name` | Friendly description stored on the endpoint. |
| `Auth` | Optional; defaults (e.g. Basic for WebDAV) apply when null. |
| `Acl`, `Headers`, `Rules` | Optional access lists, headers, and filter rules. |

---

## ASP.NET Core addon (`CloudPub.AspNet`)

The addon wires **`ICloudPubClient`** into DI and uses a fluent `ICloudPubClientBuilder`.

Startup and shutdown responsibilities are split:

1. **`HostedCloudPubLifetimeService`**: connect/publish on startup, unpublish on shutdown.
2. **`HostedCloudPubLocalhostProxyService`**: localhost address projection into `IServerAddressesFeature`.
3. **`HostedCloudPubPipelineProxyService`**: HTTP request forwarding directly into ASP.NET pipeline.

### 1. Register client + options

Pick one style.

**Option A — bind from configuration (recommended)**

`appsettings.json`:

```json
{
  "CloudPub": {
    "ServerUri": "https://cloudpub.ru",
    "Email": "you@example.com",
    "Password": "your-password"
  }
}
```

Program:

```csharp
using CloudPub;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudPub(builder.Configuration.GetSection("CloudPub"));
```

**Option B — inline options**

```csharp
builder.Services.AddCloudPub(new CloudPub.Options.CloudPubClientOptions
{
    Token = builder.Configuration["CloudPub:Token"]!,
});
```

**Option C — custom factory**

```csharp
builder.Services.AddCloudPub(sp =>
{
    CloudPub.Options.CloudPubClientOptions o = sp.GetRequiredService<IOptions<CloudPub.Options.CloudPubClientOptions>>().Value;
    return new CloudPubClient(o, sp.GetRequiredService<ICloudPubRules>());
});
```

### 2. Declare endpoints and proxy mode

Use the fluent builder returned by `AddCloudPub(...)`:

```csharp
builder.Services
    .AddCloudPub(builder.Configuration.GetSection("CloudPub"))
    .AddEndpoint(5000, "My API")
    .AddEndpoint(new CloudPub.Options.CloudPubPublishOptions
    {
        Protocol = CloudPub.Protocol.ProtocolType.Https,
        Address = "5001",
        Name = "Secure API",
    })
    .WithLocalhostProxy();
```

`WithLocalhostProxy()` and `WithPipelineProxy()` are mutually exclusive.

### 3. Add Post\Pre connection actions

Use WebApplication extension method `OnCloudPubStarted` and `OnCloudPubStopped`

```csharp
WebApplication app = builder.Build();
app.OnCloudPubStarted(ctx =>
{
    Endpoint endpoint = ctx.PublishedEndpoints.FirstOrDefault()
        ?? throw new InvalidOperationException("No endpoints found");

    telegramBotClient.RemapWebhook(endpoint.Url + "telegram/");
});

app.Run();
```

### Full minimal `Program.cs` sketch

```csharp
using CloudPub;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddCloudPub(builder.Configuration.GetSection("CloudPub"))
    .AddEndpoint(5000, "Sample site")
    .WithPipelineProxy();

WebApplication app = builder.Build();
app.MapGet("/", () => "Hello from CloudPub!");
app.Run();
```

**Requirements:** a server that exposes **`IServerAddressesFeature`** (Kestrel does). The hosted service throws at startup if the feature is missing.

---

## Sample console project

The solution includes **`CloudPub.Example`**: a CLI-style app with argument parsing and commands:

- `list`
- `publish --address <addr> --protocol <proto> [--name <text>] [--auth <mode>] [--wait]`
- `publish-http <port> [--wait]` (shortcut)
- `stop --guid <guid>`
- `unpublish --guid <guid>`
- `clean`

It reads auth settings from `CLOUDPUB_*` variables or explicit CLI options (`--token`, `--email`, `--password`, `--server`).

Examples:

```bash
set CLOUDPUB_EMAIL=you@example.com
set CLOUDPUB_PASSWORD=secret
dotnet run --project src/CloudPub.Example/CloudPub.Example.csproj -- list
dotnet run --project src/CloudPub.Example/CloudPub.Example.csproj -- publish --protocol tcp --address 127.0.0.1:25565 --name "Minecraft"
dotnet run --project src/CloudPub.Example/CloudPub.Example.csproj -- publish-http 8080 --wait
```

(Use `CLOUDPUB_TOKEN` instead of email/password if you prefer.)

---

## Building from source

```bash
git clone <repository-url>
cd CloudPub.Client
dotnet build CloudPub.Sdk.slnx -c Release
```

Packages are emitted under `bin/Release/` when `GeneratePackageOnBuild` is enabled.

---

## Repository and license

- **Repository:** see the package metadata (e.g. `RepositoryUrl` in the `.csproj` files).  
- **License:** see the `LICENSE` file bundled with the package.

For API details, open the XML documentation shipped with each package or browse the `///` comments in the source.
