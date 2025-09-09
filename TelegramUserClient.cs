namespace TelegramAutomationBot
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using TL;
    using WTelegram;

    namespace TelegramWrapper
    {
        public class TelegramUserClient : IDisposable
        {
            private readonly Client _client;
            private readonly Random _rand = new Random();
            public static ConfigModel model = new ConfigModel();
            public static string SessionFile => Path.Combine(GetDir(), "Telegram.session");
            public TelegramUserClient()
            {
                try
                {
                    byte[] startSession = File.Exists(SessionFile) ? File.ReadAllBytes(SessionFile) : null;
                    _client = new Client(Config, startSession, SaveSession);
                } catch (Exception ex) { }
            }

            public static string GetDir()
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                dir = Path.Combine(dir, "TelegramSessions");
                Directory.CreateDirectory(dir);
                return dir;
            }
            private static void SaveSession(byte[] data)
            {
                File.WriteAllBytes(SessionFile, data);
            }
            private static string Config(string what)
            {
                switch (what)
                {
                    case "api_id": return model.YOUR_API_ID.ToString();           // from https://my.telegram.org
                    case "api_hash": return model.YOUR_API_HASH;       // from https://my.telegram.org
                    case "phone_number": return model.Number;     // your phone number
                    case "verification_code":
                        Console.WriteLine("Enter the verification code you received: ");
                        return Console.ReadLine();
                    case "password":                                // if you enabled 2FA in Telegram
                        Console.Write("Enter your Telegram password: ");
                        return Console.ReadLine();
                    default: return null;
                }
            }
            public async Task ConnectAsync()
            {
                try
                {
                    await _client.LoginUserIfNeeded();
                }
                catch (Exception ex){ 

                }
            }

            private async Task<Channel> EnsureJoinedChannel(Channel channel)
            {
                if (channel == null) throw new ArgumentNullException(nameof(channel));

                if (!channel.IsActive)
                {
                    Console.WriteLine($"Joining channel @{channel.username}...");
                    await _client.Channels_JoinChannel(channel);
                }

                return channel;
            }

            // ---------- DTO ----------
            public class ChannelInfo
            {
                public long Id { get; set; }
                public string Title { get; set; }
                public string Username { get; set; }
                public int? Members { get; set; }
                public string About { get; set; }
                public bool IsJoined { get; set; }

                [System.Text.Json.Serialization.JsonIgnore]
                public Channel ChannelRef { get; set; }
            }

            // ---------- SEARCH ----------
            public async Task<ChannelInfo[]> SearchChannelsWithInfoAsync(string query, int limit = 10)
            {
                var result = await _client.Contacts_Search(query, limit);
                var channels = result.chats.Values.OfType<Channel>().ToArray();

                var detailed = await Task.WhenAll(channels.Select(async c =>
                {
                    string about = null;
                    try
                    {
                        var full = await _client.Channels_GetFullChannel(c);
                        about = full.full_chat?.About;
                    }
                    catch { }

                    return new ChannelInfo
                    {
                        Id = c.id,
                        Title = c.Title,
                        Username = c.username,
                        Members = c.participants_count,
                        About = about,
                        IsJoined = !(c.IsActive ? true: false),
                        ChannelRef = c
                    };
                }));

                return detailed.OrderByDescending(ci => ci.Members ?? 0).ToArray();
            }
            public async Task SendMessageToUserAsync(string username, string message)
            {
                if (string.IsNullOrWhiteSpace(username))
                    throw new ArgumentException("Username is required", nameof(username));

                var resolved = await _client.Contacts_ResolveUsername(username.TrimStart('@'));

                if (resolved.User == null)
                    throw new Exception($"User @{username} not found.");

                var user = resolved.User;

                await _client.Messages_SendMessage(
                    new InputPeerUser(user.id,user.access_hash),
                    message,123456
                );
            }
            // ---------- JSON EXPORT/IMPORT ----------
            public async Task ExportChannelsToJsonAsync(ChannelInfo[] channels, string filePath)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(channels, options);
                File.WriteAllText(filePath, json);
                Console.WriteLine($"✅ Exported {channels.Length} channels to {filePath}");
            }

            public ChannelInfo[] ImportChannelsFromJson(string filePath)
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ChannelInfo[]>(json);
            }

            // ---------- RESOLVE ----------
            public async Task<Channel> ResolveChannelAsync(ChannelInfo info)
            {
                if (info.ChannelRef != null) return info.ChannelRef;

                Channel resolved = null;

                if (!string.IsNullOrEmpty(info.Username))
                {
                    var resolvedPeer = await _client.Contacts_ResolveUsername(info.Username);
                    resolved = resolvedPeer.Channel;
                }
                else
                {
                    var chats = await _client.Messages_GetAllChats();
                    resolved = chats.chats.Values.OfType<Channel>().FirstOrDefault(c => c.id == info.Id);
                }

                info.ChannelRef = resolved ?? throw new Exception($"Unable to resolve channel {info.Title}");
                return resolved;
            }

            // ---------- TEMPLATE ENGINE ----------
            private string ApplyTemplate(string template, ChannelInfo info)
            {
                if (string.IsNullOrEmpty(template)) return template;

                return template
                    .Replace("{id}", info.Id.ToString())
                    .Replace("{channel}", info.Title ?? "")
                    .Replace("{username}", info.Username ?? "")
                    .Replace("{members}", info.Members?.ToString() ?? "N/A")
                    .Replace("{about}", info.About ?? "")
                    .Replace("{joined}", info.IsJoined ? "Yes" : "No");
            }

            // ---------- MESSAGING ----------
            public async Task SendMessageToChannelAsync(ChannelInfo channelInfo, string message)
            {
                var resolved = await ResolveChannelAsync(channelInfo);
                var joined = await EnsureJoinedChannel(resolved);
                await _client.SendMessageAsync(joined, message);
                Console.WriteLine($"✅ Message sent to {channelInfo.Title}");
            }

            /// <summary>
            /// Batch messaging with per-channel customization, templates and random delay.
            /// </summary>
            public async Task BatchSendMessagesAsync(IEnumerable<ChannelInfo> channels,
                                                     string defaultMessage,
                                                     Dictionary<string, string> customTemplates = null,
                                                     int minDelayMs = 8000,
                                                     int maxDelayMs = 15000)
            {
                var channelList = channels.ToList();

                for (int i = 0; i < channelList.Count; i++)
                {
                    var info = channelList[i];

                    // 1️⃣ Pick default
                    string template = defaultMessage;

                    // 2️⃣ Override if custom exists
                    if (customTemplates != null)
                    {
                        if (!string.IsNullOrEmpty(info.Username) && customTemplates.TryGetValue(info.Username, out var userMsg))
                            template = userMsg;
                        else if (customTemplates.TryGetValue(info.Id.ToString(), out var idMsg))
                            template = idMsg;
                    }

                    // 3️⃣ Apply placeholders
                    string msgToSend = ApplyTemplate(template, info);

                    try
                    {
                        await SendMessageToChannelAsync(info, msgToSend);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to send to {info.Title}: {ex.Message}");
                    }

                    if (i < channelList.Count - 1) // skip last
                    {
                        int delay = _rand.Next(minDelayMs, maxDelayMs + 1);
                        Console.WriteLine($"⏳ Waiting {delay / 1000.0:F1} seconds before next send...");
                        await Task.Delay(delay);
                    }
                }

                Console.WriteLine("✅ Batch messaging complete.");
            }
            public async Task LeaveChannelAsync(ChannelInfo channelInfo)
            {
                if (channelInfo == null) throw new ArgumentNullException(nameof(channelInfo));

                var resolved = await ResolveChannelAsync(channelInfo); // get Channel object

                if (!resolved.IsActive)
                {
                    Console.WriteLine($"You are not a member of @{channelInfo.Username}, cannot leave.");
                    return;
                }

                try
                {
                    await _client.Channels_LeaveChannel(resolved);
                    Console.WriteLine($"✅ Left channel @{channelInfo.Username}");
                    channelInfo.IsJoined = false; // update DTO
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to leave @{channelInfo.Username}: {ex.Message}");
                }
            }

            public void Dispose() => _client.Dispose();
        }
    }

}
