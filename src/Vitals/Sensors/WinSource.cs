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
    private const int VolumeRefreshMs = 30_000;
    private const int AdapterRefreshMs = 30_000;

    private readonly Dictionary<string, DiskCounter> _disks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VolumeInfo> _volumes = new();
    private readonly List<NetworkInterface> _adapters = new();
    private readonly Dictionary<string, (long Rx, long Tx)> _previousNetwork = new(StringComparer.Ordinal);

    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousTick;
    private long _lastVolumeRefresh;
    private long _lastAdapterRefresh;

    public string Name => "win";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public void Initialize()
    {
        CloseDisks();
        _volumes.Clear();
        _adapters.Clear();
        _previousNetwork.Clear();
        _previousTick = Environment.TickCount64;
        _previousIdle = 0;
        _previousKernel = 0;
        _previousUser = 0;
        _lastVolumeRefresh = 0;
        _lastAdapterRefresh = 0;

        // Topology changes rarely. Enumerating drives and adapters every second
        // is one of the more expensive parts of a driver-free sample, so cache
        // both lists and refresh only free-space and byte counters per tick.
        RefreshVolumes();
        RefreshAdapters();

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

        builder.Readings.Add(new Reading(
            "win/cpu/load", Name, "CPU", "Total", SensorKind.Load,
            SnapshotBuilder.Round(builder.CpuLoadPercent, 3), "%"));

        if (builder.Memory is { } memory)
        {
            builder.Readings.Add(new Reading("win/mem/used", Name, "Memory", "Used", SensorKind.Data, SnapshotBuilder.Round(memory.UsedBytes / 1024d / 1024d / 1024d, 3), "GB"));
            builder.Readings.Add(new Reading("win/mem/available", Name, "Memory", "Available", SensorKind.Data, SnapshotBuilder.Round(memory.AvailableBytes / 1024d / 1024d / 1024d, 3), "GB"));
            builder.Readings.Add(new Reading("win/mem/commit", Name, "Memory", "Commit", SensorKind.Data, SnapshotBuilder.Round(memory.CommitBytes / 1024d / 1024d / 1024d, 3), "GB"));
        }

        builder.Readings.Add(new Reading("win/disk/read", Name, "Disk", "Read", SensorKind.Throughput, SnapshotBuilder.Round(builder.DiskReadBytesPerSecond / 1024d / 1024d, 3), "MB/s"));
        builder.Readings.Add(new Reading("win/disk/write", Name, "Disk", "Write", SensorKind.Throughput, SnapshotBuilder.Round(builder.DiskWriteBytesPerSecond / 1024d / 1024d, 3), "MB/s"));

        if (builder.NetReceiveBytesPerSecond is { } rx && builder.NetSendBytesPerSecond is { } tx)
        {
            builder.Readings.Add(new Reading("win/net/rx", Name, "Network", "Received", SensorKind.Throughput, SnapshotBuilder.Round(rx / 1024d / 1024d, 3), "MB/s"));
            builder.Readings.Add(new Reading("win/net/tx", Name, "Network", "Sent", SensorKind.Throughput, SnapshotBuilder.Round(tx / 1024d / 1024d, 3), "MB/s"));
        }

        _previousTick = now;
    }

    public void Dispose()
    {
        CloseDisks();
        _volumes.Clear();
        _adapters.Clear();
        _previousNetwork.Clear();
    }

    private void CloseDisks()
    {
        foreach (var disk in _disks.Values)
        {
            disk.Dispose();
        }

        _disks.Clear();
    }

    private void SampleCpu(SnapshotBuilder builder)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            builder.Note("cpu load unavailable");
            return;
        }

        var idleNow = idle.ToUInt64();
        var kernelNow = kernel.ToUInt64();
        var userNow = user.ToUInt64();

        // Counters are monotonic in practice, but a reset or a race with a
        // suspended machine can make one go backwards. Never let an unsigned
        // subtraction wrap into a huge bogus delta.
        if (kernelNow < _previousKernel || userNow < _previousUser || idleNow < _previousIdle)
        {
            _previousIdle = idleNow;
            _previousKernel = kernelNow;
            _previousUser = userNow;
            return;
        }

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
        else
        {
            // GetPerformanceInfo is unavailable on a few hardened systems. Fall
            // back to the commit figures that GlobalMemoryStatusEx already gave
            // us instead of displaying zero-byte placeholders.
            limit = status.TotalPageFile;
            commit = Math.Max(0, status.TotalPageFile - status.AvailPageFile);
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

        foreach (var disk in _disks.Values)
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

            if (disk.Initialized)
            {
                read += Math.Max(0, performance.BytesRead - disk.Read);
                write += Math.Max(0, performance.BytesWritten - disk.Write);
            }

            disk.Read = performance.BytesRead;
            disk.Write = performance.BytesWritten;
            disk.Initialized = true;
        }

        builder.DiskReadBytesPerSecond = read / seconds;
        builder.DiskWriteBytesPerSecond = write / seconds;
    }

    private void SampleNetwork(SnapshotBuilder builder, double seconds)
    {
        RefreshAdaptersIfDue();

        long received = 0;
        long sent = 0;
        var sawAdapter = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var adapter in _adapters)
        {
            try
            {
                var stats = adapter.GetIPStatistics();
                sawAdapter = true;
                seen.Add(adapter.Id);

                if (_previousNetwork.TryGetValue(adapter.Id, out var previous))
                {
                    received += Math.Max(0, stats.BytesReceived - previous.Rx);
                    sent += Math.Max(0, stats.BytesSent - previous.Tx);
                }

                _previousNetwork[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
            }
            catch (NetworkInformationException)
            {
                // An adapter can disappear between the topology refresh and this
                // read. Skip it without discarding the other adapters' counters.
            }
            catch (PlatformNotSupportedException)
            {
                // Some virtual stacks expose an adapter but no IP statistics.
            }
        }

        foreach (var stale in _previousNetwork.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _previousNetwork.Remove(stale);
        }

        if (sawAdapter)
        {
            builder.NetReceiveBytesPerSecond = received / seconds;
            builder.NetSendBytesPerSecond = sent / seconds;
        }
    }

    private void RefreshVolumes()
    {
        var next = new List<VolumeInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    next.Add(new VolumeInfo(
                        drive,
                        drive.Name.TrimEnd('\\'),
                        drive.VolumeLabel,
                        drive.TotalSize));
                }
                catch
                {
                    // Skip volumes that disappear mid-enumeration.
                }
            }
        }
        catch
        {
            // Keep the previous topology rather than showing no drives for a
            // full refresh interval after a transient enumeration failure.
            _lastVolumeRefresh = Environment.TickCount64;
            return;
        }

        _volumes.Clear();
        _volumes.AddRange(next);
        _lastVolumeRefresh = Environment.TickCount64;
        SyncDiskCounters();
    }

    private void SyncDiskCounters()
    {
        var (added, removed) = DiffVolumes(
            _disks.Keys,
            _volumes.Select(volume => volume.Name));

        foreach (var stale in removed)
        {
            _disks[stale].Dispose();
            _disks.Remove(stale);
        }

        foreach (var name in added)
        {
            try
            {
                var path = @"\\.\" + name;
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _disks[name] = new DiskCounter(stream);
            }
            catch
            {
                // Volume counters need elevation; skip what we cannot open.
            }
        }
    }

    internal static (string[] Added, string[] Removed) DiffVolumes(
        IEnumerable<string> existing,
        IEnumerable<string> current)
    {
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentSet = current.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (
            currentSet.Where(name => !existingSet.Contains(name)).ToArray(),
            existingSet.Where(name => !currentSet.Contains(name)).ToArray());
    }

    private void SampleVolumes(SnapshotBuilder builder)
    {
        if (Environment.TickCount64 - _lastVolumeRefresh >= VolumeRefreshMs)
        {
            RefreshVolumes();
        }

        foreach (var volume in _volumes)
        {
            try
            {
                builder.Volumes.Add(new VolumeSample(
                    volume.Name,
                    volume.Label,
                    volume.TotalBytes,
                    volume.Drive.TotalFreeSpace));
            }
            catch
            {
                // The drive was removed or became unready. Rebuild the topology
                // on the next tick so free space does not keep failing.
                _lastVolumeRefresh = 0;
            }
        }
    }

    private void RefreshAdapters()
    {
        var next = new List<NetworkInterface>();

        try
        {
            var physical = new List<NetworkInterface>();
            var virtualAdapters = new List<NetworkInterface>();

            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (!IsSupportedAdapterType(adapter.NetworkInterfaceType))
                {
                    continue;
                }

                // Virtual adapters often carry a copy of the host traffic, which
                // double counts a VPN or a Hyper-V switch. Prefer physical
                // adapters when any exist, but keep virtual ones on a VM guest
                // where they are the only network path.
                if (IsVirtualAdapter(adapter.Description, adapter.Name))
                {
                    virtualAdapters.Add(adapter);
                }
                else
                {
                    physical.Add(adapter);
                }
            }

            next.AddRange(physical.Count > 0 ? physical : virtualAdapters);
        }
        catch
        {
            // Keep the previous topology rather than reporting no network for a
            // full refresh interval after a transient enumeration failure.
            _lastAdapterRefresh = Environment.TickCount64;
            return;
        }

        _adapters.Clear();
        _adapters.AddRange(next);
        _lastAdapterRefresh = Environment.TickCount64;
    }

    private void RefreshAdaptersIfDue()
    {
        if (Environment.TickCount64 - _lastAdapterRefresh >= AdapterRefreshMs)
        {
            RefreshAdapters();
        }
    }

    private static bool IsSupportedAdapterType(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or
        NetworkInterfaceType.GigabitEthernet or
        NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.FastEthernetFx or
        NetworkInterfaceType.Wireless80211;

    internal static bool IsVirtualAdapter(string description, string name)
    {
        var text = description + " " + name;
        return text.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase);
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

        // Native DISK_PERFORMANCE ends in WCHAR StorageManagerName[8]. Four
        // uint fields keep the same 16-byte size without pretending it is a Guid.
        public uint StorageManagerName0;
        public uint StorageManagerName1;
        public uint StorageManagerName2;
        public uint StorageManagerName3;
    }

    private sealed record VolumeInfo(DriveInfo Drive, string Name, string Label, double TotalBytes);

    private sealed class DiskCounter : IDisposable
    {
        private readonly FileStream _stream;

        public DiskCounter(FileStream stream) => _stream = stream;

        public SafeFileHandle Handle => _stream.SafeFileHandle;

        public bool Initialized { get; set; }

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
