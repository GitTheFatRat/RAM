namespace RbxManager.Core.Diagnostics;

public interface IUatLogger
{
    string TraceFilePath { get; }

    void LogTrace(string message);
}
