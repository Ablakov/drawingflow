namespace DrawingFlow.Application.Abstractions;

public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string hashedPassword, string providedPassword);
}
