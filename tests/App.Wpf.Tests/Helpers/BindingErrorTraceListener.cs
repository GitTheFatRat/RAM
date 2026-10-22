namespace RbxManager.App.Tests.Helpers;

using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class BindingErrorTraceListener : TraceListener
{
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors => _errors;

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _errors.Add(message);
        }
    }
}
