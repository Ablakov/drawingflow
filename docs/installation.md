# Установка и настройка

## 1. Требования

| Компонент | Требование |
|---|---|
| Сервер | Windows Server 2019+ или Linux (x64), .NET 10 Runtime (ASP.NET Core), PostgreSQL 14+ (рекомендуется 16) |
| Desktop | Windows 10/11 x64, .NET 10 Desktop Runtime (или self-contained публикация) |
| Agent | Windows 10/11 или Windows Server, .NET 10 Runtime (этап 3) |
| Сборка | .NET SDK 10.0.4xx; WPF-проект собирается и на macOS/Linux (`EnableWindowsTargeting`), запускается только на Windows |

## 2. PostgreSQL

1. Создайте роль и базу с кодировкой UTF-8 и локалью, поддерживающей кириллицу (поиск и сортировка используют `lower()` СУБД):
   ```sql
   CREATE ROLE drawingflow WITH LOGIN PASSWORD '<надёжный пароль>';
   CREATE DATABASE drawingflow OWNER drawingflow ENCODING 'UTF8' LC_COLLATE 'ru_RU.UTF-8' LC_CTYPE 'ru_RU.UTF-8' TEMPLATE template0;
   ```
   На Windows используйте локаль `Russian_Russia.1251`/`ru-RU` с `ENCODING 'UTF8'`; на Linux допустима `C.UTF-8`.
2. В `pg_hba.conf` разрешите подключение только с адреса сервера приложений; `listen_addresses` — внутренний интерфейс.
3. Не выдавайте учётные данные БД рабочим станциям.

## 3. Сервер DrawingFlow

### 3.1. Публикация
```bash
dotnet publish src/DrawingFlow.Server -c Release -o /opt/drawingflow/server
```

### 3.2. Конфигурация
Параметры задаются в `appsettings.Production.json` или переменными окружения (`Раздел__Ключ`). Минимальный набор:

| Параметр | Переменная окружения | Описание |
|---|---|---|
| `ConnectionStrings:DrawingFlow` | `ConnectionStrings__DrawingFlow` | `Host=…;Port=5432;Database=drawingflow;Username=drawingflow;Password=…` |
| `Jwt:SigningKey` | `Jwt__SigningKey` | Случайная строка ≥ 32 символов (например, `openssl rand -base64 48`) |
| `Jwt:AccessTokenLifetimeMinutes`, `Jwt:RefreshTokenLifetimeDays` | | По умолчанию 60 и 30 |
| `Bootstrap:Administrator:Login` / `Password` / `DisplayName` | `Bootstrap__Administrator__*` | Только для первого запуска при пустой базе |
| `Database:MigrateOnStartup` | | `false` в production (по умолчанию) |
| `RateLimits:LoginPerMinute` | | По умолчанию 20 |
| `Storage:RootPath` | `Storage__RootPath` | Каталог хранилища версий (например, `/var/lib/drawingflow/storage`); доступен только учётной записи службы, не публикуется HTTP |
| `Storage:MaxFileSizeBytes` | | Лимит размера файла версии, по умолчанию 1073741824 (1 ГиБ) |
| `Kestrel:Endpoints:Https:Url`, `Kestrel:Endpoints:Https:Certificate:Path/Password` | | TLS-сертификат (PFX) |
| `Logging:Console:FormatterName` | | `json` для структурированных логов |

Пример `appsettings.Production.json` (без секретов):
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:5443",
        "Certificate": { "Path": "/etc/drawingflow/server.pfx", "Password": "<из защищённого хранилища>" }
      }
    }
  },
  "Logging": { "Console": { "FormatterName": "json" } }
}
```

Каталог хранилища создайте заранее и выдайте права учётной записи службы:
```bash
sudo mkdir -p /var/lib/drawingflow/storage && sudo chown drawingflow: /var/lib/drawingflow/storage && sudo chmod 700 /var/lib/drawingflow/storage
```

### 3.3. Миграции и первичная инициализация
```bash
cd /opt/drawingflow/server
ASPNETCORE_ENVIRONMENT=Production dotnet DrawingFlow.Server.dll --migrate
```
Команда применяет миграции, создаёт администратора сервера из `Bootstrap__Administrator__*` (только если пользователей ещё нет) и завершает работу.
После первого входа смените пароль администратора и удалите переменные `Bootstrap__*`.

### 3.4. Запуск как службы
*Linux (systemd)* — `/etc/systemd/system/drawingflow.service`:
```ini
[Service]
WorkingDirectory=/opt/drawingflow/server
ExecStart=/usr/bin/dotnet /opt/drawingflow/server/DrawingFlow.Server.dll
EnvironmentFile=/etc/drawingflow/server.env
Restart=always
User=drawingflow
Environment=ASPNETCORE_ENVIRONMENT=Production
```
*Windows* — служба через `sc.exe create DrawingFlowServer binPath= "C:\DrawingFlow\Server\DrawingFlow.Server.exe"` (при публикации с `--self-contained` или установленным runtime).

### 3.5. Проверка
* `GET https://сервер:5443/health/live` → `Healthy`; `GET /health/ready` проверяет подключение к БД.
* `GET /api/server/info` возвращает версию.

## 4. Desktop

