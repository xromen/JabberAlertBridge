# JabberAlertBridge

Сервис принимает webhook Grafana Alerting и Prometheus Alertmanager, сохраняет уведомления в PostgreSQL и отправляет личные XMPP-сообщения пользователям из настроенного списка JID. Один исполняемый проект на .NET 10; HTTP работает на порту **8080**, XMPP по умолчанию — **5222**.

По согласованным требованиям проверка XMPP-сертификатов **отключена** (`Xmpp:ValidateCertificate=false`), STARTTLS включён. Тестовые проекты и автотесты не добавлены.

## Возможности

- Единый HTTP endpoint для Grafana и Alertmanager с Basic Auth и проверкой входных данных.
- Постоянная очередь PostgreSQL: ответ `202` возвращается после сохранения всех частей уведомления.
- Русские сообщения о проблемах и восстановлении, поддержка групп и разделение длинного текста.
- Отдельная очередь для каждого адресата, сохранение порядка и независимые повторные попытки.
- Подтверждение обработки сообщения XMPP-сервером по XEP-0198 Stream Management.
- Автоматическое переподключение к Jabber, health endpoints, метрики и готовые правила Prometheus.
- Docker Compose для локального запуска и подключения к существующему Dockprom.

Поток данных: **Grafana / Alertmanager → HTTP API → PostgreSQL → фоновый обработчик → личные XMPP-сообщения → Spark**.

## Структура репозитория

```text
src/JabberAlertBridge/   Приложение, конфигурация, очередь и миграции EF Core
deploy/                 Интеграция с Dockprom, мониторинг и SQL для администратора
examples/               Примеры webhook Grafana и Alertmanager
compose.yml             Локальный запуск с PostgreSQL
Dockerfile              Сборка и запуск контейнера приложения
.env.example            Образец переменных окружения без рабочих секретов
.editorconfig           Общие правила форматирования и именования C#
```

Начните с [.env.example](.env.example) и инструкции запуска ниже. Примеры запросов находятся в [examples](examples/), файлы развёртывания — в [deploy](deploy/).

## Быстрый запуск в Docker

Нужны Docker Engine с Linux-контейнерами и Docker Compose v2, доступ к Jabber, отдельный аккаунт бота и JID получателей. По умолчанию XMPP-сервер должен поддерживать XEP-0198. Конференция и права на вход в комнату не нужны.

1. Скопируйте `.env.example` в `.env`, замените `CHANGE_ME` и задайте параметры XMPP. Пароль локальной PostgreSQL не должен содержать `;`: он включается в строку подключения. Для более сложных паролей задайте полную корректно экранированную строку подключения в своей Compose-конфигурации.
2. Соберите образ, запустите PostgreSQL и отдельно примените миграции:

   ```sh
   docker compose build
   docker compose up -d postgres
   docker compose run --rm migrate
   docker compose up -d jabber-alert-bridge
   ```

3. Проверьте `http://localhost:8080/health/ready` и `http://localhost:8080/health/xmpp`.

В локальном Compose HTTP-порт опубликован только на `127.0.0.1`. PostgreSQL хранит данные в именованном volume и не публикует порт. Обычный запуск приложения **не применяет миграции**.

## Существующий Dockprom

`deploy/compose.dockprom.yml` — самостоятельный Compose-файл, подключающий сервис к существующей внешней сети. Укажите настоящее Docker-имя сети в `JABBER_ALERT_DOCKER_NETWORK` и существующую PostgreSQL в `JABBER_ALERT_POSTGRES_CONNECTION_STRING`.

```sh
docker compose --env-file .env -f deploy/compose.dockprom.yml build
docker compose --env-file .env -f deploy/compose.dockprom.yml run --rm jabber-alert-bridge --migrate
docker compose --env-file .env -f deploy/compose.dockprom.yml up -d
```

Этот вариант не публикует HTTP-порт на хост. Адрес внутри общей сети:

```text
http://jabber-alert-bridge:8080/api/webhooks/alerts
```

PostgreSQL может работать в любом месте, доступном из контейнера. Для корпоративных имён обеспечьте правильный DNS в Docker. Роль для миграций должна иметь права на создание схемы; рабочей роли достаточно DML-прав на таблицу и доступа к её последовательности. Автоматическое создание самой базы не выполняется.

## Запуск без Docker

Нужны .NET SDK 10 и PostgreSQL с заранее созданной базой. Задайте переменные окружения, например в PowerShell:

