namespace RbxManager.Core.Instance;

using RbxManager.Core.Common;

public sealed class DefaultInstanceGate : IInstanceGate
{
    public bool IsMultiInstanceEnabled => false;
    public bool IsAvailable => false;

    public Result TryEnable() =>
        Result.Failure("MultiInstanceNotAvailable", "MultiInstanceNotAvailable");

    public void Disable()
    {
    }
}
