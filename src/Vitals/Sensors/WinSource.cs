using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vitals.Sensors;

/// <summary>
/// Driver-free backend: CPU load, memory, volumes, disk throughput and network
/// rates. Everything here works without elevation except physical disk counters.
/// </summary>
public sealed class WinSource : ISensorSource
{
    private const uint IoctlDiskPerformance = 0x00070020;

    private readonly List<DiskCounter> _disks = new();
    private readonly Dictionary<string, (long Rx, long Tx)> _previousNetwork = new(StringComparer.Ordinal);

    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousTick;

    public string Name => "win";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public void Initialize()
    {
        _previousTick = Environment.TickCount64;

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                var path = @"\\.\" + drive.Name.TrimEnd('\\');
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _disks.Add(new DiskCounter(stream));
            }
            catch
            {
                // Volume counters need elevation; skip what we cannot open.
            }
        }

        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _previousIdle = idle.ToUInt64();
            _previousKernel = kernel.ToUInt64();
            _previousUser = user.ToUInt64();
        }
    }

    public void Sample(SnapshotBuilder builder)
    {
        var now = Environment.TickCount64;
        var seconds = Math.Max(0.001, (now - _previousTick) / 1000.0);

        SampleCpu(builder);
        SampleMemory(builder);
        SampleDisks(builder, seconds);
        SampleNetwork(builder, seconds);
        SampleVolumes(builder);

        builder.Readings.Add(new Reading("win/cpu/load", Name, "CPU", "Total", SensorKind.Load, builder.CpuLoadPercent, "%"));

        if (builder.Memory is { } memory)
        {
            builder.Readings.Add(new Reading("win/mem/used", Name, "Memory", "Used", SensorKind.Data, memory.UsedBytes / 1024d / 1024d / 1024d, "GB"));
            builder.Readings.Add(new Reading("win/mem/available", Name, "Memory", "Available", SensorKind.Data, memory.AvailableBytes / 1024d / 1024d / 1024d, "GB"));
            builder.Readings.Add(new Reading("win/mem/commit", Name, "Memory", "Commit", SensorKind.Data, memory.CommitBytes / 1024d / 1024d / 1024d, "GB"));
        }

        builder.Readings.Add(new Reading("win/disk/read", Name, "Disk", "Read", SensorKind.Throughput, builder.DiskReadBytesPerSecond / 1024d / 1024d, "MB/s"));
        builder.Readings.Add(new Reading("win/disk/write", Name, "Disk", "Write", SensorKind.Throughput, builder.DiskWriteBytesPerSecond / 1024d / 1024d, "MB/s"));

        if (builder.NetReceiveBytesPerSecond is { } rx && builder.NetSendBytesPerSecond is { } tx)
        {
            builder.Readings.Add(new Reading("win/net/rx", Name, "Network", "Received", SensorKind.Throughput, rx / 1024d / 1024d, "MB/s"));
            builder.Readings.Add(new Reading("win/net/tx", Name, "Network", "Sent", SensorKind.Throughput, tx / 1024d / 1024d, "MB/s"));
        }

        _previousTick = now;
    }

    public void Dispose()
    {
        foreach (var disk in _disks)
        {
            disk.Dispose();
        }

        _disks.Clear();
    }

    private void SampleCpu(SnapshotBuilder builder)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        var idleNow = idle.ToUInt64();
        var kernelNow = kernel.ToUInt64();
        var userNow = user.ToUInt64();

        var totalDelta = kernelNow - _previousKernel + (userNow - _previousUser);
        var idleDelta = idleNow - _previousIdle;

        if (_previousKernel != 0 && totalDelta > 0)
        {
            builder.CpuLoadPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0.0, 100.0);
        }

        _previousIdle = idleNow;
        _previousKernel = kernelNow;
        _previousUser = userNow;
    }

    private static void SampleMemory(SnapshotBuilder builder)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return;
        }

        double commit = 0;
        double limit = 0;
        double cache = 0;

        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (GetPerformanceInfo(out info, info.Size))
        {
            var pageSize = (double)info.PageSize;
            commit = info.CommitTotal * pageSize;
            limit = info.CommitLimit * pageSize;
            cache = info.SystemCache * pageSize;
        }

        builder.Memory = new MemorySample(status.TotalPhys, status.AvailPhys, commit, limit, cache);
    }

    private void SampleDisks(SnapshotBuilder builder, double seconds)
    {
        if (_disks.Count == 0)
        {
            return;
        }

        long read = 0;
        long write = 0;

        foreach (var disk in _disks)
        {
            if (!DeviceIoControl(
                    disk.Handle,
                    IoctlDiskPerformance,
                    IntPtr.Zero,
                    0,
                    out var performance,
                    (uint)Marshal.SizeOf<DiskPerformance>(),
                    out _,
                    IntPtr.Zero))
            {
                continue;
            }

            if (disk.Read > 0)
            {
                read += Math.Max(0, performance.BytesRead - disk.Read);
                write += Math.Max(0, performance.BytesWritten - disk.Write);
            }

            disk.Read = performance.BytesRead;
            disk.Write = performance.BytesWritten;
        }

        builder.DiskReadBytesPerSecond = read / seconds;
        builder.DiskWriteBytesPerSecond = write / seconds;
    }

    private void SampleNetwork(SnapshotBuilder builder, double seconds)
    {
        try
        {
            long received = 0;
            long sent = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (adapter.NetworkInterfaceType is not (
                        NetworkInterfaceType.Ethernet or
                        NetworkInterfaceType.GigabitEthernet or
                        NetworkInterfaceType.FastEthernetT or
                        NetworkInterfaceType.FastEthernetFx or
                        NetworkInterfaceType.Wireless80211))
                {
                    continue;
                }

                var stats = adapter.GetIPStatistics();
                seen.Add(adapter.Id);

                if (_previousNetwork.TryGetValue(adapter.Id, out var previous))
                {
                    received += Math.Max(0, stats.BytesReceived - previous.Rx);
                    sent += Math.Max(0, stats.BytesSent - previous.Tx);
                }

                _previousNetwork[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
            }

            foreach (var stale in _previousNetwork.Keys.Where(key => !seen.Contains(key)).ToArray())
            {
                _previousNetwork.Remove(stale);
            }

            builder.NetReceiveBytesPerSecond = received / seconds;
            builder.NetSendBytesPerSecond = sent / seconds;
        }
        catch
        {
            // Network statistics are best effort.
        }
    }

    private static void SampleVolumes(SnapshotBuilder builder)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                builder.Volumes.Add(new VolumeSample(
                    drive.Name.TrimEnd('\\'),
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.TotalFreeSpace));
            }
            catch
            {
                // Skip volumes that disappear mid-enumeration.
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskPerformance
    {
        public long BytesRead;
        public long BytesWritten;
        public long ReadTime;
        public long WriteTime;
        public long IdleTime;
        public uint ReadCount;
        public uint WriteCount;
        public uint QueueDepth;
        public uint SplitCount;
        public long QueryTime;
        public uint StorageDeviceNumber;
        public Guid StorageManagerName;
    }

    private sealed class DiskCounter : IDisposable
    {
        private readonly FileStream _stream;

        public DiskCounter(FileStream stream) => _stream = stream;

        public SafeFileHandle Handle => _stream.SafeFileHandle;

        public long Read { get; set; }

        public long Write { get; set; }

        public void Dispose() => _stream.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation performanceInformation, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        out DiskPerformance outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
