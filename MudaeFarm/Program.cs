using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MudaeFarm
{
    class Program
    {
        public static ulong[] MudaeIds = new ulong[]
        {
            522749851922989068
        };

        static ILogger _logger;
        static DiscordSocketClient _discord;

        static async Task Main(string[] args)
        {
            // Configure services
            var services = configureServices(new ServiceCollection()).BuildServiceProvider();

            using (services.CreateScope())
            {
                _logger = services.GetService<ILogger<Program>>();
                _discord = services.GetService<DiscordSocketClient>();

                // Load configuration
                await LoadConfigAsync();

                // Register events
                _discord.Log += handleLogAsync;
                _discord.MessageReceived += handleMessageAsync;

                // Login
                var connectionSource = new TaskCompletionSource<object>();

                _discord.Connected += handleConnect;
                Task handleConnect()
                {
                    connectionSource.SetResult(null);
                    return Task.CompletedTask;
                }

                await _discord.LoginAsync(TokenType.User, _config.AuthToken);
                await _discord.StartAsync();

                await connectionSource.Task;

                _discord.Connected -= handleConnect;

                // Keep the bot running
                // TODO: graceful shutdown?
                await Task.Delay(-1);

                // Unregister events
                _discord.Log -= handleLogAsync;
                _discord.MessageReceived -= handleMessageAsync;

                // Logout
                await _discord.StopAsync();
                await _discord.LogoutAsync();
            }
        }

        static IServiceCollection configureServices(IServiceCollection services) => services
            .AddSingleton<DiscordSocketClient>()
            .AddLogging(l => l.AddConsole());

        static Task handleLogAsync(LogMessage m)
        {
            var level = LogLevel.None;

            switch (m.Severity)
            {
                case LogSeverity.Verbose: level = LogLevel.Trace; break;
                case LogSeverity.Debug: level = LogLevel.Debug; break;
                case LogSeverity.Info: level = LogLevel.Information; break;
                case LogSeverity.Warning: level = LogLevel.Warning; break;
                case LogSeverity.Error: level = LogLevel.Error; break;
                case LogSeverity.Critical: level = LogLevel.Critical; break;
            }

            if (m.Exception == null)
                _logger.Log(level, m.Message);
            else
                _logger.Log(level, m.Exception, m.Exception.Message);

            return Task.CompletedTask;
        }

        static async Task handleMessageAsync(SocketMessage message)
        {
            if (!(message is SocketUserMessage userMessage))
                return;

            var author = message.Author.Id;

            if (author == _discord.CurrentUser.Id)
                await handleSelfCommandAsync(userMessage);

            else if (Array.IndexOf(MudaeIds, author) != -1)
                await handleMudaeMessageAsync(userMessage);
        }

        static async Task handleSelfCommandAsync(SocketUserMessage message)
        {
            var content = message.Content;

            if (!content.StartsWith('/'))
                return;

            content = content.Substring(1);

            var command = content.Substring(0, content.IndexOf(' '));
            var argument = content.Substring(content.IndexOf(' ') + 1);

            if (!string.IsNullOrWhiteSpace(command) &&
                !string.IsNullOrWhiteSpace(argument))
                switch (command.ToLowerInvariant())
                {
                    case "wish":
                        _config.WishlistCharacters.Add(argument.ToLowerInvariant());
                        break;
                    case "unwish":
                        _config.WishlistCharacters.Remove(argument.ToLowerInvariant());
                        break;
                    default:
                        return;
                }

            await message.DeleteAsync();
            await SaveConfigAsync();
        }

        static async Task handleMudaeMessageAsync(SocketUserMessage message)
        {
            if (!message.Embeds.Any())
                return;

            var embed = message.Embeds.First();

            if (!embed.Footer.HasValue)
                return;

            var footer = embed.Footer.Value.Text;

            if (footer.StartsWith("Belongs to", StringComparison.OrdinalIgnoreCase))
                return;

            var delimitor = footer.IndexOf('/');

            if (delimitor == -1)
                return;

            var name = footer.Substring(0, delimitor).Trim().ToLowerInvariant();
            var anime = footer.Substring(delimitor + 1).Trim().ToLowerInvariant();

            if (int.TryParse(name, out _) &&
                int.TryParse(anime, out _))
                return;

            if (_config.WishlistCharacters.Contains(name) ||
                _config.WishlistAnimes.Contains(name))
            {
                _logger.LogInformation($"Found character '{name}', trying marriage.");

                await message.AddReactionAsync(new Emoji("\uD83D\uDC96"));
                await SaveConfigAsync();
            }
            else
                _logger.LogInformation($"Ignored character '{name}', not wished.");
        }

        static Config _config;

        static async Task LoadConfigAsync()
        {
            try
            {
                _config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync("config.json"));
            }
            catch (FileNotFoundException)
            {
                _config = new Config();
            }
        }

        static async Task SaveConfigAsync()
        {
            await File.WriteAllTextAsync("config.json", JsonConvert.SerializeObject(_config));
        }
    }
}
