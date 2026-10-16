using DrawingFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Infrastructure;

/// <summary>
/// Преобразует доменные исключения в ответы ProblemDetails (RFC 9457) с русскоязычными сообщениями.
/// Непредвиденные ошибки не раскрывают внутренних деталей.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, errorCode) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Ошибка валидации", "validation"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Требуется аутентификация", "unauthorized"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Доступ запрещён", "forbidden"),
            NotFoundException => (StatusCodes.Status404NotFound, "Не найдено", "not_found"),
            ConflictException => (StatusCodes.Status409Conflict, "Конфликт данных", "conflict"),
            DomainRuleException => (StatusCodes.Status422UnprocessableEntity, "Нарушение бизнес-правила", "domain_rule"),
            StorageException => (StatusCodes.Status503ServiceUnavailable, "Хранилище файлов недоступно", "storage_unavailable"),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (StatusCodes.Status413PayloadTooLarge, "Файл слишком большой", "payload_too_large"),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Запрос отменён клиентом", "cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера", "internal_error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Необработанная ошибка при обработке {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (status == StatusCodes.Status503ServiceUnavailable)
        {
            logger.LogError(exception, "Хранилище недоступно при обработке {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (status == 499)
        {
            return true;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception switch
            {
                DomainException => exception.Message,
                BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => "Размер запроса превышает допустимый предел.",
                _ => "Произошла непредвиденная ошибка. Обратитесь к администратору.",
            },
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
