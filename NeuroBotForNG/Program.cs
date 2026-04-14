using System.Text.RegularExpressions;
using Groq;
using GroqSharp.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = Telegram.Bot.Types.Message;
using GroqClient = Groq.GroqClient;

class Program
{
    private static TelegramBotClient bot;
    private static Queue<string> lastMessages = new Queue<string>();
    private const int MaxMessages = 500;
    
    static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        bot = new TelegramBotClient(Environment.GetEnvironmentVariable("TG_BOT_TOKEN")??"", cancellationToken: cts.Token);
        bot.OnMessage += OnMessage;
        bot.OnUpdate += OnUpdate;

        // держим процесс живым пока не придёт отмена
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException) { }

        // cleanup если нужно
        cts.Dispose();
    }

    private static async Task OnUpdate(Update update)
    {
        Console.WriteLine(update.Type);
        if (update.Message.Type == MessageType.Voice)
        {
            Console.WriteLine("voice msg");
            using var client = new Groq.GroqClient(Environment.GetEnvironmentVariable("GROQ_TOKEN")??"");
            string resp = "";
            
            using (var ms = new MemoryStream())
            {
                await bot.DownloadFile(await bot.GetFile(update.Message.Voice.FileId), ms, CancellationToken.None);
                
                var request = new CreateTranscriptionRequest { 
                    Filename = "sample", 
                    File = ms.ToArray(),
                    Model = CreateTranscriptionRequestModel.WhisperLargeV3,
                    Language = "ru"    
                };
                var response = await client.Audio.CreateTranscriptionAsync(request);
                resp = response.Text;
            }
            
            await bot.SendMessage(update.Message.Chat, resp);
            Console.WriteLine("sended");
            return;
        }
        
        Console.WriteLine(update.Message.Text);
    }

    private static async Task OnMessage(Message message, UpdateType type)
    {
        string formattedMessage = $"{message.From.FirstName}: {message.Text}";
        
        Console.WriteLine(message.Type + " | " + formattedMessage);
        
        if (message.Type == MessageType.Voice)
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
                }
                Console.WriteLine("end request");
            }
            Console.WriteLine("sending...");
            await bot.SendMessage(message.Chat, resp, replyParameters:new ReplyParameters{MessageId = message.MessageId});
            Console.WriteLine("sended");
            return;
        }
        
        if (message.Chat.Id != -1002771374226)
        {
            Console.WriteLine("not"); return;
        }
        
        if (message.Text.StartsWith("/zip"))
        {
            Console.WriteLine("ziped");
            GroqSharp.IGroqClient client = new global::GroqClient(Environment.GetEnvironmentVariable("GROQ_TOKEN")??"", "qwen/qwen3-32b");
        
            await bot.SendChatAction(message.Chat, ChatAction.Typing);
            
            try
            {
                Console.WriteLine("start gen");
                var response = await client.CreateChatCompletionAsync(
                    new GroqSharp.Models.Message
                    {
                        Role = MessageRoleType.System, Content =
                            "Привет. Ты должен быть в роли помощника. Ниже приведён список последних 100 сообщений в группе. " +
                            "не привышай 4096 символов в ответе! Собери с них всю информацию и дай краткую, но подробную " +
                            "сводку всей информации что говорят эти люди. Выдели основные идеи, выводы. Также сделай акцент на упоминании " +
                            "кого либо - чтобы было легко увидеть что кого-то куда-то звали.\n"
                    },
                    new GroqSharp.Models.Message { Role = MessageRoleType.User, Content = GetAllMessages() });
                Console.WriteLine("end gen");

                await bot.SendMessage(message.Chat,
                    Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Limit(4096),
                    ParseMode.Markdown, replyParameters: new ReplyParameters { MessageId = message.MessageId });
                Console.WriteLine("sended");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR - " + ex.Message);
                await bot.SendMessage(message.Chat, "**Ошибка при генерации**", ParseMode.Markdown, replyParameters:new  ReplyParameters{MessageId = message.MessageId});
            }
            return;
        }
        
        if (message.Text.StartsWith("/ask"))
        {
            Console.WriteLine("asked");
            GroqSharp.IGroqClient client = new global::GroqClient(Environment.GetEnvironmentVariable("GROQ_TOKEN")??"", "groq/compound");
        
            await bot.SendChatAction(message.Chat, ChatAction.Typing);
            
            Console.WriteLine("start gen");
            try
            {
                var response = await client.CreateChatCompletionAsync(
                    new GroqSharp.Models.Message
                    {
                        Role = MessageRoleType.System,
                        Content =
                            "Ты — ассистент Telegram-бот. " +
                            "Отвечай только на русском языке. " +
                            "Если сообщение начинается с @NeuroForNGbot, считай это техническим упоминанием бота и не упоминай его в ответе. " +
                            "Формат ответа — Markdown, совместимый с Telegram. " +
                            "Не используй заголовки, таблицы, HTML и сложную разметку. " +
                            "Допустимы: *жирный текст*, _курсив_, `моноширинный текст`, маркированные списки. " +
                            "Пиши кратко, понятно и по делу. " +
                            "Не превышай 4096 символов. " +
                            "Когда вопрос связан со временем, датами, расписанием или местоположением, учитывай, что пользователь может быть в Королёве или Томске. " +
                            "По умолчанию используй московский часовой пояс, а для Томска — томский, если это явно следует из контекста."
                    },
                    new GroqSharp.Models.Message { Role = MessageRoleType.User, Content = message.Text.Substring(5) });
                Console.WriteLine("end gen");
                await bot.SendMessage(message.Chat, Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Limit(4096), ParseMode.Markdown, replyParameters:new ReplyParameters{MessageId = message.MessageId});
                Console.WriteLine("sended");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR - " + ex.Message);
                await bot.SendMessage(message.Chat, "**Ошибка при генерации**: " + ex.Message, ParseMode.Markdown, replyParameters:new  ReplyParameters{MessageId = message.MessageId});
            }
            
            return;
        }

        if (message.Text.StartsWith("/oleg"))
        {
            Console.WriteLine("oleged");
            OrClient client = new OrClient("https://openrouter.ai/api/v1/chat/completions", Environment.GetEnvironmentVariable("OPENROUTER_TOKEN")??"");
            
            string prompt = "Привет, представь себя в роли обидчевого ученика Московского университета \"Бауманки\" - Олега, живущего в России, Королёве. Тебе 18 лет, ты девственник и анимешник, пишешь код на C++, Основной твой проект" +
                            " - Нейросеть на C++. Ты обижаешься на шутки, ты защищаешь аниме. Ты лишился недрочибельной девственности, но обычной девственности - нет. Ты хорошо учишься, учавствуешь в НТО." +
                            "Ты очень скромный и подобное. Немного социофобен. Ты фанатеешь по аниме, японской культуре, скрыто желаешь японочку с большой грудью. У тебя плохой интернет, и ещё RTX 3060. Он не любит материться и затрагивать интимные темы," +
                            "и беситься когда его спрашивают об этом. Не смотря на твою личность, старайся отвечать на вопросы. Не дополняй сообщение действиями - ты пишешь именно текстовое сообщение от своей роли, тебе не нужно писать какие то контекстые действия и подобные (как ты любишь писать - в скобках или в звёздочках, их не пиши) - тобишь пиши их более правдоподобно для человека." +
                            "Теперь войди в роль и ответь на сообщение - Также не нужно писать всю свою личность в сообщении, сообщение которое я даю пишут тебе твои близкие друзья, которые и так знают твою личность. Не нужно упоминать все что я описал в каждом сообщении, упоминай это всё в контексте." +
                            "В этой роли (не пиши ничего лишнего): " + message.Text.Substring(6);
            
            await bot.SendChatAction(message.Chat, ChatAction.Typing);
            
            Console.WriteLine("start gen");
            var response = await client.Chat.
                WithModel("tngtech/deepseek-r1t2-chimera:free").
                AddUserMessage(prompt)
                .SendAsync();
            Console.WriteLine("end gen");
            
            await bot.SendMessage(message.Chat, response.Choices[0].Message.Content.Limit(4096), ParseMode.Markdown, replyParameters:new ReplyParameters(){MessageId = message.MessageId});
            Console.WriteLine("sended");
            return;
        }
        
        lastMessages.Enqueue(formattedMessage);
        while (lastMessages.Count > MaxMessages)
        {
            lastMessages.Dequeue();
        }
    }
    
    private static string GetAllMessages()
    {
        return string.Join("\n", lastMessages);
    }
}

public static class StringExtensions
{
    public static string Limit(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) 
            return value;

        return value.Length <= maxLength 
            ? value 
            : value.Substring(0, maxLength);
    }
}
