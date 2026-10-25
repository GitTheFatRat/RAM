namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using RbxManager.Core.Processes;

public sealed class SystemProcessProvider : IProcessProvider
{
    private const string ProcessName = "RobloxPlayerBeta";

    public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        List<ProcessSnapshot> snapshots = new(processes.Length);

        foreach (Process process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    DateTimeOffset startTime;
                    try
                    {
                        startTime = new DateTimeOffset(process.StartTime);
                    }
                    catch
                    {
                        startTime = DateTimeOffset.UtcNow;
                    }

                    long workingSet = 0;
                    try
                    {
                        workingSet = process.WorkingSet64;
                    }
                    catch
                    {
                        // Ignore race errors
                    }

                    snapshots.Add(new ProcessSnapshot(process.Id, startTime, workingSet));
                }
            }
            catch
            {
                // Tolerant of access and race errors
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
    }

    public bool Kill(int pid, bool graceful)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return true;
            }

            if (graceful)
            {
                bool closed = process.CloseMainWindow();
                if (closed && process.WaitForExit(5000))
                {
                    return true;
                }
            }

            process.Kill(entireProcessTree: true);
            return process.WaitForExit(3000);
        }
        catch (ArgumentException)
        {
            // Process not found or already exited
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsResponding(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited || process.Responding;
        }
        catch
        {
            // Gone or inaccessible: exit handling belongs to the session poll, not the hang detector.
            return true;
        }
    }

    public bool IsProcessRunning(string processName)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(processName);
            bool isRunning = processes.Length > 0;
            foreach (Process p in processes)
            {
                p.Dispose();
            }
            return isRunning;
        }
        catch
        {
            return false;
        }
    }
}
