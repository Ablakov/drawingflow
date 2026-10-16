using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DrawingFlow.Application;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Infrastructure;
using DrawingFlow.Infrastructure.Options;
using DrawingFlow.Infrastructure.Persistence;
using DrawingFlow.Infrastructure.Security;
using DrawingFlow.Infrastructure.Storage;
using DrawingFlow.Server.Auth;
using DrawingFlow.Server.Controllers;
using DrawingFlow.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Инфраструктура и прикладные службы ---
builder.Services.AddDrawingFlowInfrastructure(builder.Configuration);
builder.Services.AddDrawingFlowApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// --- Web API ---
builder.Services
    .AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        var messages = options.ModelBindingMessageProvider;
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"Значение «{value}» недопустимо для поля «{field}».");
        messages.SetValueIsInvalidAccessor(value => $"Значение «{value}» недопустимо.");
        messages.SetUnknownValueIsInvalidAccessor(field => $"Недопустимое значение поля «{field}».");
        messages.SetValueMustBeANumberAccessor(field => $"Поле «{field}» должно быть числом.");
        messages.SetValueMustNotBeNullAccessor(_ => "Значение обязательно.");
        messages.SetMissingBindRequiredValueAccessor(field => $"Поле «{field}» обязательно.");
        messages.SetMissingKeyOrValueAccessor(() => "Отсутствует ключ или значение.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "Тело запроса обязательно.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"Значение «{value}» недопустимо.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Недопустимое значение.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Значение должно быть числом.");
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value is { Errors.Count: > 0 })
                .ToDictionary(
                    e => string.IsNullOrEmpty(e.Key) || e.Key.StartsWith('$') ? "body" : char.ToLowerInvariant(e.Key[0]) + e.Key[1..],
                    e => e.Value!.Errors
                        .Select(err => err.Exception is not null || string.IsNullOrEmpty(err.ErrorMessage) || err.ErrorMessage.Contains("JSON", StringComparison.Ordinal)
                            ? "Некорректный формат данных запроса."
                            : err.ErrorMessage)
                        .Distinct()
                        .ToArray());
            var problem = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Ошибка валидации",
                Detail = "Проверьте правильность переданных данных.",
                Instance = context.HttpContext.Request.Path,
            };
            problem.Extensions["errorCode"] = "validation";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        };
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// --- Аутентификация и авторизация ---
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.Events = new JwtBearerEvents
        {
            // Единый формат ошибки аутентификации (ProblemDetails, русский текст) вместо пустого 401.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var response = context.Response;
                response.StatusCode = StatusCodes.Status401Unauthorized;
                response.Headers.WWWAuthenticate = "Bearer";
                var expired = context.AuthenticateFailure is Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException;
                await response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Требуется аутентификация",
                    Detail = expired ? "Срок действия сессии истёк. Выполните вход заново." : "Требуется вход в систему.",
                    Instance = context.Request.Path,
                    Extensions = { ["errorCode"] = expired ? "token_expired" : "unauthorized", ["traceId"] = context.HttpContext.TraceIdentifier },
                }, options: null, contentType: "application/problem+json");
            },
            OnForbidden = async context =>
            {
                var response = context.Response;
                response.StatusCode = StatusCodes.Status403Forbidden;
                await response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Доступ запрещён",
                    Detail = "Недостаточно прав для выполнения операции.",
                    Instance = context.Request.Path,
                    Extensions = { ["errorCode"] = "forbidden", ["traceId"] = context.HttpContext.TraceIdentifier },
                }, options: null, contentType: "application/problem+json");
            },
        };
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.Value),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = DrawingFlowClaims.Login,
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --- Ограничение частоты попыток входа ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Слишком много попыток",
            Detail = "Превышено число попыток входа. Повторите через минуту.",
        }, options: null, contentType: "application/problem+json", cancellationToken: token);
    };
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimits:LoginPerMinute", 20),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// --- Диагностика ---
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);

// --- Обратный прокси (параметры читаются при создании опций, чтобы учесть все источники конфигурации) ---
builder.Services.AddOptions<ReverseProxyOptions>().Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName));
builder.Services.AddOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>()
    .Configure<IOptions<ReverseProxyOptions>>((options, proxy) =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in proxy.Value.KnownProxies)
        {
            options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
        }

        foreach (var network in proxy.Value.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });

var app = builder.Build();

// --- Миграции и первичная инициализация ---
if (args.Contains("--verify-storage", StringComparer.OrdinalIgnoreCase))
{
    using var verifyScope = app.Services.CreateScope();
    var report = await verifyScope.ServiceProvider.GetRequiredService<StorageVerifier>().VerifyAsync(CancellationToken.None);
    Console.WriteLine($"Проверено версий: {report.CheckedVersions}. Отсутствует объектов хранилища: {report.Missing.Count}.");
    foreach (var missing in report.Missing)
    {
        Console.WriteLine($"  ОТСУТСТВУЕТ: документ {missing.DocumentNumber}, версия {missing.VersionNumber}, ключ {missing.StorageKey} (версия {missing.VersionId})");
    }

    Environment.ExitCode = report.IsConsistent ? 0 : 3;
    return;
}

var migrateAndExit = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (migrateAndExit || databaseOptions.MigrateOnStartup)
    {
        await initializer.MigrateAsync(CancellationToken.None);
    }

    if (migrateAndExit)
    {
        await initializer.BootstrapAsync(CancellationToken.None);
        app.Logger.LogInformation("Миграции применены, сервер завершает работу (режим --migrate).");
        return;
    }

    await initializer.BootstrapAsync(CancellationToken.None);
}

var reverseProxy = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
if (reverseProxy.Enabled)
{
    app.UseForwardedHeaders();
    app.Logger.LogInformation("Включена обработка заголовков обратного прокси (известных прокси: {Proxies}, сетей: {Networks})", reverseProxy.KnownProxies.Length, reverseProxy.KnownNetworks.Length);
    if (reverseProxy.KnownProxies.Length == 0 && reverseProxy.KnownNetworks.Length == 0)
    {
        app.Logger.LogWarning("ReverseProxy: списки известных прокси пусты — заголовкам X-Forwarded-* доверяется от любого клиента. Укажите KnownProxies или KnownNetworks.");
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AgentRevocationMiddleware>();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

/// <summary>Точка входа видна тестовому проекту (WebApplicationFactory).</summary>
public partial class Program;
