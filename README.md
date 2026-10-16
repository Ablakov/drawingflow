# DrawingFlow

Система централизованного управления проектной документацией: Windows-приложение (WPF), сервер ASP.NET Core с PostgreSQL,
служба-агент для автоматического мониторинга рабочих папок.

## Состав

```
DrawingFlow.sln
src/      Desktop (WPF) · Server (Web API) · Agent (служба Windows) · Domain · Application · Infrastructure · Contracts · Client
tests/    UnitTests · IntegrationTests (реальный PostgreSQL)
docs/     architecture · data-model · file-sync · security · installation · backup-recovery · roadmap
scripts/  dev-postgres.sh — локальный кластер PostgreSQL для разработки
```

## Быстрый старт разработчика

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH   # если SDK установлен в ~/.dotnet
scripts/dev-postgres.sh start
dotnet build DrawingFlow.sln
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/DrawingFlow.Server
dotnet test
```

Сервер разработки: `http://localhost:5080`; учётные данные администратора — в `src/DrawingFlow.Server/appsettings.Development.json`; файлы версий — в `.local/storage`.
Desktop запускается на Windows: укажите адрес сервера в окне входа.
Агент: `dotnet run --project src/DrawingFlow.Agent -- register --server http://localhost:5080/ --key <ключ>`, затем `dotnet run --project src/DrawingFlow.Agent`.

Подробности — в [docs/architecture.md](docs/architecture.md), установка — в [docs/installation.md](docs/installation.md), план — в [docs/roadmap.md](docs/roadmap.md).