```powershell
$env:ConnectionStrings__Postgres = 'Host=localhost;Database=jabber_alert_bridge;Username=jabber_bridge;Password=your-password;Timeout=5;Command Timeout=10'
$env:Xmpp__Host = 'jabber.company.local'
$env:Xmpp__Jid = 'monitoring@company.local'
$env:Xmpp__Password = 'your-xmpp-password'
$env:Xmpp__RecipientJids = 'user1@company.local,user2@company.local'
$env:WebhookAuth__Password = 'your-http-password'
dotnet restore
dotnet run --project src/JabberAlertBridge --no-launch-profile -- --migrate
dotnet run --project src/JabberAlertBridge --no-launch-profile
```

Режим `--migrate` использует только настройки БД и завершает процесс после миграций. Альтернатива — проверенный администратором идемпотентный `deploy/sql/migrations.sql`.

## Конфигурация

Основные значения находятся в `src/JabberAlertBridge/appsettings.json`. Все параметры можно переопределить через переменные окружения .NET: `Xmpp__Host`, `Delivery__MaxPendingMessages` и т. д. Для смены HTTP-порта задайте `URLS=http://0.0.0.0:8081`. Изменения требуют перезапуска.

| Группа | Параметры и значения по умолчанию |
| --- | --- |
| `ConnectionStrings` | `Postgres` — обязательная строка подключения |
| `WebhookAuth` | `Username=monitoring`, обязательный `Password` |
| `Xmpp` | `Host`, `Port=5222`, `Jid`, `Password`, непустой список `RecipientJids` |
| `Xmpp` | `RequireTls=true`, `ValidateCertificate=false`, `RequireServerAcknowledgement=true` |
| `Xmpp` | `ConnectTimeoutSeconds=15`, `PingIntervalSeconds=30` |
| `Delivery` | `PollIntervalSeconds=2`, `SendTimeoutSeconds=20`, `LeaseDurationSeconds=60` |
| `Delivery` | `RetryInitialSeconds=5`, `RetryMaxSeconds=300`, `MaxAttempts=0` — без ограничения временных ошибок |
| `Delivery` | `MaxPendingMessages=5000`, `MessageRetentionDays=14`, `DeadRetentionDays=90` |
| `AlertFormatting` | `TimeZoneId=Asia/Vladivostok`, `MaxMessageLength=4000`, `MaxAlertsPerRequest=100`, `MaxRequestBodyBytes=262144` |
| `AlertFormatting` | `IncludeLinks=true`, `SendResolved=true` |

`Host` — сетевой адрес XMPP-сервера, `Jid` — адрес аккаунта бота вида `user@domain` без ресурса. Они могут иметь разные домены. SASL-аутентификация при `RequireTls=true` не начинается, пока соединение не защищено TLS.

В `appsettings.json` адресаты задаются массивом:

```json
"Xmpp": {
  "RecipientJids": [
    "user1@company.local",
    "user2@company.local"
  ]
}
```

Для Compose укажите в `.env` строку `JABBER_ALERT_XMPP_RECIPIENT_JIDS=user1@company.local,user2@company.local`. Приложение принимает `Xmpp__RecipientJids` как список через запятую; непустая строка имеет приоритет над массивом. Поддерживаются и стандартные переменные `Xmpp__RecipientJids__0`, `Xmpp__RecipientJids__1` и т. д. Пробелы вокруг адресов удаляются, повторы без учёта регистра исключаются. Пустой итоговый список или некорректный JID останавливает запуск.

Получатели берутся только из конфигурации. Поля адресатов, реквизитов и комнаты из webhook игнорируются. Список не меняется динамически; требуется перезапуск.

Секреты можно передавать файлами, в том числе через Docker Secrets:

```text
ConnectionStrings__PostgresFile=/run/secrets/postgres-connection
WebhookAuth__PasswordFile=/run/secrets/webhook-password
Xmpp__PasswordFile=/run/secrets/xmpp-password
```

Файлы нужно смонтировать в контейнер. Значение из файла имеет приоритет над обычным значением соответствующего параметра; завершающие переводы строки удаляются. `.env`, каталог `secrets/` и локальные артефакты исключены из Git и Docker build context.

## HTTP API

`POST /api/webhooks/alerts`, `Content-Type: application/json`, HTTP Basic Auth.

```sh
curl --user monitoring --header 'Content-Type: application/json' \
  --data-binary @examples/grafana-firing.json \
  http://localhost:8080/api/webhooks/alerts
```

`curl` запросит пароль интерактивно. Другой пример — `examples/alertmanager-mixed.json` с проблемой и восстановлением.

```json
{
  "status": "queued",
  "messageIds": [
    "a40ac7dd-d793-4b0b-8f7e-47cd2f8ab824",
    "c07e836a-f53c-4a69-875a-df5c646b00ad"
  ],
  "queuedCount": 2
}
```

