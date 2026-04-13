// The MIT License (MIT)
// 
// CloudPub.Client
// Copyright 2026 © Rikitav Tim4ik
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the “Software”), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using CloudPub.Options;
using CloudPub.Protocol;

namespace CloudPub.Example;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] commandArgs = args.Skip(1).ToArray();

        try
        {
            CliOptions cli = ParseOptions(commandArgs);
            CloudPubClientOptions clientOptions = BuildClientOptions(cli);
            await using CloudPubClient client = new CloudPubClient(clientOptions, new CloudPubRules());

            await client.ConnectAsync().ConfigureAwait(false);
            return await ExecuteCommandAsync(client, command, cli).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> ExecuteCommandAsync(CloudPubClient client, string command, CliOptions options)
    {
        switch (command)
        {
            case "list":
                return await HandleListAsync(client).ConfigureAwait(false);

            case "publish":
                return await HandlePublishAsync(client, options).ConfigureAwait(false);

            case "publish-http":
                return await HandleLegacyPublishHttpAsync(client, options).ConfigureAwait(false);

            case "unpublish":
                return await HandleUnpublishAsync(client, options).ConfigureAwait(false);

            case "stop":
                return await HandleStopAsync(client, options).ConfigureAwait(false);

            case "clean":
                await client.CleanAsync().ConfigureAwait(false);
                Console.WriteLine("All published endpoints were removed.");
                return 0;

            case "help":
                PrintHelp();
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintHelp();
                return 1;
        }
    }

    private static async Task<int> HandleListAsync(CloudPubClient client)
    {
        IReadOnlyList<Endpoint> endpoints = [.. (await client.ListAsync().ConfigureAwait(false))];
        if (endpoints.Count == 0)
        {
            Console.WriteLine("No publications.");
            return 0;
        }

        Console.WriteLine("GUID                                 STATUS    PROTOCOL   URL");
        foreach (Endpoint ep in endpoints)
            Console.WriteLine($"{ep.Guid,-36} {(ep.Status ?? "-"),-8} {ep.Protocol,-9} {ep.Url}");

        return 0;
    }

    private static async Task<int> HandlePublishAsync(CloudPubClient client, CliOptions options)
    {
        if (!options.Values.TryGetValue("address", out string? address) || string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("publish requires --address <value>.");

        ProtocolType protocol = ProtocolType.Http;
        if (options.Values.TryGetValue("protocol", out string? protocolRaw)
            && !Enum.TryParse(protocolRaw, true, out protocol))
            throw new ArgumentException($"Invalid protocol '{protocolRaw}'.");

        AuthType auth = AuthType.None;
        if (options.Values.TryGetValue("auth", out string? authRaw)
            && !Enum.TryParse(authRaw, true, out auth))
            throw new ArgumentException($"Invalid auth '{authRaw}'.");

        CloudPubPublishOptions publishOptions = new CloudPubPublishOptions
        {
            Protocol = protocol,
            Address = address,
            Name = options.Values.GetValueOrDefault("name") ?? string.Empty,
            Auth = auth
        };

        Endpoint endpoint = await client.PublishAsync(publishOptions).ConfigureAwait(false);
        Console.WriteLine($"Published: {endpoint.Url}");
        Console.WriteLine($"GUID: {endpoint.Guid}");

        if (options.Flags.Contains("wait"))
            await WaitAndUnpublishAsync(client, endpoint).ConfigureAwait(false);

        return 0;
    }

    private static async Task<int> HandleLegacyPublishHttpAsync(CloudPubClient client, CliOptions options)
    {
        if (options.Positionals.Count == 0 || !ushort.TryParse(options.Positionals[0], out ushort port))
            throw new ArgumentException("Usage: publish-http <port>");

        options.Values["protocol"] = ProtocolType.Http.ToString();
        options.Values["address"] = port.ToString();
        options.Values.TryAdd("name", "cloudpub-example");
        return await HandlePublishAsync(client, options).ConfigureAwait(false);
    }

    private static async Task<int> HandleUnpublishAsync(CloudPubClient client, CliOptions options)
    {
        string guid = GetRequired(options, "guid");
        Endpoint endpoint = await ResolveEndpointAsync(client, guid).ConfigureAwait(false);

        await client.UnpublishAsync(endpoint).ConfigureAwait(false);
        Console.WriteLine($"Unpublished: {endpoint.Url}");
        return 0;
    }

    private static async Task<int> HandleStopAsync(CloudPubClient client, CliOptions options)
    {
        string guid = GetRequired(options, "guid");
        Endpoint endpoint = await ResolveEndpointAsync(client, guid).ConfigureAwait(false);

        await client.StopAsync(endpoint).ConfigureAwait(false);
        Console.WriteLine($"Stopped: {endpoint.Url}");
        return 0;
    }

    private static async Task WaitAndUnpublishAsync(CloudPubClient client, Endpoint endpoint)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("Press Ctrl+C to unpublish and exit.");
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { }

        await client.UnpublishAsync(endpoint).ConfigureAwait(false);
        Console.WriteLine("Unpublished and exited.");
    }

    private static async Task<Endpoint> ResolveEndpointAsync(CloudPubClient client, string guid)
    {
        Endpoint? endpoint = (await client.ListAsync().ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.Guid, guid, StringComparison.OrdinalIgnoreCase));

        return endpoint ?? throw new ArgumentException($"Endpoint with guid '{guid}' was not found.");
    }

    private static CloudPubClientOptions BuildClientOptions(CliOptions options)
    {
        string? serverUri = options.Values.GetValueOrDefault("server")   ?? Environment.GetEnvironmentVariable("CLOUDPUB_SERVER_URI");
        string? email =     options.Values.GetValueOrDefault("email")    ?? Environment.GetEnvironmentVariable("CLOUDPUB_EMAIL");
        string? password =  options.Values.GetValueOrDefault("password") ?? Environment.GetEnvironmentVariable("CLOUDPUB_PASSWORD");
        string? token =     options.Values.GetValueOrDefault("token")    ?? Environment.GetEnvironmentVariable("CLOUDPUB_TOKEN");

        CloudPubClientOptions clientOptions = new CloudPubClientOptions();
        if (!string.IsNullOrWhiteSpace(serverUri))
            clientOptions.ServerUri = new Uri(serverUri);

        if (!string.IsNullOrWhiteSpace(token))
        {
            clientOptions.Token = token;
            return clientOptions;
        }

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            clientOptions.Email = email;
            clientOptions.Password = password;
            return clientOptions;
        }

        throw new ArgumentException("Set CLOUDPUB_TOKEN or both CLOUDPUB_EMAIL and CLOUDPUB_PASSWORD (or pass --token / --email --password).");
    }

    private static CliOptions ParseOptions(string[] args)
    {
        CliOptions options = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string key = arg[2..].ToLowerInvariant();
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    options.Values[key] = args[++i];
                else
                    options.Flags.Add(key);

                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length == 2)
            {
                string key = ShortToLongOption(arg[1]);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    options.Values[key] = args[++i];
                else
                    options.Flags.Add(key);

                continue;
            }

            options.Positionals.Add(arg);
        }

        return options;
    }

    private static string ShortToLongOption(char option) => char.ToLowerInvariant(option) switch
    {
        'a' => "address",
        'p' => "protocol",
        'n' => "name",
        'g' => "guid",
        's' => "server",
        't' => "token",
        'e' => "email",
        _ => throw new ArgumentException($"Unknown short option '-{option}'.")
    };

    private static string GetRequired(CliOptions options, string key)
    {
        if (options.Values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new ArgumentException($"Missing required option '--{key}'.");
    }

    private static bool IsHelp(string arg)
        => arg is "help" or "-h" or "--help";

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CloudPub CLI example

            Auth options (env or CLI):
              CLOUDPUB_SERVER_URI / --server <uri>
              CLOUDPUB_TOKEN      / --token <token>
              CLOUDPUB_EMAIL      / --email <email>
              CLOUDPUB_PASSWORD   / --password <password>

            Commands:
              list
                Prints active publications.

              publish --address <addr> [--protocol <proto>] [--name <text>] [--auth <mode>] [--wait]
                Publishes endpoint. Works for http/https/tcp/udp/... (any ProtocolType value).
                Example: publish --protocol tcp --address 127.0.0.1:25565 --name mc

              publish-http <port> [--wait]
                Legacy shortcut for publish --protocol http --address <port>.

              stop --guid <guid>
                Stops traffic for endpoint without removing it.

              unpublish --guid <guid>
                Unpublishes endpoint by guid.

              clean
                Removes all publications from current account.
            """);
    }

    private sealed class CliOptions
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Positionals { get; } = [];
    }
}
