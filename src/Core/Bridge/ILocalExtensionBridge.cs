namespace RbxManager.Core.Bridge;

using System;
using RbxManager.Core.Models;

public sealed record ExtensionImportedAccount(
    AccountId AccountId,
    string Username,
    long UserId,
    bool WasUpdated);

public interface ILocalExtensionBridge : IDisposable
{
    bool IsListening { get; }

    string EndpointUrl { get; }

    event Action<ExtensionImportedAccount>? AccountImported;

    bool StartListening();

    void StopListening();
}
