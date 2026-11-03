# Notification Service

Учебный backend-проект на C# / .NET 10 для асинхронной доставки уведомлений.
Разрабатываем постепенно: перед новым компонентом разбираем концепции,
после реализации проверяем поведение и понимание.

## Текущее состояние

Реализованы минимальный ASP.NET Core API, /health, ошибки Problem Details,
модель Notification с валидацией и переходами статусов, интеграционные и unit-тесты.
Отправка уведомлений, БД, очередь и Worker пока не реализованы.

.NET SDK 10.0.403 установлен пользователем. Проверка Release-сборки:
29 тестов прошли, ошибок нет. Подробности модели и практика —
[docs/02-notification-model.md](docs/02-notification-model.md).

Для запуска команд без PowerShell-скрипта и независимо от PATH:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build Notification-Service.slnx --no-restore
& "C:\Program Files\dotnet\dotnet.exe" test Notification-Service.slnx --no-build
& "C:\Program Files\dotnet\dotnet.exe" run --project src/NotificationService.Api --no-build
```

Перед пересборкой Debug остановите запущенный API через Ctrl+C. Для проверки
без остановки API используйте отдельную конфигурацию:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test Notification-Service.slnx --no-restore -c Release
```

Установку инструментов и скачивание зависимостей выполняет пользователь.

## Запуск в Windows / VS Code

Открыть эту папку в VS Code. Рекомендуемые расширения указаны в `.vscode/extensions.json`.
Нужен .NET 10 **SDK**, а не только Runtime.

Если SDK отсутствует, установить его по
[официальной инструкции](https://learn.microsoft.com/dotnet/core/install/windows)
или локально из PowerShell в корне проекта:

```powershell
New-Item -ItemType Directory -Force .tools | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile .tools/dotnet-install.ps1
& ./.tools/dotnet-install.ps1 -Channel 10.0 -Quality GA -InstallDir "$PWD/.dotnet" -NoPath
```

Скрипт-обёртка использует локальный SDK, а при его отсутствии — системный.
SDK, кеш NuGet и временные файлы исключены из Git.

```powershell
./scripts/dotnet.ps1 --info
./scripts/dotnet.ps1 restore Notification-Service.slnx
./scripts/dotnet.ps1 build Notification-Service.slnx --no-restore
./scripts/dotnet.ps1 test Notification-Service.slnx --no-build
./scripts/dotnet.ps1 run --project src/NotificationService.Api
```

Открыть http://localhost:5080/health — ожидается `200 OK` и `Healthy`.
Остановить сервер: Ctrl+C. В VS Code команды также доступны через
Terminal → Run Task → `build`, `test`, `run API`; Ctrl+Shift+B запускает сборку.
Файл `NotificationService.Api.http` содержит запросы для расширения REST Client.

## Как устроен первый компонент

- `Notification-Service.slnx` объединяет проекты приложения и тестов.
- `NotificationService.Api.csproj` задаёт Web SDK, .NET 10 и проверку nullable-ссылок.
- `Program.cs` — точка входа. `CreateBuilder` загружает конфигурацию и готовит DI-контейнер.
- `builder.Services` регистрирует зависимости; `Build` создаёт приложение.
- `AddHealthChecks` регистрирует механизм проверки состояния, `MapHealthChecks` публикует его по HTTP.
  Пока нет проверок зависимостей: `Healthy` не означает готовность базы или брокера.
- `AddProblemDetails`, `UseExceptionHandler` и `UseStatusCodePages` обеспечивают стандартное
  представление ошибок. Middleware обрабатывает HTTP-запросы в порядке регистрации.
- `Run` запускает HTTP-сервер. `public partial class Program` делает точку входа доступной тестам.
- `ApiTests` использует `WebApplicationFactory<Program>` для запуска настоящего HTTP-конвейера
  в памяти. `IClassFixture` управляет временем жизни фабрики; `Task` и `await` позволяют
  дождаться ответа без блокировки потока. `using` освобождает ресурсы.

Пока не нужны собственные интерфейсы, репозитории и дополнительные архитектурные слои:
добавим их при появлении конкретных обязанностей и зависимостей.

## Практика первого этапа

1. Запустите API и оба теста. Объясните разницу между SDK и Runtime.
2. Объясните, чем регистрация `AddHealthChecks` отличается от `MapHealthChecks`.
3. Выполните запрос на неизвестный адрес. Найдите HTTP-код и поле `status` в JSON.
4. Добавьте `GET /api/info`, возвращающий JSON с именем сервиса, и тест ответа `200`.
5. Объясните, почему успешный `/health` пока не подтверждает возможность отправки email.

Пришлите ответы и изменения для проверки перед следующим компонентом.

## Последовательность дальнейшей разработки

1. Основа .NET и HTTP: текущий каркас, упражнения C#, DI, Task и async/await.
2. Контракт уведомления: DTO, валидация, модель, статусы и тесты переходов.
3. PostgreSQL / EF Core: миграции, создание и чтение, фильтры, пагинация, история попыток.
4. RabbitMQ: exchanges, queues, publisher confirms, manual acknowledgements и DLQ.
5. Отдельный Worker, SMTP Email Provider и Mailpit.
6. Transactional Outbox, конкурентная обработка, Idempotency-Key и отложенные retry.
7. Redis: ограничение частоты запросов, обоснованное кеширование; Docker Compose.
8. Интеграционные сценарии отказов, Serilog, OpenAPI, проверки зависимостей и документация.

Каждый этап: объяснение → небольшой компонент → тесты → осмысленный Git-коммит → практика.

## Целевой контракт

- `POST /api/notifications` — recipient, subject, message, channel; заголовок `Idempotency-Key`.
- `GET /api/notifications/{id}` — состояние уведомления.
- `GET /api/notifications?status=Failed&recipient=...&page=1&pageSize=20` — список.
- `GET /api/notifications/{id}/attempts` — история доставки.
- `POST /api/notifications/{id}/retry` — повтор после окончательной ошибки.

Целевые таблицы: Notifications, DeliveryAttempts, OutboxMessages.
Состояния: Pending, Queued, Processing, Sent, Failed.
Детальные правила переходов и ручного retry определим перед реализацией модели.

## Решения, которые нужно учесть при реализации надёжности

Создание уведомления и Outbox-события должно происходить в одной транзакции PostgreSQL.
Публикация — только через Outbox dispatcher; сообщение может попасть в RabbitMQ повторно.
Нужны publisher confirms, устойчивые очереди и сообщения, ручные acknowledgements,
защита от конкурентной обработки, ограниченные попытки с экспоненциальной задержкой и DLQ.

Уникальное ограничение БД защищает Idempotency-Key при одновременных запросах.
Одинаковый ключ с другим содержимым должен давать конфликт, а не тихо возвращать чужой результат.

SMTP не гарантирует exactly-once: если отправка завершилась, а процесс упал до записи
результата в БД, повтор может доставить письмо ещё раз. Защита Worker от дубликатов
уменьшает риск, но не устраняет это окно. Не будем обещать абсолютную однократность доставки.

В интеграционных тестах следующих этапов проверим сбои БД, брокера и SMTP,
повторную доставку, параллельные запросы и восстановление после падения Worker.
