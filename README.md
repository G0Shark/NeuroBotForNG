# NeuroBotForNG
### Простой бот для:
1. Транскрипция сообщений (Группа и ЛС) - **Исполльзую API от Groq**
2. Нейропомощник на базе OpenAI API - **Использую MIMO Token Plan**

### Самостоятельно имплементировал в него парочку инструментов:
1. Получение истории сообщений с группы
2. Выполнение HTTP GET на URL
3. Выполнение любой команды по RCON на Minecraft сервере

При необходимости имплементируйте свои инструменты в файле `Tools.cs`

---
# Build
1. Скопируйте репозиторий
2. Забилдите проект ``dotnet build && dotnet run``

---
# Usage
### Вы можете использовать docker образ.
```yml
services:
  bot:
    image: ghcr.io/g0shark/neurobotforng:latest
    container_name: neurobotforng
    restart: no
    env_file:
      - .env
    volumes:
      - ./system_prompt.txt:/app/system_prompt.txt:ro
```

### Программа так же требует .env
```env
TG_BOT_TOKEN=
GROQ_TOKEN=
OPENAI_API_KEY=
OPENAI_BASE_URL=
GROUP_ID=
MSNG_IP_ADDRESS=
MSNG_RCON_PASSWORD=
```

либо скопируйте .env.example - в нём уже всё заготовленно

### После - создайте system_prompt.txt, или используйте мой
Создайте файл `system_prompt.txt` рядом с docker-compose файлом. В нём можете писать любую информацию касаемо системного промпта для нейросети. При желаниии можете использовать мой.
```bash
nano system_prompt.txt
```

**Добавьте бота в группу и дайте ему права администратора - только с ними он сможет считывать сообщения с группы. После вы можете использовать `/ask` с запросом - нейросеть даст ответ**