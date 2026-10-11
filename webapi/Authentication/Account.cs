namespace ServerActivity.Api.Authentication;

public sealed class Account(string login, string passwordHash)
{
    public string Login { get; } = login;
    public string PasswordHash { get; set; } = passwordHash;
}
