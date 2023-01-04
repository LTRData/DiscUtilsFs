# DiscUtilsFs

This application mounts a file system from a disk image file as a virtual file system.

On Windows, it uses Dokan to mount the virtual file system and on FreeBSD and Linux it uses Fuse.

Image file formats and file system structures are accessed using DiscUtils library on all platforms.
