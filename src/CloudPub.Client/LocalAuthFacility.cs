using CloudPub.Components;
using System.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace CloudPub;

/// <summary>
/// Default implementation of <see cref="IAuthFacility"/>. Stores client info in the local storage.
/// </summary>
public class LocalAuthFacility : IAuthFacility
{
    private class CloudpubClientConfig
    {
        public string? AgentId { get; set; }
    }

    /// <inheritdoc/>
    public Task<string?> TryLoadAgentIdAsync(bool userDir)
    {
        string dir = userDir ? GetCachePath() : GetUserlessCachePath();
        if (!Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Can't create config dir: " + dir + ", error: " + ex.Message);
                throw new IOException($"Can't create config dir: {dir}", ex);
            }
        }

        string cfg = Path.Combine(dir, "client.toml");
        if (!File.Exists(cfg))
        {
            Debug.WriteLine("Config file not found, returning null");
            return Task.FromResult<string?>(null);
        }

        Debug.WriteLine($"Config dir: {dir}");
        if (!TomlSerializer.TryDeserialize(File.ReadAllText(cfg), out TomlTable? table))
        {
            Debug.WriteLine("Failed to parse config file, returning null");
            return Task.FromResult<string?>(null);
        }

        CloudpubClientConfig config = ReadConfig(table);
        return Task.FromResult(config.AgentId);
    }

    /// <inheritdoc/>
    public Task TrySaveAgentIdAsync(bool userDir, string? agentId)
    {
        string dir = userDir ? GetCachePath() : GetUserlessCachePath();
        if (!Directory.Exists(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Can't create config dir: " + dir + ", error: " + ex.Message);
                throw new IOException($"Can't create config dir: {dir}", ex);
            }
        }

        string cfg = Path.Combine(dir, "config.toml");
        try
        {
            FileStream stream = File.Create(cfg);
            CloudpubClientConfig config = new CloudpubClientConfig() { AgentId = agentId };
            TomlSerializer.Serialize(stream, WriteConfig(config));
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Can't create config file: " + cfg + ", error: " + ex.Message);
            throw new IOException($"Can't create config file: {cfg}", ex);
        }
    }

    private static CloudpubClientConfig ReadConfig(TomlTable table)
    {
        CloudpubClientConfig config = new CloudpubClientConfig();
        if (table.TryGetValue("agent_id", out object? agentIdObj) && agentIdObj is string agentId)
            config.AgentId = agentId;

        return config;
    }

    private static TomlTable WriteConfig(CloudpubClientConfig config)
    {
        TomlTable table = new TomlTable();
        if (!string.IsNullOrEmpty(config.AgentId))
            table.Add("agent_id", config.AgentId);

        return table;
    }

    private string GetCachePath()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32NT:
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                        ?? throw new UnauthorizedAccessException();

                    return Path.Combine(appData, "cloudpub");
                }
            
            case PlatformID.Unix:
                {
                    string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    if (!string.IsNullOrEmpty(xdgConfigHome))
                        return xdgConfigHome;

                    string? home = Environment.GetEnvironmentVariable("HOME")
                        ?? throw new UnauthorizedAccessException();
                    
                    return Path.Combine(home, ".config", "cloudpub");
                }

            case PlatformID.MacOSX:
                {
                    string? home = Environment.GetEnvironmentVariable("HOME")
                        ?? throw new UnauthorizedAccessException();

                    return Path.Combine(home, "Library", "Application Support", "cloudpub");
                }

            default:
                throw new NotSupportedException("Unsupported platform");
        }
    }

    private string GetUserlessCachePath()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32NT:
                {
                    string rootDisk = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    return Path.Combine(rootDisk, "config\\systemprofile\\AppData\\Local\\cloudpub");
                }
            
            case PlatformID.Unix:
                {
                    return "/var/cache/cloudpub";
                }

            case PlatformID.MacOSX:
                {
                    return "/Library/Caches/cloudpub";
                }

            default:
                throw new NotSupportedException("Unsupported platform");
        }
    }
}
