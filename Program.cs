using System;
using System.Threading.Tasks;

namespace TelegramAutomationBot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Task.Run(async() =>
            {
                using (var tgClient = new TelegramWrapper.TelegramUserClient())
                {
                    await tgClient.ConnectAsync();
                    var user = "tg_bot_tst";
                    var channelInfo = new TelegramWrapper.TelegramUserClient.ChannelInfo { Username = user };
                    await tgClient.SendMessageToChannelAsync(channelInfo, "Hello Dev Testing Bot!");
                    await tgClient.LeaveChannelAsync(channelInfo);
                    Console.WriteLine("Message sent!");
                    // 📂 Load channels
                    //var imported = tgClient.ImportChannelsFromJson("channels.json");

                    //// 🎯 Custom templates per channel
                    //var customTemplates = new Dictionary<string, string>
                    //{
                    //    { "techupdates", "🔥 Hey {channel}, {members} users are waiting for news!" },
                    //    { "1234567890", "📊 Report for {channel}: {about}" }
                    //};

                    //// ✅ Batch send with placeholders
                    //await tgClient.BatchSendMessagesAsync(imported,
                    //    "Hello {channel} 👋 You currently have {members} members.",
                    //    customTemplates,
                    //    8000,
                    //    15000);
                }
            });
            Console.ReadKey();
        }
    }
}
