namespace HorrorFriday.Web.Helpers;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 30;

    public static (bool valid, string? error) Validate(string password)
    {
        if (password.Length < MinLength)
            return (false, $"La password deve contenere almeno {MinLength} caratteri.");
        if (password.Length > MaxLength)
            return (false, $"La password non può superare {MaxLength} caratteri.");
        if (!password.Any(char.IsUpper))
            return (false, "La password deve contenere almeno una lettera maiuscola.");
        if (!password.Any(char.IsLower))
            return (false, "La password deve contenere almeno una lettera minuscola.");
        if (!password.Any(char.IsDigit))
            return (false, "La password deve contenere almeno un numero.");
        return (true, null);
    }
}
