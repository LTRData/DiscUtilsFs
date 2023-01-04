using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtilsFs;

public unsafe partial class PhysicalDeviceStream : FileStream
{
    #region "Native calls and structs"
    private static partial class NativeCalls
    {
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct DISK_GEOMETRY
        {
            public enum MEDIA_TYPE : int
            {
                Unknown = 0x0,
                F5_1Pt2_512 = 0x1,
                F3_1Pt44_512 = 0x2,
                F3_2Pt88_512 = 0x3,
                F3_20Pt8_512 = 0x4,
                F3_720_512 = 0x5,
                F5_360_512 = 0x6,
                F5_320_512 = 0x7,
                F5_320_1024 = 0x8,
                F5_180_512 = 0x9,
                F5_160_512 = 0xA,
                RemovableMedia = 0xB,
                FixedMedia = 0xC,
                F3_120M_512 = 0xD,
                F3_640_512 = 0xE,
                F5_640_512 = 0xF,
                F5_720_512 = 0x10,
                F3_1Pt2_512 = 0x11,
                F3_1Pt23_1024 = 0x12,
                F5_1Pt23_1024 = 0x13,
                F3_128Mb_512 = 0x14,
                F3_230Mb_512 = 0x15,
                F8_256_128 = 0x16,
                F3_200Mb_512 = 0x17,
                F3_240M_512 = 0x18,
                F3_32M_512 = 0x19
            }

            public DISK_GEOMETRY(long cylinders, MEDIA_TYPE mediaType, int tracksPerCylinder, int sectorsPerTrack, int bytesPerSector)
            {
                Cylinders = cylinders;
                MediaType = mediaType;
                TracksPerCylinder = tracksPerCylinder;
                SectorsPerTrack = sectorsPerTrack;
                BytesPerSector = bytesPerSector;
            }

            public long Cylinders { get; }
            public MEDIA_TYPE MediaType { get; }
            public int TracksPerCylinder { get; }
            public int SectorsPerTrack { get; }
            public int BytesPerSector { get; }
        }

#if NET7_0_OR_GREATER
        [LibraryImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeviceIoControl(SafeFileHandle handle,
                                                   int ctrlCode,
                                                   void* input,
                                                   int intputSize,
                                                   void* output,
                                                   int outputSize,
                                                   out int outSize,
                                                   nint overlapped);

        [LibraryImport("c", SetLastError = true)]
        public static partial int ioctl(SafeFileHandle handle,
                                        long ctrlCode,
                                        void* data);
#else
        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(SafeFileHandle handle,
                                                  int ctrlCode,
                                                  void* input,
                                                  int intputSize,
                                                  void* output,
                                                  int outputSize,
                                                  out int outSize,
                                                  nint overlapped);

        [DllImport("c", SetLastError = true)]
        public static extern int ioctl(SafeFileHandle handle,
                                       long ctrlCode,
                                       void* data);
#endif

        // Windows
        public const int IOCTL_DISK_GET_LENGTH_INFO = 0x7405C;
        public const int IOCTL_DISK_GET_DRIVE_GEOMETRY = 0x70000;

        // FreeBSD
        public const int DIOCGMEDIASIZE = 0x40086481;
        public const int DIOCGSECTORSIZE = 0x40046480;

        // Linux
        public const int BLKGETSIZE64 = unchecked((int)0x80081272);
        public const int BLKSSZGET = 0x1268;
    }
    #endregion

    public PhysicalDeviceStream(string path, FileAccess access)
        : base(path, FileMode.Open, access, FileShare.Read, bufferSize: 1)
    {
        Length = GetLength();
        SectorSize = GetSectorSize();
    }

    public override long Length { get; }

    private long GetLength()
    {
        long length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var rc = NativeCalls.DeviceIoControl(SafeFileHandle,
                                                 NativeCalls.IOCTL_DISK_GET_LENGTH_INFO,
                                                 null,
                                                 0,
                                                 &length,
                                                 sizeof(long),
                                                 out _,
                                                 0);

            if (!rc)
            {
                throw new Win32Exception();
            }

            return length;
        }
#if NETCOREAPP
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            var rc = NativeCalls.ioctl(SafeFileHandle,
                                       NativeCalls.DIOCGMEDIASIZE,
                                       &length);

            if (rc != 0)
            {
                throw new Win32Exception();
            }

            return length;
        }
#endif
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var rc = NativeCalls.ioctl(SafeFileHandle,
                                       NativeCalls.BLKGETSIZE64,
                                       &length);

            if (rc != 0)
            {
                throw new Win32Exception();
            }

            return length;
        }
        else
        {
            Console.WriteLine("Warning: Disk size query not supported on this platform. Assuming end seek position as length.");
            
            length = Seek(0, SeekOrigin.End);
            
            Seek(0, SeekOrigin.Begin);

            return length;
        }
    }

    public virtual int SectorSize { get; }

    private int GetSectorSize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NativeCalls.DISK_GEOMETRY disk_geometry;

            var rc = NativeCalls.DeviceIoControl(SafeFileHandle,
                                                 NativeCalls.IOCTL_DISK_GET_DRIVE_GEOMETRY,
                                                 null,
                                                 0,
                                                 &disk_geometry,
                                                 sizeof(long),
                                                 out _,
                                                 0);

            if (!rc)
            {
                throw new Win32Exception();
            }

            return disk_geometry.BytesPerSector;
        }
#if NETCOREAPP
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            int size;

            var rc = NativeCalls.ioctl(SafeFileHandle,
                                       NativeCalls.DIOCGSECTORSIZE,
                                       &size);

            if (rc != 0)
            {
                throw new Win32Exception();
            }

            return size;
        }
#endif
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            int size;

            var rc = NativeCalls.ioctl(SafeFileHandle,
                                       NativeCalls.BLKSSZGET,
                                       &size);

            if (rc != 0)
            {
                throw new Win32Exception();
            }

            return size;
        }
        else
        {
            Console.WriteLine("Warning: Disk sector size query not supported on this platform, assuming 512 bytes.");
            return 512;
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();
}
