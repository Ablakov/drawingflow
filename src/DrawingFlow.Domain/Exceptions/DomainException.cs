namespace DrawingFlow.Domain.Exceptions;

/// <summary>
/// Базовое исключение бизнес-логики. Сообщение предназначено для показа пользователю (на русском языке).
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Машиночитаемый код ошибки (латиницей).</summary>
    public string ErrorCode { get; }
}

/// <summary>Запрошенный объект не найден или недоступен для текущего пользователя.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message = "Объект не найден.")
        : base("not_found", message) { }
}

/// <summary>Пользователь аутентифицирован, но не имеет прав на операцию.</summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "Недостаточно прав для выполнения операции.")
        : base("forbidden", message) { }
}

/// <summary>Пользователь не аутентифицирован или учётные данные недействительны.</summary>
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Требуется вход в систему.")
        : base("unauthorized", message) { }
}

/// <summary>Операция конфликтует с текущим состоянием данных (дубликат, параллельное изменение).</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string message)
        : base("conflict", message) { }
}

/// <summary>Нарушение бизнес-правила (недопустимый переход состояния и т. п.).</summary>
public sealed class DomainRuleException : DomainException
{
    public DomainRuleException(string message)
        : base("domain_rule", message) { }
}

/// <summary>Ошибка валидации входных данных. Содержит ошибки по полям.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }, message) { }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors, string? message = null)
        : base("validation", message ?? "Проверьте правильность введённых данных.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Хранилище файлов недоступно или объект отсутствует. Клиенту возвращается 503.</summary>
public sealed class StorageException : DomainException
{
    public StorageException(string message = "Хранилище файлов недоступно. Повторите попытку позже или обратитесь к администратору.")
        : base("storage_unavailable", message) { }
}
