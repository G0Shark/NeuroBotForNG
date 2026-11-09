using System.Text.Json;
using OpenAI.Chat;

namespace NeuroBotForNG;

public static class Tools
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task<string> ExecuteToolAsync(ChatToolCall call, Func<string> getHistory)
    {
        try
        {
            switch (call.FunctionName)
            {
                case "get_msg_history":
                {
                    return getHistory();
                }
                case nameof(http_get):
                {
                    using JsonDocument argumentsJson = JsonDocument.Parse(call.FunctionArguments);
                    if (!argumentsJson.RootElement.TryGetProperty("url", out JsonElement url))
                    {
                        return "⛔ Ошибка: отсутствует аргумент 'url'.";
                    }

                    return await http_get(url.GetString() ?? url.ToString());
                }
                default:
                    return $"⛔ Неизвестная функция: {call.FunctionName}";
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Tool '{call.FunctionName}' execution error: {ex.Message}");
            return $"⛔ Ошибка выполнения инструмента {call.FunctionName}: {ex.Message}";
        }
    }

    public static ChatCompletionOptions GetTools()
    {
        ChatTool getMsgHistory = ChatTool.CreateFunctionTool(
            functionName: "get_msg_history",
            functionDescription: "Возвращает недавнюю историю сообщений из группы",
            functionParameters: BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {}
                }
                """)
        );
        
        ChatTool httpGet = ChatTool.CreateFunctionTool(
            functionName: nameof(http_get),
            functionDescription: "Воспроизвести GET для ссылки под видом браузера и получить ответ",
            functionParameters: BinaryData.FromBytes("""
                {
                    "type": "object",
                    "properties": {
                        "url": {
                            "type": "string",
                            "description": "Точная ссылка на сайт"
                        }
                    },
                    "required": [ "url" ]
                }
                """u8.ToArray())
        );

        return new ChatCompletionOptions()
        {
            Tools = { getMsgHistory, httpGet }
        };
    }

    private static async Task<string> http_get(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Псевдо-браузерные заголовки
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language",
                "en-US,en;q=0.9,ru;q=0.8");
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync();
            // Ограничение размера контента, чтобы не переполнять контекст модели
            if (content.Length > 15000)
            {
                content = content.Substring(0, 15000) + "\n\n...[Ответ обрезан из-за превышения лимита длины]";
            }
            return content;
        }
        catch (Exception ex)
        {
            Logger.Error($"HTTP GET error ({url}): {ex.Message}");
            return $"⛔ Ошибка HTTP GET ({url}): {ex.Message}";
        }
    }
}
