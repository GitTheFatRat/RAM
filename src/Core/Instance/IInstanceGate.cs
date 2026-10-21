namespace RbxManager.Core.Instance;

using RbxManager.Core.Common;

public interface IInstanceGate
{
    bool IsMultiInstanceEnabled { get; }
    bool IsAvailable { get; }

    Result TryEnable();
    void Disable();
}
