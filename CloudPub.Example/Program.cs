using CloudPub.Options;
using Protocol;

namespace CloudPub.Example;

internal static class Program
{
    private static void PrintHelp()
    {
        Console.WriteLine("""
            CloudPub C# SDK — example client

            Environment (optional):
              CLOUDPUB_SERVER_URI   Default: https://cloudpub.ru
              CLOUDPUB_EMAIL        Account email
              CLOUDPUB_PASSWORD     Account password
              CLOUDPUB_TOKEN        Auth token (if set, email/password not required)

            Usage:
              cloudpub-example list
                  Connect and print active publications.

              cloudpub-example ping
                  Measure control-channel / tunnel round-trip (µs, approximate).

              cloudpub-example publish-http <port>
                  Publish HTTP to localhost:<port> (e.g. 8080). Ctrl+C to exit, then unpublish.

              cloudpub-example help
                  Show this text.

            Example:
              set CLOUDPUB_EMAIL=you@example.com
              set CLOUDPUB_PASSWORD=secret
              dotnet run --project sdk/csharp/example/CloudPub.Example.csproj -- list
            """);
    }

    public static async Task<int> Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        if (cmd is "help" or "-h" or "--help")
        {
            PrintHelp();
            return cmd == "help" ? 0 : 1;
        }

        string? serverUri = Environment.GetEnvironmentVariable("CLOUDPUB_SERVER_URI");
        string? email = Environment.GetEnvironmentVariable("CLOUDPUB_EMAIL");
        string? password = Environment.GetEnvironmentVariable("CLOUDPUB_PASSWORD");
        string? token = Environment.GetEnvironmentVariable("CLOUDPUB_TOKEN");

        CloudPubClientOptions clientOptions = new CloudPubClientOptions();

        if (!string.IsNullOrWhiteSpace(serverUri))
        {
            clientOptions.ServerUri = new Uri(serverUri);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            clientOptions.Token = token;
        }
        else if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            clientOptions.Email = email;
            clientOptions.Password = password;
        }
        else
        {
            Console.Error.WriteLine("Set CLOUDPUB_TOKEN or both CLOUDPUB_EMAIL and CLOUDPUB_PASSWORD.");
            return 1;
        }

        await using CloudPubClient client = new CloudPubClient(clientOptions);
        await client.ConnectAsync();

        switch (cmd)
        {
            /*
            case "list":
            {
                var services = await conn.ListAsync();
                if (services.Count == 0)
                {
                    Console.WriteLine("No publications.");
                    return 0;
                }
                
                foreach (var s in services)
                {
                    Console.WriteLine($"{s.Guid}  {s.Status ?? "?"}  {s.Url}  {s.Name ?? ""}");
                }

                return 0;
            }

            case "ping":
            {
                var us = await conn.PingAsync();
                Console.WriteLine($"Approx. median RTT: {us} µs");
                return 0;
            }
            */

            case "publish-http":
            {
                if (args.Length < 2 || !int.TryParse(args[1], out var port) || port is < 1 or > 65535)
                {
                    Console.Error.WriteLine("Usage: publish-http <port>");
                    return 1;
                }

                Console.WriteLine($"Publishing HTTP localhost:{port} ...");
                Endpoint endpoint = await client.PublishAsync(new CloudPubPublishOptions()
                {
                    Protocol = ProtocolType.Http,
                    Address = port.ToString(),
                    Name = "cloudpub-example",
                    Auth = AuthType.None
                });
                    
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                Console.WriteLine($"GUID: {endpoint.Guid}");
                Console.WriteLine($"URL:  {endpoint.Url}");
                Console.WriteLine("Press Ctrl+C to unpublish and exit.");

                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // ctrl+c
                }

                Console.WriteLine("Unpublishing...");
                await client.UnpublishAsync(endpoint);
                Console.WriteLine("Done.");
                return 0;
            }

            default:
            {
                Console.Error.WriteLine($"Unknown command: {cmd}");
                PrintHelp();
                return 1;
            }
        }
    }
}
