namespace RbxManager.Core.Storage;

using RbxManager.Core.Models;

public interface IVaultKeyProvider
{
    bool HasKey { get; }

    SecretValue? GetKey();

    void SetKey(SecretValue? key);

    void Clear();
}
