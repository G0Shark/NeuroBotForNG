﻿using System.Text.RegularExpressions;
 using CodexNet;
 using DotNetEnv;
using Groq;
using GroqSharp.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = Telegram.Bot.Types.Message;
using GroqClient = Groq.GroqClient;
using Thread = CodexNet.Thread;

class Program
{
    private static TelegramBotClient bot;
    private static Queue<string> lastMessages = new Queue<string>();
    private const int MaxMessages = 500;
    static Thread codexThread;
    static async Task Main(string[] args)
    {
        Env.Load();
        
        var codex = new Codex(new CodexOptions
        {
            CodexPathOverride = Environment.GetEnvironmentVariable("CODEX_PATH")!
        });
        codexThread = codex.StartThread(new ThreadOptions
        {
            SkipGitRepoCheck = true,
            Model = "gpt-5.5"
        });
        
        Console.WriteLine("warmup");
        
        await codexThread.RunAsync(File.ReadAllText("./system_prompt.txt"));
        
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
            await bot.SendMessage(
                chatId: message.Chat,
                text: "⛔ *Я работаю только в группе NewGame!*\nЧтобы использовать меня - используйте /ask в *группе*",
                parseMode: ParseMode.Markdown,
                replyParameters: new ReplyParameters
                {
                    MessageId = message.MessageId
                }
            );
            
            return;
        }

        if (message.Text.StartsWith("/reset"))
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
        }
        
        if (message.Text.StartsWith("/ask"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var loadingMessage = await bot.SendMessage(
                        chatId: message.Chat,
                        text: "⌛ *Генерирую ответ...*",
                        parseMode: ParseMode.Markdown,
                        replyParameters: new ReplyParameters
                        {
                            MessageId = message.MessageId
                        }
                    );

                    var turn = await codexThread.RunAsync(message.From.Username + ": " + message.Text);

                    if (turn.FinalResponse.Contains("GET_HISTORY"))
                    {
                        await bot.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: loadingMessage.MessageId,
                            text: "⌛ *Потребовалась история сообщений, скоро отвечу...*",
                            parseMode: ParseMode.Markdown
                        );

                        turn = await codexThread.RunAsync("Сводка последних сообщений: \n" + GetAllMessages());
                    }

                    if (turn.FinalResponse.Contains("READ_MEMORY"))
                    {
                        await bot.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: loadingMessage.MessageId,
                            text: "⌛ *Потребовалась прочитать память, скоро отвечу...*",
                            parseMode: ParseMode.Markdown
                        );

                        turn = await codexThread.RunAsync("Долгосрочная память: \n" + File.ReadAllText("./memory.txt"));
                    }
                    
                    if (turn.FinalResponse.Contains("WRITE_MEMORY"))
                    {
                        await bot.EditMessageText(
                            chatId: message.Chat.Id,
                            messageId: loadingMessage.MessageId,
                            text: "✏️ *Добавление заметок в память...*",
                            parseMode: ParseMode.Markdown
                        );

                        File.AppendAllText("./memory.txt", "\n" + turn.FinalResponse.Substring(12));
                        
                        turn = await codexThread.RunAsync("Память обновлена, нынешняя память: \n" + File.ReadAllText("./memory.txt"));
                    }

                    await bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: loadingMessage.MessageId,
                        text: turn.FinalResponse,
                        parseMode: ParseMode.Markdown
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
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
    
    private static string GetAllMessages()
    {
        return string.Join("\n", lastMessages);
    }
}
