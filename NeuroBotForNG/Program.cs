using System.ClientModel;
using DotNetEnv;
using NeuroBotForNG;
using OpenAI;
using OpenAI.Chat;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    private static TelegramBotClient bot;
    private static Queue<string> lastMessages = new Queue<string>();
    private const int MaxMessages = 500;
    
    private static ChatCompletionOptions competitionOptions;
    private static ChatClient client;
    private static List<ChatMessage> messages;
    static async Task Main(string[] args)
    {
        Env.Load();
        
        client = new(
            model: "mimo-v2.5-pro",
            credential: new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!),
            options: new OpenAIClientOptions()
            {
                Endpoint = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL")!)
            });
        
        Console.WriteLine("warmup");

        competitionOptions = Tools.GetTools();
        messages = new List<ChatMessage>
        {
            new SystemChatMessage(File.ReadAllText("./system_prompt.txt"))
        };
        
        Console.WriteLine("warmup ended");
        
        var cts = new CancellationTokenSource();
        bot = new TelegramBotClient(Environment.GetEnvironmentVariable("TG_BOT_TOKEN")!, cancellationToken: cts.Token);
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
            await VoiceTranscription.Handler(update.Message, bot);
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
            await VoiceTranscription.Handler(message, bot);
            return;
        }
        
        if (message.Chat.Id != long.Parse(Environment.GetEnvironmentVariable("GROUP_ID")!))
        {
            await bot.SendMessage(
                chatId: message.Chat,
                text: "⛔ *Я работаю только в группе NewGame!*\nЧтобы использовать меня - используйте /ask в *группе*\nЛибо пересылай голосовое сообщение - я дам тебе его текст",
                parseMode: ParseMode.Markdown,
                replyParameters: new ReplyParameters
                {
                    MessageId = message.MessageId
                }
            );
            
            return;
        }

        /*if (message.Text.StartsWith("/reset"))
        {
            if (message.From.Id != 5061499376)
            {
                await bot.SendMessage(
                    chatId: message.Chat,
                    text: "⛔ *Сброс контекста разрешён только мне)*",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = message.MessageId
                    }
                );
            }
            else
            {
                await bot.SendMessage(
                    chatId: message.Chat,
                    text: "⛔ *Перезапуск контекста...*",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = message.MessageId
                    }
                );
                
                var codex = new Codex(new CodexOptions
                {
                    CodexPathOverride = ".\\codex.exe"
                });
                codexThread = codex.StartThread(new ThreadOptions
                {
                    SkipGitRepoCheck = true,
                    Model = "gpt-5.5"
                });
        
                Console.WriteLine("warmup");
        
                await codexThread.RunAsync(File.ReadAllText("./system_prompt.txt"));
        
                Console.WriteLine("warmup ended");
            }
        }*/
        
        if (message.Text.StartsWith("/ask"))
        {
            _ = Task.Run(async () =>
            {
                Message loadingMessage = await bot.SendMessage(
                    chatId: message.Chat,
                    text: "⌛ *Генерирую ответ...*",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = message.MessageId
                    }
                );
                try
                {
                    messages.Add(new UserChatMessage(formattedMessage));
                    
                    while (true)
                    {
                        ChatCompletion response = await client.CompleteChatAsync(
                            messages,
                            competitionOptions
                        );

                        if (response.FinishReason == ChatFinishReason.Stop)
                        {
                            messages.Add(new AssistantChatMessage(response.Content[0].Text));

                            string end = $"\n\n`{response.Usage.TotalTokenCount} токенов`";
                            
                            try
                            {
                                await bot.EditMessageText(
                                    chatId: message.Chat.Id,
                                    messageId: loadingMessage.MessageId,
                                    text: response.Content[0].Text + end,
                                    parseMode: ParseMode.Markdown
                                );
                            }
                            catch (Exception e)
                            {
                                await bot.EditMessageText(
                                    chatId: message.Chat.Id,
                                    messageId: loadingMessage.MessageId,
                                    text: EscapeMarkdownV2(response.Content[0].Text) + $"{end} | *⛔ Ошибка MARKDOWN*",
                                    parseMode: ParseMode.Markdown
                                );
                            }
                            return;
                        }
                        
                        if (response.FinishReason == ChatFinishReason.ToolCalls)
                        {
                            foreach (var call in response.ToolCalls)
                            {
                                try
                                {
                                    await bot.EditMessageText(
                                        chatId: message.Chat.Id,
                                        messageId: loadingMessage.MessageId,
                                        text: $"⚙️ *Потребовался инструмент:* `{call.FunctionName}` (ID: {call.Id})",
                                        parseMode: ParseMode.Markdown
                                    );
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                                
                                if (call.FunctionName == "get_msg_history")
                                {
                                    messages.Add(new AssistantChatMessage(response));
                                    messages.Add(new ToolChatMessage(call.Id, GetAllMessages()));
                                }
                                else
                                {
                                    Tools.RunTool(call, messages, response);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    
                    await bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: loadingMessage.MessageId,
                        text: "🚨 Случилась необработанная ошибка: " + ex.Message + "\n\nStackTrace: " + ex.StackTrace
                    );
                }
            });

            return;
        } 
        
        lastMessages.Enqueue(formattedMessage);
        while (lastMessages.Count > MaxMessages)
        {
            lastMessages.Dequeue();
        }
    }
    
    static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var chars = new[] { "_", "*", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!" };

        foreach (var ch in chars)
            text = text.Replace(ch, "\\" + ch);

        return text;
    }
    
    private static string GetAllMessages()
    {
        return string.Join("\n", lastMessages);
    }
}
