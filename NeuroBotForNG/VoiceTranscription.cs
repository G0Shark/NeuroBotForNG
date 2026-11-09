using Groq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace NeuroBotForNG;

public static class VoiceTranscription
{
    public static async Task Handler(Message message, TelegramBotClient bot)
    {
        if (message.Voice == null) return;

        Logger.Log($"Voice message arrived: {message.Id}");
        string apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
            ?? Environment.GetEnvironmentVariable("GROQ_TOKEN")
            ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.Error("Groq API key is not set (GROQ_API_KEY or GROQ_TOKEN is required)");
            await bot.SendMessage(message.Chat, "⛔ Ошибка: GROQ_API_KEY / GROQ_TOKEN не настроен в .env", replyParameters: new ReplyParameters { MessageId = message.MessageId });
            return;
        }

        using var client = new Groq.GroqClient(apiKey);
        string resp = "";

        Logger.Log($"Started MemoryStream");
        using (var ms = new MemoryStream())
        {
            Logger.Log("Download voiceline");
            await bot.DownloadFile(await bot.GetFile(message.Voice.FileId), ms, CancellationToken.None);
            Logger.Log("Download ended");
            var request = new CreateTranscriptionRequest { 
                Filename = message.MessageId+".ogg", 
                File = ms.ToArray(),
                Model = CreateTranscriptionRequestModel.WhisperLargeV3,
                Language = "ru"    
            };
            Logger.Log("Sending request to GROQ");
            try
            {
                var response = await client.Audio.CreateTranscriptionAsync(request);
                resp = response.Text;
            }
            catch (Exception e)
            {
                Logger.Error($"Transcription failed: {e.Message}");
                
                Console.WriteLine(e);
                resp = $"⛔ Ошибка транскрипции сообщения: {e.Message}";
                await bot.SendMessage(message.Chat, resp, replyParameters:new ReplyParameters{MessageId = message.MessageId});
            }
            Logger.Log("End request to GROQ");
        }
        Logger.Log("Sending message to user");
        await bot.SendMessage(message.Chat, resp, replyParameters:new ReplyParameters{MessageId = message.MessageId});
        Logger.Log($"Completed: {message.Id}");
        return;
    }
}