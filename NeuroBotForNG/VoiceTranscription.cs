using Groq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace NeuroBotForNG;

public static class VoiceTranscription
{
    public static async Task Handler(Message message, TelegramBotClient bot)
    {
        Console.WriteLine("voice msg");
        using var client = new Groq.GroqClient(Environment.GetEnvironmentVariable("GROQ_TOKEN")??"");
        string resp = "";

        Console.WriteLine("start ms");
        using (var ms = new MemoryStream())
        {
            Console.WriteLine("download");
            await bot.DownloadFile(await bot.GetFile(message.Voice.FileId), ms, CancellationToken.None);
            Console.WriteLine("end");
            var request = new CreateTranscriptionRequest { 
                Filename = message.MessageId+".ogg", 
                File = ms.ToArray(),
                Model = CreateTranscriptionRequestModel.WhisperLargeV3,
                Language = "ru"    
            };
            Console.WriteLine("send request");
            try
            {
                var response = await client.Audio.CreateTranscriptionAsync(request);
                resp = response.Text;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                resp = $"⛔ Ошибка транскрипции сообщения: {e.Message}";
                await bot.SendMessage(message.Chat, resp, replyParameters:new ReplyParameters{MessageId = message.MessageId});
            }
            Console.WriteLine("end request");
        }
        Console.WriteLine("sending...");
        await bot.SendMessage(message.Chat, resp, replyParameters:new ReplyParameters{MessageId = message.MessageId});
        Console.WriteLine("sended");
        return;
    }
}