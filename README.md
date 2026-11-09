# NeuroBotForNG
### Простой бот для:
1. Транскрипция голосовых сообщений (Группа и ЛС) — **API Groq (Whisper Large V3)**
2. Нейропомощник и ответы на вопросы — **OpenAI API (или любой совместимый провайдер: OpenRouter, MIMO и др.)**

### Команды бота:
- `/ask <вопрос>` — задать вопрос нейросети (или ответить `Reply` на любое сообщение, чтобы проанализировать его)
- `/reset` (или `/clear`) — сбросить контекст диалога бота (доступно только владельцу)
- `/status` — посмотреть текущую модель, статистику памяти и провайдера
- `/help` — краткая справка по возможностям бота
- 🎤 **Голосовые сообщения** — отправьте голосовое в чат или в ЛС боту для мгновенной расшифровки через Groq Whisper

### Встроенные инструменты (Tool Calling):
1. Получение истории сообщений из группы (`get_msg_history`)
2. Выполнение HTTP GET на URL под видом браузера (`http_get`)

При необходимости добавляйте свои инструменты в файле `Tools.cs`.

---
# Build
1. Клонируйте репозиторий
2. Запустите сборку и проект:
```bash
dotnet build && dotnet run --project NeuroBotForNG
```

---
# Usage
### Запуск через Docker
```yml
services:
  bot:
    image: ghcr.io/g0shark/neurobotforng:latest
    container_name: neurobotforng
    restart: unless-stopped
    env_file:
      - .env
    volumes:
      - ./system_prompt.txt:/app/system_prompt.txt:ro
```

### Конфигурация .env
Скопируйте `.env.example` в `.env`:
```env
# Telegram
TG_BOT_TOKEN=
GROUP_ID=
ADMIN_ID=5061499376

# OpenAI API (для текстового чата /ask)
OPENAI_API_KEY=
# OPENAI_BASE_URL=https://api.openai.com/v1
# OPENAI_MODEL=gpt-4o-mini

# Groq API (для транскрипции голосовых через Whisper)
GROQ_API_KEY=
```

### Системный промпт
Создайте файл `system_prompt.txt` рядом с исполняемым файлом или docker-compose. В нем можно задать поведение, правила форматирования и контекст для бота.

```bash
nano system_prompt.txt
```

**Добавьте бота в группу и выдайте права администратора (для чтения всех сообщений). После этого используйте команду `/ask <вопрос>` в группе — бот ответит с использованием нейросети и инструментов.**
