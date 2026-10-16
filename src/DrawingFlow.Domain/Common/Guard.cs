using System.Text.RegularExpressions;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Common;

/// <summary>Проверки входных значений доменных объектов. Все сообщения — на русском языке.</summary>
public static partial class Guard
{
    public const int CodeMaxLength = 50;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@\-]*$")]
    private static partial Regex LoginPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static string Required(string? value, string field, string displayName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationException(field, $"Поле «{displayName}» обязательно для заполнения.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(field, $"Поле «{displayName}» не должно превышать {maxLength} символов.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string field, string displayName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(field, $"Поле «{displayName}» не должно превышать {maxLength} символов.");
        }

        return trimmed;
    }

    /// <summary>Код организации или проекта: латиница, цифры, точка, подчёркивание, дефис; начинается с буквы или цифры.</summary>
    public static string Code(string? value, string field, string displayName)
    {
        var trimmed = Required(value, field, displayName, CodeMaxLength);
        if (!CodePattern().IsMatch(trimmed))
        {
            throw new ValidationException(field,
                $"Поле «{displayName}» может содержать только латинские буквы, цифры, точку, подчёркивание и дефис и должно начинаться с буквы или цифры.");
        }

        return trimmed;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string Login(string? value)
    {
        var trimmed = Required(value, "login", "Логин", 64);
        if (trimmed.Length < 3)
        {
            throw new ValidationException("login", "Логин должен содержать не менее 3 символов.");
        }

        if (!LoginPattern().IsMatch(trimmed))
        {
            throw new ValidationException("login",
                "Логин может содержать только латинские буквы, цифры и символы . _ @ - и должен начинаться с буквы или цифры.");
        }

        return trimmed;
    }

    public static string NormalizeLogin(string login) => login.Trim().ToUpperInvariant();

    public static string? Email(string? value)
    {
        var trimmed = Optional(value, "email", "Электронная почта", 254);
        if (trimmed is not null && !EmailPattern().IsMatch(trimmed))
        {
            throw new ValidationException("email", "Некорректный адрес электронной почты.");
        }

        return trimmed;
    }

    public static void Password(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
        {
            throw new ValidationException("password", "Пароль должен содержать не менее 8 символов.");
        }

        if (value.Length > 256)
        {
            throw new ValidationException("password", "Пароль слишком длинный (максимум 256 символов).");
        }

        var hasLetter = value.Any(char.IsLetter);
        var hasDigit = value.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
        {
            throw new ValidationException("password", "Пароль должен содержать хотя бы одну букву и одну цифру.");
        }
    }

    /// <summary>Номер (шифр) документа: любые печатные символы, без управляющих; сравнение без учёта регистра и лишних пробелов.</summary>
    public static string DocumentNumber(string? value)
    {
        var trimmed = Required(value, "documentNumber", "Номер документа", 100);
        if (trimmed.Any(char.IsControl))
        {
            throw new ValidationException("documentNumber", "Номер документа содержит недопустимые символы.");
        }

        return trimmed;
    }

    public static string NormalizeDocumentNumber(string documentNumber) =>
        string.Join(' ', documentNumber.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();

    /// <summary>Имя файла без пути. Отбрасывает каталоги, запрещает управляющие символы и имена из одних точек.</summary>
    public static string FileName(string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        var separator = raw.LastIndexOfAny(['/', '\\']);
        var name = separator >= 0 ? raw[(separator + 1)..] : raw;
        if (string.IsNullOrWhiteSpace(name) || name.Trim('.').Length == 0)
        {
            throw new ValidationException("fileName", "Укажите имя файла.");
        }

        if (name.Length > 260)
        {
            throw new ValidationException("fileName", "Имя файла не должно превышать 260 символов.");
        }

        if (name.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new ValidationException("fileName", "Имя файла содержит недопустимые символы.");
        }

        return name;
    }

    /// <summary>SHA-256 в виде 64 шестнадцатеричных символов; возвращает в нижнем регистре.</summary>
    public static string Sha256(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length != 64 || !trimmed.All(Uri.IsHexDigit))
        {
            throw new ValidationException("sha256Hash", "Контрольная сумма должна содержать 64 шестнадцатеричных символа.");
        }

        return trimmed.ToLowerInvariant();
    }

    public static DateTime Utc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"Значение «{field}» должно быть в UTC.", field);
        }

        return value;
    }
}