| Код | Значение |
| --- | --- |
| `202` | Все части сохранены одной транзакцией; это ещё не подтверждение доставки |
| `400` | Некорректный JSON, поля или число алертов |
| `401` | Не пройдена Basic Auth |
| `413` | Превышен размер тела, в том числе при chunked-запросе |
| `415` | Неверный Content-Type |
| `429` | Недостаточно места для всего запроса; `Retry-After: 30` |
| `503` | Ошибка сохранения в PostgreSQL |

У каждого алерта обязательны `status` (`firing` или `resolved`) и непустой `labels.alertname`. Ошибка одного элемента отклоняет весь запрос. Отсутствующие описание, экземпляр и время допустимы. Минимальная дата `0001-01-01` трактуется как отсутствующая. Неизвестные поля игнорируются. Стандартные тестовые уведомления Grafana обрабатываются как обычные алерты.

`messageIds` содержит идентификаторы отдельных отправок каждому пользователю. Две части для трёх пользователей создают шесть записей; `queuedCount` равен шести. Весь набор сохраняется одной транзакцией либо отклоняется целиком, если недостаточно места.

Источник распознаётся по полям Grafana, затем по `version` Alertmanager, иначе записывается `webhook`. Повторы намеренно не устраняются. Когда `SendResolved=false` и все элементы отфильтрованы, ответ — `202` с пустым списком и `queuedCount=0`.

Время в БД — UTC, в сообщениях — выбранный часовой пояс. Выводятся только предусмотренные поля: название, severity, instance, service/job, environment/env, summary, description и время. Для восстановлений добавляются окончание и длительность. Ссылки выбираются в порядке panel → dashboard → generator; только HTTP(S), до 1024 символов. Смешанные группы содержат сначала проблемы, затем восстановления. Длинные сообщения делятся на пронумерованные части; границы алертов сохраняются, если отдельный алерт помещается в лимит.

## Гарантии и восстановление

