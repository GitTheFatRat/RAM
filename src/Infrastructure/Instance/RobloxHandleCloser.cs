namespace RbxManager.Infrastructure.Instance;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public static partial class RobloxHandleCloser
{
    private const int SystemExtendedHandleInformation = 64;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int ObjectNameInformationClass = 1;
    private const uint ProcessDupHandle = 0x0040;
    private const uint DuplicateCloseSource = 0x00000001;
    private const uint DuplicateSameAccess = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleTableEntryInfoEx
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern uint NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern uint NtQueryObject(
        IntPtr handle,
        int objectInformationClass,
        IntPtr objectInformation,
        uint objectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(IntPtr hFile);

    private const uint FileTypeDisk = 0x0001;
    private const uint FileTypePipe = 0x0003;

    public static int CloseSingletonHandlesInRobloxProcesses(
        IProcessProvider processProvider,
        params string[] targetNames)
    {
        ArgumentNullException.ThrowIfNull(processProvider);
        IReadOnlyList<ProcessSnapshot> robloxProcesses = processProvider.GetRobloxProcesses();
        if (robloxProcesses.Count == 0)
        {
            return 0;
        }

        HashSet<int> targetPids = new(robloxProcesses.Count);
        foreach (ProcessSnapshot proc in robloxProcesses)
        {
            if (proc.Pid > 0)
            {
                targetPids.Add(proc.Pid);
            }
        }

        return CloseMatchingHandlesInProcesses(targetPids, targetNames);
    }

    public static int CloseMatchingHandlesInProcesses(
        IReadOnlySet<int> targetProcessIds,
        IReadOnlyList<string> targetSubstrings)
    {
        if (targetProcessIds == null || targetProcessIds.Count == 0 ||
            targetSubstrings == null || targetSubstrings.Count == 0)
        {
            return 0;
        }

        uint bufferSize = 0x40000;
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint status;
            while ((status = NtQuerySystemInformation(
                SystemExtendedHandleInformation,
                buffer,
                bufferSize,
                out uint requiredLength)) == StatusInfoLengthMismatch)
            {
                Marshal.FreeHGlobal(buffer);
                bufferSize = Math.Max(bufferSize * 2, requiredLength + 0x10000);
                if (bufferSize > 64 * 1024 * 1024)
                {
                    buffer = IntPtr.Zero;
                    return 0;
                }
                buffer = Marshal.AllocHGlobal((int)bufferSize);
            }

            if (status != 0)
            {
                return 0;
            }

            long handleCount = IntPtr.Size == 8
                ? Marshal.ReadInt64(buffer)
                : Marshal.ReadInt32(buffer);

            int headerOffset = IntPtr.Size * 2;
            int entrySize = Marshal.SizeOf<SystemHandleTableEntryInfoEx>();
            long maxEntries = ((long)bufferSize - headerOffset) / entrySize;
            if (handleCount > maxEntries)
            {
                handleCount = maxEntries;
            }

            Dictionary<int, IntPtr> openProcessHandles = new();
            IntPtr currentProcess = GetCurrentProcess();
            IntPtr nameBuffer = Marshal.AllocHGlobal(2048);
            int closedCount = 0;

            try
            {
                for (long i = 0; i < handleCount; i++)
                {
                    IntPtr entryPtr = IntPtr.Add(buffer, headerOffset + (int)(i * entrySize));
                    SystemHandleTableEntryInfoEx entry = Marshal.PtrToStructure<SystemHandleTableEntryInfoEx>(entryPtr);
                    int pid = (int)entry.UniqueProcessId;
                    if (!targetProcessIds.Contains(pid))
                    {
                        continue;
                    }

                    if (!openProcessHandles.TryGetValue(pid, out IntPtr processHandle))
                    {
                        processHandle = OpenProcess(ProcessDupHandle, false, pid);
                        openProcessHandles[pid] = processHandle;
                    }

                    if (processHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr sourceHandle = new((long)(ulong)entry.HandleValue);
                    if (!DuplicateHandle(
                        processHandle,
                        sourceHandle,
                        currentProcess,
                        out IntPtr duplicatedHandle,
                        0,
                        false,
                        DuplicateSameAccess))
                    {
                        continue;
                    }

                    bool matchesTarget = false;
                    try
                    {
                        uint fileType = GetFileType(duplicatedHandle);
                        if (fileType != FileTypePipe && fileType != FileTypeDisk)
                        {
                            string? objectName = QueryObjectName(duplicatedHandle, nameBuffer, 2048);
                            if (!string.IsNullOrEmpty(objectName))
                            {
                                for (int s = 0; s < targetSubstrings.Count; s++)
                                {
                                    string sub = targetSubstrings[s];
                                    if (!string.IsNullOrEmpty(sub) &&
                                        objectName.Contains(sub, StringComparison.OrdinalIgnoreCase))
                                    {
                                        matchesTarget = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(duplicatedHandle);
                    }

                    if (matchesTarget)
                    {
                        if (DuplicateHandle(
                            processHandle,
                            sourceHandle,
                            IntPtr.Zero,
                            out IntPtr dummy,
                            0,
                            false,
                            DuplicateCloseSource))
                        {
                            if (dummy != IntPtr.Zero)
                            {
                                CloseHandle(dummy);
                            }

                            closedCount++;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nameBuffer);
                foreach (IntPtr procHandle in openProcessHandles.Values)
                {
                    if (procHandle != IntPtr.Zero)
                    {
                        CloseHandle(procHandle);
                    }
                }
            }

            return closedCount;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static string? QueryObjectName(IntPtr handle, IntPtr buffer, uint bufferSize)
    {
        uint status = NtQueryObject(
            handle,
            ObjectNameInformationClass,
            buffer,
            bufferSize,
            out _);

        if (status != 0)
        {
            return null;
        }

        UnicodeString unicodeString = Marshal.PtrToStructure<UnicodeString>(buffer);
        if (unicodeString.Length == 0 || unicodeString.Buffer == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2);
    }
}
