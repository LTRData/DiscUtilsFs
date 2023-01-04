using DiscUtils;
using DiscUtils.Dokan;
using DiscUtils.Fuse;
using DiscUtils.Streams;
using DiscUtils.VirtualFileSystem;
using DokanNet;
using FuseDotNet;
using FuseDotNet.Extensions;
using FuseDotNet.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0057 // Use range operator

namespace DiscUtilsFs;

internal static class DiscUtilsSupport
{
    private static readonly Assembly[] Asms =
    {
        typeof(DiscUtils.Btrfs.BtrfsFileSystem).Assembly,
        typeof(DiscUtils.Ext.ExtFileSystem).Assembly,
        typeof(DiscUtils.Fat.FatFileSystem).Assembly,
        typeof(DiscUtils.HfsPlus.HfsPlusFileSystem).Assembly,
        typeof(DiscUtils.Iso9660.CDReader).Assembly,
        typeof(DiscUtils.Lvm.LogicalVolumeManager).Assembly,
        typeof(DiscUtils.Nfs.NfsFileSystem).Assembly,
        typeof(DiscUtils.Ntfs.NtfsFileSystem).Assembly,
        typeof(DiscUtils.Registry.RegistryHive).Assembly,
        typeof(DiscUtils.SquashFs.SquashFileSystemReader).Assembly,
        typeof(DiscUtils.Swap.SwapFileSystem).Assembly,
        typeof(DiscUtils.Udf.UdfReader).Assembly,
        typeof(DiscUtils.Vdi.Disk).Assembly,
        typeof(DiscUtils.Vhd.Disk).Assembly,
        typeof(DiscUtils.Vhdx.Disk).Assembly,
        typeof(DiscUtils.VirtualFileSystem.TarFileSystem).Assembly,
        typeof(DiscUtils.Wim.WimFileSystem).Assembly,
        typeof(DiscUtils.Vmdk.Disk).Assembly,
        typeof(DiscUtils.Xfs.XfsFileSystem).Assembly,
        typeof(ExFat.DiscUtils.ExFatFileSystem).Assembly
    };

    public static void RegisterAssemblies()
    {
        foreach (var asm in Asms.Distinct())
        {
            DiscUtils.Setup.SetupHelper.RegisterAssembly(asm);
        }
    }
}

public static class Program
{
    private const string VhdKey = "--vhd";
    private const string PartKey = "--part";
    private const string FsKey = "--fs";
    private const string TmpKey = "--tmp";
    private const string DebugKey = "-d";
    private const string VersionKey = "-V";
    private const string WritableKey = "-w";
    private const string MetaFilesKey = "-m";
    private const string NoExecKey = "--noexec";

