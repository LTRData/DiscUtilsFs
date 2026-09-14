# DiscUtilsFs

Mount a file system from a disk image as a virtual file system. DiscUtils reads the image and file-system structures; Dokan exposes the mounted view on Windows, and FUSE exposes it on Linux and FreeBSD.

This is a file-system mount tool. It also provides temporary in-memory and discard file systems.

## Requirements and build

| Platform | Mount backend | Native requirements |
| --- | --- | --- |
| Windows | [DiscUtils.MountDokan](https://github.com/LTRData/DiscUtils/tree/LTRData.DiscUtils-initial/Integrations/DiscUtils.MountDokan) | A compatible Dokan 2.x driver/runtime installed separately. |
| Linux | [DiscUtils.MountFuse](https://github.com/LTRData/DiscUtils/tree/LTRData.DiscUtils-initial/Integrations/DiscUtils.MountFuse) | FUSE 3, kernel FUSE support and mount permissions. |
| FreeBSD | DiscUtils.MountFuse | FUSE 3 and kernel FUSE support; use a modern .NET build on x64. |

See [FuseDotNet](https://github.com/LTRData/FuseDotNet) for its native platform mappings and library-loading requirements. Access to raw devices also requires the appropriate device permissions.

Use the .NET 10 SDK for the current source tree:

```sh
dotnet build DiscUtilsFs/DiscUtilsFs.csproj -c Release -f net10.0
```

The project targets `net10.0`, `net9.0`, `net8.0`, `netstandard2.1` and `net48`. Select a runnable framework and install a compatible runtime on the target machine; .NET Standard is a compatibility target, not a standalone runtime. FreeBSD handling is compiled into the modern .NET targets.

Build outputs are placed under `Release/<framework>/`. The project restores `LTRData.DiscUtils`, `LTRData.DiscUtils.MountDokan` and `LTRData.DiscUtils.MountFuse` from NuGet.

## Mount modes

Choose one source mode and put the mount point last. Option names are case-sensitive. Supply option values in the same argument, such as `--vhd=disk.vhdx`, rather than as separate arguments.

| Source options | Behavior |
| --- | --- |
| `--vhd=image --part=N` | Opens a disk image using DiscUtils disk detection, with a raw-disk fallback. Despite the option name, this is not restricted to VHD. |
| `--fs=image` | Opens an image containing a file system directly, without selecting a volume. Files ending in `.iso` are checked for UDF before ISO 9660/Joliet. |
| `--wim=image --index=N` | Opens an image inside a WIM file. The index is **one-based**. |
| `--tmp` | Creates an in-memory file system. Its contents last only for the mount's lifetime. |
| `--discard` | Creates a file system whose file data is discarded through `Stream.Null`. |

`--part` is required with `--vhd`. Positive values select the **one-based logical-volume order returned by DiscUtils**, which need not equal an OS partition number. Use `--part=0` to detect a file system across the entire disk content.

Supported image/file-system formats and write capabilities come from the restored DiscUtils packages. See the [DiscUtils capability matrix](https://github.com/LTRData/DiscUtils). The mount adapters can expose fewer operations than the underlying file system supports.

## Examples

From the repository root, mount the first logical volume of a disk image on Windows at an unused drive letter:

```powershell
dotnet .\Release\net10.0\DiscUtilsFs.dll --vhd=C:\Images\disk.vhdx --part=1 R:\
```

On Linux or FreeBSD, use an existing empty mount directory and foreground operation:

```sh
dotnet Release/net10.0/DiscUtilsFs.dll --vhd=disk.vhdx --part=1 -f /path/to/mountpoint
```

Other source modes:

```sh
dotnet Release/net10.0/DiscUtilsFs.dll --fs=disc.iso -f /path/to/mountpoint
dotnet Release/net10.0/DiscUtilsFs.dll --wim=install.wim --index=1 -f /path/to/mountpoint
dotnet Release/net10.0/DiscUtilsFs.dll --tmp -f /path/to/mountpoint
```

Quote an entire argument when its path contains spaces, for example `"--vhd=C:\Disk Images\disk.vhdx"`.

The image modes open their backing files read-only by default. **Adding `-w` requests direct write access to the backing image or device; there is no temporary overlay or commit/discard step.** Writes require a writable disk/file-system implementation and adapter support. If the opened file system reports that it cannot write, the tool mounts it read-only and prints a warning.

The `--tmp` and `--discard` modes create writable virtual file systems without requiring `-w`.

## Other options

| Option | Meaning |
| --- | --- |
| `-d` | Enables diagnostic output; on FUSE it also implies foreground operation. |
| `-f` | Keeps FUSE in the foreground. The Windows Dokan path already waits for the mount to close. |
| `-m` | Exposes NTFS metadata files. Hidden and system files are already made visible for NTFS. |
| `-n` | Windows only: requests a Dokan network drive. |
| `--noexec` | Requests execute blocking through the selected adapter's `BlockExecute` option, subject to adapter support. |
| `-V` | Prints the application version before continuing normal argument processing. |
| FUSE options | Remaining arguments are forwarded to FUSE on Linux/FreeBSD, for example `-o noforget`. |

Each option key may occur only once in the supplied arguments. Combine multiple FUSE mount options in one comma-separated `-o` value.

Paths under `/dev/`, and Windows device paths such as `\\.\PhysicalDrive1`, are recognized by the raw-device path handling. `--fs` treats the device as a file-system volume; `--vhd` uses disk/volume detection.

## Unmounting

On Windows, press **Ctrl+C** in the application's console to request removal of the Dokan mount.

For a Linux mount owned by the current user, run this from another terminal:

```sh
fusermount3 -u /path/to/mountpoint
```

On FreeBSD, or with the required OS privileges:

```sh
umount /path/to/mountpoint
```

## License

DiscUtilsFs is distributed under the [MIT License](LICENSE.txt). Its dependencies retain their own licenses.
