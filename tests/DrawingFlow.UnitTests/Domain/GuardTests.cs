using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.UnitTests.Domain;

public sealed class GuardTests
{
    [Theory]
    [InlineData("ACME")]
    [InlineData("kzh-01")]
    [InlineData("A.B_C-1")]
    [InlineData("1")]
    public void Code_accepts_valid_values(string code)
    {
        Assert.Equal(code, Guard.Code($"  {code} ", "code", "Код"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-abc")]
    [InlineData("код")]
    [InlineData("a b")]
    [InlineData("a/b")]
    public void Code_rejects_invalid_values(string code)
    {
        var ex = Assert.Throws<ValidationException>(() => Guard.Code(code, "code", "Код"));
        Assert.Contains("code", ex.Errors.Keys);
    }

    [Fact]
    public void Code_rejects_too_long_value()
    {
        Assert.Throws<ValidationException>(() => Guard.Code(new string('A', Guard.CodeMaxLength + 1), "code", "Код"));
    }

    [Fact]
    public void NormalizeCode_and_login_are_case_insensitive()
    {
        Assert.Equal(Guard.NormalizeCode("acme"), Guard.NormalizeCode("ACME"));
        Assert.Equal(Guard.NormalizeLogin("Ivanov"), Guard.NormalizeLogin("ivanov "));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("иванов")]
    [InlineData("a b")]
    [InlineData("-user")]
    public void Login_rejects_invalid_values(string login)
    {
        Assert.Throws<ValidationException>(() => Guard.Login(login));
    }

    [Theory]
    [InlineData("ivanov")]
    [InlineData("i.ivanov@corp")]
    [InlineData("user_01-x")]
    public void Login_accepts_valid_values(string login)
    {
        Assert.Equal(login, Guard.Login(login));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    public void Password_rejects_weak_values(string? password)
    {
        Assert.Throws<ValidationException>(() => Guard.Password(password));
    }

    [Fact]
    public void Password_accepts_letters_and_digits()
    {
        Guard.Password("Ivanov12345");
    }

    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Email_normalizes(string? input, string? expected)
    {
        Assert.Equal(expected, Guard.Email(input));
    }

    [Fact]
    public void Email_rejects_invalid()
    {
        Assert.Throws<ValidationException>(() => Guard.Email("not-an-email"));
    }

    [Fact]
    public void Utc_rejects_local_time()
    {
        Assert.Throws<ArgumentException>(() => Guard.Utc(DateTime.Now, "x"));
    }

    [Theory]
    [InlineData("C:\\Проекты\\план.dwg", "план.dwg")]
    [InlineData("/home/user/схема.pdf", "схема.pdf")]
    [InlineData("  отчёт.docx ", "отчёт.docx")]
    public void FileName_strips_directories(string input, string expected)
    {
        Assert.Equal(expected, Guard.FileName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("bad:name.txt")]
    [InlineData("tab\tname")]
    public void FileName_rejects_invalid(string input)
    {
        Assert.Throws<ValidationException>(() => Guard.FileName(input));
    }

    [Fact]
    public void Sha256_normalizes_to_lower_case_and_validates_length()
    {
        var upper = new string('A', 64);
        Assert.Equal(new string('a', 64), Guard.Sha256(upper));
        Assert.Throws<ValidationException>(() => Guard.Sha256("abc"));
        Assert.Throws<ValidationException>(() => Guard.Sha256(new string('g', 64)));
    }
}
