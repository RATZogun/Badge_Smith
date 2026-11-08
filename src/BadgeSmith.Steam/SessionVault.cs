namespace BadgeSmith.Steam;

public interface ISessionVault
{
    string? Read();

    void Save(string refreshToken);

    void Delete();
}

public sealed class SessionVaultException(string message) : Exception(message);

public static class SessionVault
{
    public static ISessionVault ForCurrentSystem() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialVault()
        : OperatingSystem.IsLinux() ? new LinuxSecretVault()
        : throw new PlatformNotSupportedException("O cofre de sessão só existe no Windows e no Linux.");
}
