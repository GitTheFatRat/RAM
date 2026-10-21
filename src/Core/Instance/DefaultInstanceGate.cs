namespace RbxManager.Core.Instance;

using RbxManager.Core.Common;

public sealed class DefaultInstanceGate : IInstanceGate
{
    public bool IsMultiInstanceEnabled => false;
    public bool IsAvailable => false;

    public Result TryEnable() =>
        Result.Failure("Tính năng chạy nhiều tài khoản không khả dụng trong bản dựng này.", "MultiInstanceNotAvailable");

    public void Disable()
    {
    }
}
