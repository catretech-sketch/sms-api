namespace Sms.Shared.Kernel.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encoded);
}
