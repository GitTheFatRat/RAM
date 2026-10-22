namespace RbxManager.Core.Security;

public interface IBasicAuthenticationChallenge
{
    string? Uri { get; }
    string? Challenge { get; }
    void SetResponse(string username, string password);
    void Cancel();
}