- Для каждого пользователя отправляется stanza `type='chat'` на bare JID, без привязки к ресурсу конкретного клиента.
- По умолчанию запись переходит в `sent` после подтверждения обработки stanza сервером по [XEP-0198](https://xmpp.org/extensions/xep-0198.html). Отсутствие подтверждения вызывает повтор. Это не подтверждение получения или прочтения сотрудником: дальнейшая маршрутизация, ограничения пользователей и offline-хранилище зависят от Jabber-сервера. Поздние ошибки маршрутизации видны в логах; после серверного подтверждения запись автоматически не возвращается в очередь.
- Если сервер не поддерживает XEP-0198, XMPP остаётся неготовым с ошибкой `stream-management-required`, а сообщения хранятся в очереди. Для совместимости можно явно задать `Xmpp__RequireServerAcknowledgement=false`: тогда на сервере без XEP-0198 `sent` означает только завершение записи в сокет и возможна потеря сообщения при неоднозначном обрыве. На сервере с XEP-0198 подтверждения используются и при этой настройке.
- Доставка — **at-least-once**. При сбое после публикации и до фиксации `sent` возможен дубль. Прочтение сотрудниками не подтверждается.
- Порядок сохраняется отдельно для каждого получателя: `resolved` не обгоняет его ожидающий `firing`. Задержавшаяся отправка одному пользователю не блокирует остальных. Уже успешные отправки не повторяются из-за ошибки другого адресата.
- Сетевая отправка выполняется вне транзакции БД. Атомарный захват использует `FOR UPDATE SKIP LOCKED` и lease. Завершение проверяет владельца и номер попытки; после аварии истёкшую аренду можно захватить снова.
- Нет готового XMPP-соединения — нет захвата сообщений и расходования попыток. Неверный пароль и ошибка настройки не переводят всю очередь в `dead`; подключения повторяются с задержкой до 300 секунд. При штатной остановке бот публикует недоступность и закрывает XML-поток.
- Ошибки конкретного сообщения `bad-request`, `jid-malformed`, `not-acceptable`, `policy-violation`, а также достижение заданного `MaxAttempts` переводят запись в `dead`. Ошибки хранятся кодами, без текста payload или секретов.
- Активная очередь включает `pending` и `processing`. Глобальная транзакционная advisory-блокировка сериализует постановку и проверку вместимости.
- Все накопленные сообщения доставляются независимо от возраста. История `sent` и `dead` очищается ежечасно по срокам хранения.
- Адрес пользователя сохраняется в записи очереди. Изменение списка влияет на новые webhook; уже принятые сообщения сохраняют прежних адресатов.
- Запускайте **один экземпляр** приложения. Масштабирование XMPP-сессий не предусмотрено.

Для просмотра проблемных записей:

```sql
SELECT id, sequence, recipient_jid, status, attempts, created_at, next_attempt_at, last_error
FROM outgoing_messages
WHERE status IN ('pending', 'processing', 'dead')
ORDER BY sequence;
```

После устранения причины администратор может выполнить:

```sh
psql "$DATABASE_URL" -v message_id='UUID' -v capacity=5000 -f deploy/sql/requeue-dead.sql
```

Передайте актуальный `MaxPendingMessages` в `capacity`. Скрипт возвращает только выбранную `dead`-запись с проверкой вместимости под той же advisory-блокировкой. Пустой результат означает, что запись не подходит или очередь заполнена. Повторная постановка сохраняет исходное место в порядке доставки этому пользователю.

### Обновление с версии для конференции

Остановите прежний экземпляр, сделайте резервную копию БД, замените `Xmpp:RoomJid` на `Xmpp:RecipientJids` и примените миграции перед запуском новой версии.

Миграция `DirectUserRecipients` переименовывает `room_jid` в `recipient_jid`, сохраняя историю. Прежние `pending` и `processing` переводятся в `dead`; у всех старых неотправленных записей сохраняется отметка `legacy-room-message:`. Они не перенаправляются новым пользователям автоматически и исключены из обычного SQL-повтора. Администратор может отдельно выбрать конкретного пользователя и осознанно повторно поставить нужную запись после проверки содержимого. Старые `sent` остаются историей отправок в комнату.

## Мониторинг и источники

| Endpoint | Значение |
| --- | --- |
| `/health/live` | Процесс работает, без проверки внешних зависимостей |
| `/health/ready` | Доступна таблица очереди PostgreSQL, разрешена запись |
| `/health/xmpp` | Бот авторизован и готов отправлять личные сообщения с выбранной политикой подтверждения |
| `/metrics` | Метрики Prometheus |

Health и метрики не требуют Basic Auth: ограничьте доступ к ним внутренней сетью. Readiness не зависит от XMPP и заполнения очереди. XMPP-сессия проверяется периодическим ping сервера; при обрыве выполняется новое подключение.

Добавьте scrape-конфигурацию из `deploy/prometheus/scrape.yml` и подключите `deploy/prometheus/alerts.yml` через `rule_files`. Подготовлены алерты: недоступность самого сервиса, XMPP более 5 минут, ошибка БД, ожидание более 10 минут, заполнение более 80% и наличие `dead`. Направляйте эти аварии также в независимый от моста канал.

В Grafana создайте Contact point типа Webhook с адресом сервиса, Basic Auth и стандартным payload, затем подключите Notification policy. Для Alertmanager используйте фрагмент `deploy/alertmanager/receiver.yml`; указанный файл пароля должен быть реально примонтирован. Не направляйте один алерт двумя путями одновременно, если дубли не нужны.

Логи выводятся в stdout как JSON: идентификатор запроса/сообщения, статусы, количество, длительность, коды ошибок. Полное тело webhook, пароли и Authorization не логируются. stdout можно собирать существующим агентом Loki.

Резервное копирование PostgreSQL и проверка восстановления выполняются средствами администратора. Сохраните БД перед обновлением; для работающих экземпляров применяйте миграции контролируемо. Пример локальной резервной копии:

```sh
docker compose exec -T postgres pg_dump -U jabber_bridge -d jabber_alert_bridge -Fc > bridge.dump
```

Для бинарного дампа используйте оболочку, сохраняющую бинарный stdout, например Bash или PowerShell 7.4+.

## Разработка и стиль

`.editorconfig` основан на [рекомендациях Microsoft](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions) и [стиле dotnet/runtime](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md): четыре пробела, UTF-8, LF, фигурные скобки Allman, пространства имён уровня файла, PascalCase для типов и публичных членов, `_camelCase` для закрытых полей, префикс `I` у интерфейсов. `var` используется, когда тип очевиден. Логические блоки разделяются пустыми строками; длинные сигнатуры и вызовы переносятся по аргументам, ориентир длины строки — 120 символов.

Проверка стиля включена при сборке, предупреждения считаются ошибками. Для полного форматирования и проверки:

```sh
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
```

Автотесты не создаются по требованию заказчика. Для изменения схемы:

```sh
dotnet tool restore
dotnet ef migrations add Name --project src/JabberAlertBridge --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations script --idempotent --project src/JabberAlertBridge --output deploy/sql/migrations.sql
```

Коммиты оформляются на русском языке в прошедшем времени: краткий информативный заголовок и пункты с выполненными изменениями в теле.