```bash
dotnet publish src/DrawingFlow.Desktop -c Release -r win-x64 --self-contained false -o publish/desktop
```
Распространите каталог (или установщик) на рабочие станции. При первом запуске укажите адрес сервера `https://…`, логин и пароль.
Настройки: `%APPDATA%\DrawingFlow\settings.json`; сессия: `%LOCALAPPDATA%\DrawingFlow\session.bin` (DPAPI); локальные копии версий: `%LOCALAPPDATA%\DrawingFlow\cache\`.

## 5. Agent

1. В Desktop (страница «Агенты», администратор организации) зарегистрируйте агента и скопируйте показанный один раз ключ.
2. Опубликуйте и разместите службу на рабочей станции:
   ```bash
   dotnet publish src/DrawingFlow.Agent -c Release -r win-x64 --self-contained false -o publish/agent
   ```
3. От имени администратора сохраните ключ (он шифруется DPAPI в области компьютера и хранится в `%ProgramData%\DrawingFlow\Agent`):
   ```
   DrawingFlow.Agent.exe register --server https://drawingflow.company.local/ --key <ключ агента>
   ```
4. Установите и запустите службу:
   ```
   sc.exe create "DrawingFlow Agent" binPath= "C:\DrawingFlow\Agent\DrawingFlow.Agent.exe" start= auto obj= "NT AUTHORITY\LocalService"
   sc.exe start "DrawingFlow Agent"
   ```
   Учётная запись службы должна иметь право чтения наблюдаемых папок (для сетевых папок — доменная учётная запись).
5. В Desktop добавьте агенту папки наблюдения (проект, путь, маски, правило сопоставления). Агент подхватывает изменения в течение `Agent:ConfigurationRefreshInterval` (1 минута).
6. Проверка: `DrawingFlow.Agent.exe status` показывает регистрацию и очередь; в Desktop агент отмечен «на связи».

Настройки службы — раздел `Agent` в `appsettings.json` (интервалы, окно стабильности, повторы, `ExcludedRoots`). Журнал — в журнале событий Windows (источник «DrawingFlow Agent») и stdout при запуске из консоли.

## 5а. Чек-лист первичной настройки (администратор)

1. Развернуть PostgreSQL (раздел 2) и сервер (раздел 3); задать `Jwt__SigningKey`, строку подключения, `Storage__RootPath`, TLS.
2. Выполнить `DrawingFlow.Server --migrate` с `Bootstrap__Administrator__*` — создаётся администратор сервера с временным паролем.
3. Войти в Desktop администратором, сменить временный пароль (обязательный диалог), удалить переменные `Bootstrap__*` на сервере.
4. «Администрирование» → создать организацию, указав владельца (или стать им).
5. «Участники» → создать пользователей (временные пароли сообщить лично) и назначить роли организации.
6. «Проекты» → создать проекты, назначить роли участников (инженеры, проверяющие, руководители).
7. «Агенты» → зарегистрировать агентов рабочих станций, скопировать ключи, установить службу (раздел 5), настроить папки наблюдения.
8. Проверить `/health/ready`, первую загрузку версии агентом и прохождение согласования по `windows-verification.md`.
9. Настроить резервное копирование (`backup-recovery.md`) и проверить восстановление на тестовом стенде.

## 5б. Обратный прокси и журналирование

* За nginx/IIS включите `ReverseProxy:Enabled = true` и перечислите адреса прокси в `ReverseProxy:KnownProxies` (или сети в `KnownNetworks`, CIDR).
  Тогда ограничение частоты входа и адрес клиента в аудите используют `X-Forwarded-For`. Пустые списки означают доверие любому отправителю — только если сервер недоступен напрямую.
* Структурированные логи: `"Logging": { "Console": { "FormatterName": "json" } }`. Ошибки содержат `traceId`, совпадающий с ответом клиенту.
* Desktop пишет журнал в `%LOCALAPPDATA%\DrawingFlow\logs\desktop-ГГГГММДД.log` (14 дней); агент — в `%ProgramData%\DrawingFlow\Agent\logs` и в журнал событий Windows (источник «DrawingFlow Agent», предупреждения и ошибки).
* Проверка целостности хранилища: `DrawingFlow.Server --verify-storage` (только чтение; код выхода 3, если объекты отсутствуют).

## 6. Обновление

1. Сделайте резервную копию БД (см. `backup-recovery.md`).
2. Остановите службу сервера, разверните новую публикацию.
3. Выполните `dotnet DrawingFlow.Server.dll --migrate`.
4. Запустите службу, проверьте `/health/ready`. Обновите Desktop на рабочих станциях (совместимость API — в рамках одной мажорной версии).

## 6а. Публикация релиза

```bash
dotnet publish src/DrawingFlow.Server  -c Release -o publish/server
dotnet publish src/DrawingFlow.Desktop -c Release -r win-x64 --self-contained false -o publish/desktop
dotnet publish src/DrawingFlow.Agent   -c Release -r win-x64 --self-contained false -o publish/agent
```
Версия задаётся свойством `Version` в `Directory.Build.props`; она отображается в окне входа, в сведениях о сервере и в сердцебиении агента.

## 7. Локальная разработка (на этой машине разработчика)

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
scripts/dev-postgres.sh start            # PostgreSQL 16 на порту 5433, данные в .local/pgdata
dotnet build DrawingFlow.sln
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/DrawingFlow.Server   # http://localhost:5080, миграции применяются автоматически, файлы — в .local/storage
# агент для разработки: регистрация ключа и запуск (каталог данных .local/agent)
DOTNET_ENVIRONMENT=Development dotnet run --project src/DrawingFlow.Agent -- register --server http://localhost:5080/ --key <ключ>
DOTNET_ENVIRONMENT=Development dotnet run --project src/DrawingFlow.Agent
dotnet test                              # модульные и интеграционные тесты (нужен запущенный dev-кластер)
```
Учётные данные dev-администратора заданы в `src/DrawingFlow.Server/appsettings.Development.json` и действуют только для локальной базы.
Описание API в Development: `http://localhost:5080/openapi/v1.json`.
Новая миграция: `ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Имя> --project src/DrawingFlow.Infrastructure --startup-project src/DrawingFlow.Server --output-dir Persistence/Migrations`
(после этого пересоберите решение перед запуском).
