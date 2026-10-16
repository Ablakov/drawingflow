using System.Net;
using DrawingFlow.Contracts;

namespace DrawingFlow.Client;

/// <summary>Сервер вернул ошибку. Сообщение пригодно для показа пользователю.</summary>
public class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, ApiProblem? problem, string? rawBody = null)
        : base(BuildMessage(statusCode, problem))
    {
        StatusCode = statusCode;
        Problem = problem;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }
    public ApiProblem? Problem { get; }
    public string? RawBody { get; }
    public string? ErrorCode => Problem?.ErrorCode;
    public IReadOnlyDictionary<string, string[]>? Errors => Problem?.Errors;

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
    public bool IsValidation => StatusCode == HttpStatusCode.BadRequest || StatusCode == HttpStatusCode.UnprocessableEntity;

    private static string BuildMessage(HttpStatusCode statusCode, ApiProblem? problem)
    {
        if (!string.IsNullOrWhiteSpace(problem?.Detail))
        {
            return problem.Detail;
        }

        if (!string.IsNullOrWhiteSpace(problem?.Title))
        {
            return problem.Title;
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Сервер отклонил запрос: проверьте введённые данные.",
            HttpStatusCode.Unauthorized => "Требуется вход в систему.",
            HttpStatusCode.Forbidden => "Недостаточно прав для выполнения операции.",
            HttpStatusCode.NotFound => "Объект не найден.",
            HttpStatusCode.Conflict => "Конфликт данных. Обновите информацию и повторите попытку.",
            HttpStatusCode.TooManyRequests => "Слишком много запросов. Повторите позже.",
            HttpStatusCode.ServiceUnavailable => "Сервер временно недоступен.",
            _ => $"Сервер вернул ошибку {(int)statusCode}.",
        };
    }
}

/// <summary>Не удалось связаться с сервером (сеть, DNS, TLS, тайм-аут).</summary>
public sealed class ApiConnectionException(string message, Exception? inner) : Exception(message, inner)
{
    public static ApiConnectionException From(Exception inner, Uri? baseAddress) => new(
        $"Не удалось подключиться к серверу {baseAddress?.GetLeftPart(UriPartial.Authority) ?? string.Empty}. " +
        "Проверьте адрес сервера и сетевое подключение.", inner);
}
