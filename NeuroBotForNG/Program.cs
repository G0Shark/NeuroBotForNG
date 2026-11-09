using System.ClientModel;
using System.Diagnostics;
using DotNetEnv;
using NeuroBotForNG;
using OpenAI;
using OpenAI.Chat;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static TelegramBotClient bot = null!;
    private static readonly Queue<string> lastMessages = new Queue<string>();
    private const int MaxMessages = 500;
    
    private static ChatCompletionOptions competitionOptions = null!;
    private static ChatClient client = null!;
    private static List<ChatMessage> messages = null!;
    private static readonly SemaphoreSlim chatLock = new SemaphoreSlim(1, 1);

    private static string systemPrompt = "";
    private static string groqModel = "llama-3.3-70b-versatile";

    static async Task Main(string[] args)
    {
        Env.Load();

        string groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY")
            ?? Environment.GetEnvironmentVariable("GROQ_TOKEN")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? "";

        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            Logger.Error("Ключ GROQ_API_KEY / GROQ_TOKEN не найден в .env!");
        }

        string groqBaseUrl = Environment.GetEnvironmentVariable("GROQ_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            ?? "https://api.groq.com/openai/v1";

        groqModel = Environment.GetEnvironmentVariable("GROQ_MODEL")
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? "llama-3.3-70b-versatile";

        Logger.Log($"Инициализация AI клиента: Модель: {groqModel}, Endpoint: {groqBaseUrl}");

        client = new(
            model: groqModel,
            credential: new ApiKeyCredential(groqApiKey),
            options: new OpenAIClientOptions()
            {
                Endpoint = new Uri(groqBaseUrl)
            });
        
        competitionOptions = Tools.GetTools();

        string promptPath = "./system_prompt.txt";
        systemPrompt = File.Exists(promptPath)
            ? File.ReadAllText(promptPath)
            : "Ты — @NeuroBotForNG, полезный ИИ-помощник в Telegram.";

        messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt)
        };
        
        var cts = new CancellationTokenSource();
        string tgBotToken = Environment.GetEnvironmentVariable("TG_BOT_TOKEN") ?? "";
        if (string.IsNullOrWhiteSpace(tgBotToken))
        {
            Logger.Error("TG_BOT_TOKEN не задан в .env!");
        }

        bot = new TelegramBotClient(tgBotToken, cancellationToken: cts.Token);
        bot.OnMessage += OnMessage;
        bot.OnUpdate += OnUpdate;

        Logger.Log("Бот успешно запущен и ожидает сообщений...");

        // держим процесс живым пока не придёт отмена
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException) { }

        // cleanup
        cts.Dispose();
    }

    private static async Task OnUpdate(Update update)
    {
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            await OnCallbackQuery(update.CallbackQuery);
        }
    }

    private static async Task OnCallbackQuery(CallbackQuery query)
    {
        if (string.IsNullOrEmpty(query.Data) || !query.Data.StartsWith("del:"))
        {
            return;
        }

        string[] parts = query.Data.Split(':');
        if (parts.Length > 1 && long.TryParse(parts[1], out long authorId) && authorId != 0)
        {
            if (query.From.Id != authorId)
            {
                try
                {
                    if (query.Message != null)
                    {
                        var member = await bot.GetChatMember(query.Message.Chat.Id, query.From.Id);
                        if (member.Status != ChatMemberStatus.Administrator && member.Status != ChatMemberStatus.Creator)
                        {
                            await bot.AnswerCallbackQuery(query.Id, "⛔ Удалить ответ может только автор вопроса или администратор чата.", showAlert: true);
                            return;
                        }
                    }
                }
                catch
                {
                    await bot.AnswerCallbackQuery(query.Id, "⛔ Удалить ответ может только автор вопроса.", showAlert: true);
                    return;
                }
            }
        }

        if (query.Message != null)
        {
            try
            {
                await bot.DeleteMessage(query.Message.Chat.Id, query.Message.MessageId);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to delete message: {ex.Message}");
            }
        }

        try
        {
            await bot.AnswerCallbackQuery(query.Id);
        }
        catch { }
    }

    private static async Task OnMessage(Message message, UpdateType type)
    {
        if (message == null) return;
        Logger.Message(message);
        
        if (message.Type == MessageType.Voice)
        {
            await VoiceTranscription.Handler(message, bot);
            return;
        }

        string? text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string senderName = message.From?.FirstName ?? message.SenderChat?.Title ?? "Unknown";

        // Проверка разрешенной группы
        string? allowedGroupIdStr = Environment.GetEnvironmentVariable("GROUP_ID");
        if (!string.IsNullOrWhiteSpace(allowedGroupIdStr) && long.TryParse(allowedGroupIdStr, out long allowedGroupId))
        {
            if (message.Chat.Id != allowedGroupId)
            {
                Logger.Warn($"Bot triggered in not allowed group: {message.Chat.Id}");
                await bot.SendMessage(
                    chatId: message.Chat,
                    text: "⛔ *Я работаю только в группе NewGame!*\nЧтобы использовать меня — пишите в *группе*\nЛибо пересылайте голосовые сообщения в ЛС для транскрипции.",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = message.MessageId
                    }
                );
                return;
            }
        }

        // Команда /help или /start
        if (IsCommand(text, "/help") || IsCommand(text, "/start"))
        {
            await bot.SendMessage(
                chatId: message.Chat,
                text: "🤖 *Команды @NeuroBotForNG:*\n\n" +
                      "• `/ask <вопрос>` — задать вопрос нейросети (или ответьте Reply на сообщение)\n" +
                      "• `/reset` — сбросить контекст диалога бота\n" +
                      "• `/status` — информация о модели и состоянии бота\n" +
                      "• 🎤 *Голосовые сообщения* — отправьте голосовое в чат или перешлите для расшифровки\n\n" +
                      "🛠 *Инструменты AI:* поиск информации в вебе (`http_get`) и чтение недавней истории чата (`get_msg_history`).",
                parseMode: ParseMode.Markdown,
                replyParameters: new ReplyParameters { MessageId = message.MessageId }
            );
            return;
        }

        // Команда /status
        if (IsCommand(text, "/status"))
        {
            await bot.SendMessage(
                chatId: message.Chat,
                text: $"📊 *Статус NeuroBotForNG:*\n\n" +
                      $"• *Провайдер:* Groq Cloud (LPU)\n" +
                      $"• *LLM Модель:* `{groqModel}`\n" +
                      $"• *STT Модель:* `whisper-large-v3`\n" +
                      $"• *Сообщений в контексте диалога:* {messages.Count}\n" +
                      $"• *Сообщений в буфере чата:* {lastMessages.Count}/{MaxMessages}\n" +
                      $"• *Инструменты:* `get_msg_history`, `http_get`",
                parseMode: ParseMode.Markdown,
                replyParameters: new ReplyParameters { MessageId = message.MessageId }
            );
            return;
        }

        // Команда /reset
        if (IsCommand(text, "/reset"))
        {
            await chatLock.WaitAsync();
            try
            {
                messages.Clear();
                messages.Add(new SystemChatMessage(systemPrompt));
                await bot.SendMessage(
                    chatId: message.Chat,
                    text: "🔄 *Контекст диалога успешно сброшен!*\nПамять бота очищена и возвращена к исходному системному промпту.",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters { MessageId = message.MessageId }
                );
            }
            finally
            {
                chatLock.Release();
            }
            return;
        }

        // Команда /ask
        if (IsCommand(text, "/ask"))
        {
            _ = Task.Run(async () =>
            {
                string cleanPrompt = text;
                int spaceIndex = cleanPrompt.IndexOf(' ');
                cleanPrompt = spaceIndex >= 0 ? cleanPrompt.Substring(spaceIndex + 1).Trim() : "";

                // Если вопрос пустой и нет Reply на сообщение
                if (string.IsNullOrWhiteSpace(cleanPrompt) && message.ReplyToMessage == null)
                {
                    await bot.SendMessage(
                        chatId: message.Chat,
                        text: "💡 *Как использовать команду /ask:*\n\n" +
                              "• `/ask <вопрос>` — задайте вопрос нейросети\n" +
                              "• Ответьте *(Reply)* командой `/ask` на чужое сообщение, чтобы нейросеть ответила с его учетом\n\n" +
                              $"✨ *Модель:* `{groqModel}` на Groq\n" +
                              "⚡ *Скорость:* ~280 токенов/сек",
                        parseMode: ParseMode.Markdown,
                        replyParameters: new ReplyParameters { MessageId = message.MessageId }
                    );
                    return;
                }

                // Извлечение контекста Reply, если есть
                string replyContext = "";
                if (message.ReplyToMessage != null)
                {
                    string replySender = message.ReplyToMessage.From?.FirstName ?? message.ReplyToMessage.SenderChat?.Title ?? "Собеседник";
                    string replyBody = message.ReplyToMessage.Text ?? message.ReplyToMessage.Caption ?? "[Вложенный файл или медиа]";
                    replyContext = $"[В ответ на сообщение от {replySender}: \"{replyBody}\"]\n";
                }

                string userQuery = string.IsNullOrWhiteSpace(cleanPrompt)
                    ? "Проанализируй это сообщение и ответь на него."
                    : cleanPrompt;

                string formattedForAi = $"{replyContext}{senderName}: {userQuery}";

                Logger.Log($"ASK command arrived from {senderName} (MsgId: {message.Id})");
                Message loadingMessage = await bot.SendMessage(
                    chatId: message.Chat,
                    text: "🧠 *Думаю над ответом...*",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters
                    {
                        MessageId = message.MessageId
                    }
                );

                // Запуск индикатора набора текста Telegram
                using var typingCts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    while (!typingCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await bot.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: typingCts.Token);
                        }
                        catch { }

                        try
                        {
                            await Task.Delay(4000, typingCts.Token);
                        }
                        catch (OperationCanceledException) { }
                    }
                }, typingCts.Token);

                var stopwatch = Stopwatch.StartNew();

                await chatLock.WaitAsync();
                try
                {
                    TrimHistoryIfNeeded();
                    messages.Add(new UserChatMessage(formattedForAi));
                    
                    int maxIterations = 8;
                    while (maxIterations-- > 0)
                    {
                        ChatCompletion response = await client.CompleteChatAsync(
                            messages,
                            competitionOptions
                        );

                        if (response.FinishReason == ChatFinishReason.Stop)
                        {
                            stopwatch.Stop();
                            typingCts.Cancel();

                            string replyText = response.Content.Count > 0 ? response.Content[0].Text : "Пустой ответ.";
                            messages.Add(new AssistantChatMessage(replyText));

                            double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                            string tokenInfo = response.Usage != null
                                ? $"\n\n⚡ *{elapsedSec:F1}с* • `{response.Usage.TotalTokenCount} токенов` • *Groq*"
                                : $"\n\n⚡ *{elapsedSec:F1}с* • *Groq*";

                            var deleteMarkup = new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"del:{message.From?.Id ?? 0}")
                            );

                            await SendOrEditTelegramResponse(bot, message.Chat.Id, loadingMessage.MessageId, replyText, tokenInfo, deleteMarkup);
                            return;
                        }
                        
                        if (response.FinishReason == ChatFinishReason.ToolCalls)
                        {
                            Logger.Log($"AI requested tool execution: {response.ToolCalls.Count} call(s)");
                            messages.Add(new AssistantChatMessage(response));

                            foreach (var call in response.ToolCalls)
                            {
                                string statusText = call.FunctionName switch
                                {
                                    "get_msg_history" => "📜 *Читаю недавнюю историю чата...*",
                                    "http_get" => "🌐 *Загружаю данные по веб-ссылке...*",
                                    _ => $"⚙️ *Выполняю инструмент:* `{call.FunctionName}`"
                                };

                                try
                                {
                                    await bot.EditMessageText(
                                        chatId: message.Chat.Id,
                                        messageId: loadingMessage.MessageId,
                                        text: statusText,
                                        parseMode: ParseMode.Markdown
                                    );
                                }
                                catch (Exception e)
                                {
                                    Logger.Error($"Error updating toolcall status: {e.Message}");
                                }
                                
                                Logger.Log($"AI tool called: {call.FunctionName} (ID: {call.Id})");
                                string toolResult = await Tools.ExecuteToolAsync(call, GetAllMessages);
                                messages.Add(new ToolChatMessage(call.Id, toolResult));
                            }
                        }
                        else
                        {
                            stopwatch.Stop();
                            typingCts.Cancel();

                            string replyText = response.Content.Count > 0 ? response.Content[0].Text : "";
                            if (!string.IsNullOrWhiteSpace(replyText))
                            {
                                messages.Add(new AssistantChatMessage(replyText));
                            }

                            var deleteMarkup = new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"del:{message.From?.Id ?? 0}")
                            );

                            await SendOrEditTelegramResponse(bot, message.Chat.Id, loadingMessage.MessageId, replyText, "", deleteMarkup);
                            return;
                        }
                    }

                    typingCts.Cancel();
                    await bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: loadingMessage.MessageId,
                        text: "⚠️ Превышен лимит вызова инструментов при генерации ответа."
                    );
                }
                catch (Exception ex)
                {
                    typingCts.Cancel();
                    Logger.Error($"Unhandled error in /ask: {ex.Message}");
                    Console.WriteLine(ex);
                    
                    try
                    {
                        await bot.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: loadingMessage.MessageId,
                            text: $"🚨 Случилась необработанная ошибка: {ex.Message}"
                        );
                    }
                    catch
                    {
                        // Ignore edit failures
                    }
                }
                finally
                {
                    chatLock.Release();
                }
            });

            return;
        } 
        
        // Обычные сообщения сохраняем в очередь для инструмента get_msg_history
        string formattedForHistory = $"{senderName}: {text}";
        lastMessages.Enqueue(formattedForHistory);
        while (lastMessages.Count > MaxMessages)
        {
            lastMessages.Dequeue();
        }
    }

    private static bool IsCommand(string text, string command)
    {
        if (text.Equals(command, StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.StartsWith(command + "@", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static void TrimHistoryIfNeeded()
    {
        // Оставляем системный промпт (индекс 0) и максимум 30 последних сообщений
        if (messages.Count > 35)
        {
            int toRemove = messages.Count - 35;
            messages.RemoveRange(1, toRemove);
        }
    }

    private static async Task SendOrEditTelegramResponse(
        TelegramBotClient bot,
        long chatId,
        int loadingMessageId,
        string responseText,
        string tokenInfo,
        InlineKeyboardMarkup? replyMarkup = null)
    {
        string fullMessage = responseText + tokenInfo;

        if (fullMessage.Length <= 4000)
        {
            try
            {
                await bot.EditMessageText(
                    chatId: chatId,
                    messageId: loadingMessageId,
                    text: fullMessage,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: replyMarkup
                );
            }
            catch (Exception)
            {
                Logger.Warn("AI answer: Markdown parsing failed, falling back to plain text");
                await bot.EditMessageText(
                    chatId: chatId,
                    messageId: loadingMessageId,
                    text: fullMessage,
                    replyMarkup: replyMarkup
                );
            }
            return;
        }

        // Разбивка длинного сообщения (>4000 символов)
        List<string> chunks = SplitMessage(fullMessage, 3900);

        try
        {
            await bot.EditMessageText(
                chatId: chatId,
                messageId: loadingMessageId,
                text: chunks[0],
                parseMode: ParseMode.Markdown
            );
        }
        catch (Exception)
        {
            await bot.EditMessageText(
                chatId: chatId,
                messageId: loadingMessageId,
                text: chunks[0]
            );
        }

        for (int i = 1; i < chunks.Count; i++)
        {
            bool isLast = (i == chunks.Count - 1);
            InlineKeyboardMarkup? currentMarkup = isLast ? replyMarkup : null;

            try
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: chunks[i],
                    parseMode: ParseMode.Markdown,
                    replyMarkup: currentMarkup
                );
            }
            catch (Exception)
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: chunks[i],
                    replyMarkup: currentMarkup
                );
            }
        }
    }

    private static List<string> SplitMessage(string text, int chunkSize)
    {
        List<string> chunks = new();
        string remaining = text;
        while (remaining.Length > 0)
        {
            if (remaining.Length <= chunkSize)
            {
                chunks.Add(remaining);
                break;
            }

            int splitIndex = remaining.LastIndexOf('\n', chunkSize);
            if (splitIndex < 1000)
            {
                splitIndex = chunkSize;
            }

            chunks.Add(remaining.Substring(0, splitIndex));
            remaining = remaining.Substring(splitIndex).TrimStart('\r', '\n');
        }
        return chunks;
    }
    
    private static string GetAllMessages()
    {
        return string.Join("\n", lastMessages);
    }
}