    public static int Main(params string[] args)
    {
        ConsoleLogger? logger = null;

        try
        {
            DiscUtilsSupport.RegisterAssemblies();

            var arguments = args
                .Where(x => x.StartsWith("-", StringComparison.Ordinal))
                .Select(x =>
                {
                    var pos = x.IndexOf('=');

                    if (pos < 0)
                    {
                        return new KeyValuePair<string, string?>(x, null);
                    }
                    else
                    {
                        return new KeyValuePair<string, string?>(x.Remove(pos), x.Substring(pos + 1));
                    }
                })
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

            var fuse_args = args.Where(arg =>
                !arg.StartsWith(VhdKey, StringComparison.Ordinal) &&
                !arg.StartsWith(TmpKey, StringComparison.Ordinal) &&
                !arg.StartsWith(FsKey, StringComparison.Ordinal) &&
                !arg.StartsWith(PartKey, StringComparison.Ordinal) &&
                !arg.StartsWith(NoExecKey, StringComparison.Ordinal) &&
                !arg.StartsWith(MetaFilesKey, StringComparison.Ordinal) &&
                !arg.StartsWith(WritableKey, StringComparison.Ordinal))
                .Prepend(typeof(Program).Assembly.GetName().Name ?? "DiscUtilsFs");

            var dokan_options = default(DokanOptions);

            var mount_point = args
                .Where(arg => !arg.StartsWith("-", StringComparison.Ordinal))
                .LastOrDefault();

            if (arguments.ContainsKey(VersionKey))
            {
                Console.WriteLine($"DiscUtilsFs version: {typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version}");
            }

            var access = arguments.ContainsKey(WritableKey)
                ? FileAccess.ReadWrite
                : FileAccess.Read;

            if (arguments.ContainsKey(DebugKey))
            {
                logger = new ConsoleLogger("[DiscUtilsFs]");
                logger.Debug($"Initialized console logger");

                dokan_options |= DokanOptions.DebugMode;
            }

            IFileSystem? file_system;

            if (arguments.TryGetValue(VhdKey, out var vhdPath) &&
                vhdPath is not null &&
                arguments.TryGetValue(PartKey, out var partNoStr) &&
                partNoStr is not null)
            {
                file_system = InitializeFromVhd(vhdPath, partNoStr, access);
            }
            else if (arguments.TryGetValue(FsKey, out var fsPath) &&
                fsPath is not null)
            {
                file_system = InitializeFromFsImage(fsPath, access);
            }
            else if (arguments.ContainsKey(TmpKey))
            {
                file_system = InitializeTmpFs();
            }
            else
            {
                Console.WriteLine(@"DiscUtilsFs
Mount file systems in disk images files using DiscUtils disk image and file
system implementations in user mode.

Syntax:
DiscUtilsFs --tmp [fuseoptions] mountdir
DiscUtilsFs --vhd=image [--part=number] [-w] [-m] [fuseoptions] mountdir
DiscUtilsFs --fs=image [-w] [-m] [fuseoptions] mountdir

--tmp       Creates a temporary file system with in-memory file allocation
            that only last while the file system is mounted.

--vhd       Opens a disk image file. Supported formats are currently vhd,
            vhdx, vdi, vmdk, dmg and raw formats.

--part      Number of partition (1-based) to mount within image, or 0 to mount
            a file system that spans the entire image (no partition table).

--fs        Opens a raw file system image file.

-d          Debug messag output to terminal. Implies -f.

-f          Foreground operation.

-m          For NTFS, expose meta files as normal files.

-w          Writable mode, for file system implementations that support it.
            Allows modifications to file system.

fuseoptions Options to pass on to fuse, for example -o noforget.

mountdir    Directory where to mount the file system.
");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return -1;
                }
                else
                {
                    return (int)Fuse.CallMain(fuse_args);
                }
            }

            if (file_system == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("No supported file system found.");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"File system type: {file_system.GetType().Name}");

            if (file_system.CanWrite)
            {
                logger?.Info($"Read-write file system");
            }
            else
            {
                logger?.Info($"Read-only file system");

                fuse_args = fuse_args
                    .Append("-o").Append("ro,noforget,kernel_cache");

                dokan_options |= DokanOptions.WriteProtection;
            }

            if (file_system.IsThreadSafe)
            {
                logger?.Info($"Multi-thread aware file system");
            }
            else
            {
                logger?.Info($"File system requires single thread operation");

                fuse_args = fuse_args
                    .Append("-s");
            }

            if (file_system is DiscUtils.Ntfs.NtfsFileSystem ntfs)
            {
                ntfs.NtfsOptions.HideHiddenFiles = false;
                ntfs.NtfsOptions.HideSystemFiles = false;

                if (arguments.ContainsKey(MetaFilesKey))
                {
                    ntfs.NtfsOptions.HideMetafiles = false;
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DokanDiscUtilsOptions options = default;

                if (arguments.ContainsKey(NoExecKey))
                {
                    options |= DokanDiscUtilsOptions.BlockExecute;
                }

                using var dokan_discutils = new DokanDiscUtils(file_system, options);

                if (dokan_discutils.NamedStreams)
                {
                    dokan_options |= DokanOptions.AltStream;
                }

                logger?.Debug($"Now calling Dokan to mount the file system.");

                try
                {
                    Dokan.Init();

                    dokan_discutils.Mount(mount_point, dokan_options, !file_system.IsThreadSafe, logger);

                    logger?.Debug($"Dismounted.");
                }
                catch (DokanException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($@"Error: {ex.JoinMessages()}");
                    Console.ResetColor();

                    return ex.HResult;
                }
            }
            else
            {
                FuseDiscUtilsOptions options = default;

                if (arguments.ContainsKey(NoExecKey))
                {
                    options |= FuseDiscUtilsOptions.BlockExecute;
                }

                using var fuse_discutils = new FuseDiscUtils(file_system, options, logger);

                logger?.Debug($"Now calling Fuse to mount the file system.");

                try
                {
                    fuse_discutils.Mount(fuse_args, logger);

                    logger?.Debug($"Dismounted.");
                }
                catch (PosixException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($@"Error: {ex.JoinMessages()}");
                    Console.ResetColor();

                    return (int)ex.NativeErrorCode;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger?.Error($"{ex}");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($@"Error: {ex.JoinMessages()}");
            Console.ResetColor();

            return (int)ex.ToPosixResult();
        }
    }

    private static IFileSystem InitializeTmpFs()
    {
        IFileSystem file_system;

        var vfs = new VirtualFileSystem(new VirtualFileSystemOptions
        {
            HasSecurity = false,
            IsThreadSafe = false,
            VolumeLabel = "VirtualFs"
        });

#if SAMPLE_FILE

        var stream = new MemoryStream();
        var bytes = Encoding.Default.GetBytes("HELLO WORLD!\n\n");
        stream.Write(bytes);

        vfs.AddFile("subdir" + Path.DirectorySeparatorChar + "file.txt", (mode, access) => SparseStream.FromStream(stream, Ownership.None))
            .Length = stream.Length;

        vfs.UpdateUsedSpace();

#endif

        vfs.CreateFile += (sender, e) => e.Result = vfs.AddFile(e.Path,
                                                                new SparseMemoryStream(new(65536), FileAccess.ReadWrite),
                                                                DateTime.UtcNow,
                                                                DateTime.UtcNow,
                                                                DateTime.UtcNow,
                                                                FileAttributes.Normal);

        file_system = vfs;

        return file_system;
    }

    private static IFileSystem? InitializeFromFsImage(string fsPath, FileAccess access)
    {
        if (string.IsNullOrWhiteSpace(fsPath))
        {
            throw new InvalidOperationException($"Missing value for argument: {FsKey}");
        }

        var part_content = IsDevicePath(fsPath)
            ? OpenDevice(fsPath, access)
            : File.Open(fsPath, FileMode.Open, access);

        if (Path.GetExtension(fsPath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return new DiscUtils.Iso9660.CDReader(part_content, joliet: true);
        }
        else
        {
            return FileSystemManager.DetectFileSystems(part_content).FirstOrDefault()?.Open(part_content);
        }
    }

    private static IFileSystem? InitializeFromVhd(string vhdPath, string partNoStr, FileAccess access)
    {
        if (string.IsNullOrWhiteSpace(vhdPath))
        {
            throw new InvalidOperationException($"Missing value for argument: {VhdKey}");
        }

        if (!int.TryParse(partNoStr, out var partNo))
        {
            throw new ArgumentException($"Invalid partition number: {partNoStr}");
        }

        var disk = IsDevicePath(vhdPath)
            ? new DiscUtils.Raw.Disk(OpenDevice(vhdPath, access), Ownership.Dispose)
            : VirtualDisk.OpenDisk(vhdPath, access) ??
                new DiscUtils.Raw.Disk(vhdPath, access);

        Console.WriteLine($"Opened image '{vhdPath}', type {disk.DiskTypeInfo.Name}");

        var partitions = disk.Partitions;

        if (partNo > 0 && (partitions == null || partNo > partitions.Count))
        {
            throw new DriveNotFoundException($"Partition {partNo} not found");
        }

        if (partitions == null || partNo == 0 || partitions.Count == 0)
        {
            var disk_content = disk.Content;

            return FileSystemManager.DetectFileSystems(disk_content).FirstOrDefault()?.Open(disk_content);
        }
        else
        {
            Console.WriteLine($"Found partition table, type {partitions.GetType().Name}");

            var part = partitions[partNo - 1];

            Console.WriteLine($"Found partition type {part.TypeAsString}");

            var part_content = part.Open();

            return FileSystemManager.DetectFileSystems(part_content).FirstOrDefault()?.Open(part_content);
        }
    }

    private static Stream OpenDevice(string fsPath, FileAccess access)
    {
        var diskStream = new PhysicalDeviceStream(fsPath, access);
        var deviceSize = diskStream.Length;
        var sectorSize = diskStream.SectorSize;

        Console.WriteLine($"Device '{fsPath}', {deviceSize} bytes total, {sectorSize} bytes per sector");

        var sparseStream = SparseStream.FromStream(diskStream, Ownership.Dispose);
        var aligningStream = new AligningStream(sparseStream, Ownership.Dispose, sectorSize);
        return aligningStream;
    }

    private static bool IsDevicePath(string vhdPath)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && (vhdPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            || vhdPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            && vhdPath.IndexOf('\\', 4) < 0
            || vhdPath.StartsWith("/dev/", StringComparison.Ordinal);

    private static string JoinMessages(this Exception ex)
        => string.Join(Environment.NewLine, ex.EnumerateMessages());

    private static IEnumerable<string> EnumerateMessages(this Exception? ex)
    {
        while (ex is not null)
        {
            yield return ex.Message;
            ex = ex.InnerException;
        }
    }
}
