using Groq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace NeuroBotForNG;

public static class VoiceTranscription
{
    public static async Task Handler(Message message, TelegramBotClient bot)
    {
        Logger.Log($"Voice message arrived: {message.Id}");
        using var client = new Groq.GroqClient(Environment.GetEnvironmentVariable("GROQ_TOKEN")??"");
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