using System.Text.Json;
using OpenAI.Chat;

namespace NeuroBotForNG;

public static class Tools
{
    public static void RunTool(ChatToolCall call, List<ChatMessage> messages, ChatCompletion response)
    {
        switch (call.FunctionName)
        {
            case nameof(http_get):
            {
                using JsonDocument argumentsJson = JsonDocument.Parse(call.FunctionArguments);
                bool hasLocation = argumentsJson.RootElement.TryGetProperty("url", out JsonElement url);

                if (!hasLocation)
                {
                    throw new ArgumentNullException(nameof(url), "The url argument is required.");
                }

                string toolResult = http_get(url.ToString());
                messages.Add(new AssistantChatMessage(response));
                messages.Add(new ToolChatMessage(call.Id, toolResult));
                break;
            }
        }
    }

    public static ChatCompletionOptions GetTools()
    {
        ChatTool getMsgHistory = ChatTool.CreateFunctionTool(
            functionName: "get_msg_history",
            functionDescription: "Возвращает историю сообщений из группы",
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

    private static readonly HttpClient client = new HttpClient();

    private static string http_get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Псевдо-браузерные заголовки
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

        request.Headers.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

        request.Headers.TryAddWithoutValidation("Accept-Language",
            "en-US,en;q=0.9,ru;q=0.8");

        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var response = client.Send(request);

        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStringAsync().Result;
    }
}