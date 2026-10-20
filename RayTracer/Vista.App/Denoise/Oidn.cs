using System.Runtime.InteropServices;

namespace Vista.App.Denoise;

/// <summary>
/// The part of Intel Open Image Denoise's C API (<c>OpenImageDenoise/oidn.h</c>, 2.5) Vista calls, by P/Invoke (vista-plan §10
/// M1.6). The DLLs are fetched by <c>tools/fetch-oidn.ps1</c> and copied next to the executable by the build; without them the
/// first call throws <see cref="DllNotFoundException"/>, which <see cref="OidnDenoiser"/> turns into a warning. Handles are
/// opaque pointers; strings are UTF-8; C's <c>bool</c> is one byte.
/// </summary>
internal static partial class Oidn
{
    /// <summary>The library's file name without its extension.</summary>
    public const string Library = "OpenImageDenoise";

    /// <summary><c>OIDNDeviceType</c>.</summary>
    public enum DeviceType
    {
        /// <summary>Chosen by the library.</summary>
        Default = 0,
        /// <summary>The CPU.</summary>
        Cpu = 1,
        /// <summary>SYCL (Intel GPUs).</summary>
        Sycl = 2,
        /// <summary>CUDA (NVIDIA GPUs).</summary>
        Cuda = 3,
        /// <summary>HIP (AMD GPUs).</summary>
        Hip = 4,
        /// <summary>Metal (Apple GPUs).</summary>
        Metal = 5,
    }

    /// <summary><c>OIDNError</c>.</summary>
    public enum Error
    {
        /// <summary>No error occurred.</summary>
        None = 0,
        /// <summary>An unknown error occurred.</summary>
        Unknown = 1,
        /// <summary>An invalid argument was specified.</summary>
        InvalidArgument = 2,
        /// <summary>The operation is not allowed.</summary>
        InvalidOperation = 3,
        /// <summary>Not enough memory to execute the operation.</summary>
        OutOfMemory = 4,
        /// <summary>The hardware (e.g. CPU) is not supported.</summary>
        UnsupportedHardware = 5,
        /// <summary>The operation was cancelled by the user.</summary>
        Cancelled = 6,
    }

    /// <summary><c>OIDNFormat</c> (the float formats only).</summary>
    public enum Format
    {
        /// <summary>Undefined.</summary>
        Undefined = 0,
        /// <summary>One float per pixel.</summary>
        Float = 1,
        /// <summary>Two floats per pixel.</summary>
        Float2 = 2,
        /// <summary>Three floats per pixel.</summary>
        Float3 = 3,
        /// <summary>Four floats per pixel.</summary>
        Float4 = 4,
    }

    /// <summary><c>OIDNQuality</c>.</summary>
    public enum Quality
    {
        /// <summary>The library's default (high).</summary>
        Default = 0,
        /// <summary>High performance, for previews.</summary>
        Fast = 4,
        /// <summary>Balanced, for interactive rendering.</summary>
        Balanced = 5,
        /// <summary>High quality, for final frames.</summary>
        High = 6,
    }

    [LibraryImport(Library, EntryPoint = "oidnGetNumPhysicalDevices")]
    public static partial int GetNumPhysicalDevices();

    [LibraryImport(Library, EntryPoint = "oidnGetPhysicalDeviceInt", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetPhysicalDeviceInt(int physicalDeviceId, string name);

    /// <summary>Returns a string the library owns (do not free); null pointer when there is none.</summary>
    [LibraryImport(Library, EntryPoint = "oidnGetPhysicalDeviceString", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetPhysicalDeviceString(int physicalDeviceId, string name);

    [LibraryImport(Library, EntryPoint = "oidnNewDeviceByID")]
    public static partial IntPtr NewDeviceById(int physicalDeviceId);

    [LibraryImport(Library, EntryPoint = "oidnCommitDevice")]
    public static partial void CommitDevice(IntPtr device);

    [LibraryImport(Library, EntryPoint = "oidnReleaseDevice")]
    public static partial void ReleaseDevice(IntPtr device);

    [LibraryImport(Library, EntryPoint = "oidnGetDeviceInt", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetDeviceInt(IntPtr device, string name);

    /// <summary>
    /// The first error on <paramref name="device"/> since the last call (from this thread), clearing it; a null device asks for
    /// the error of a failed device creation. <paramref name="message"/> is owned by the library.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "oidnGetDeviceError")]
    public static partial Error GetDeviceError(IntPtr device, out IntPtr message);

    [LibraryImport(Library, EntryPoint = "oidnNewBuffer")]
    public static partial IntPtr NewBuffer(IntPtr device, nuint byteSize);

    [LibraryImport(Library, EntryPoint = "oidnReleaseBuffer")]
    public static partial void ReleaseBuffer(IntPtr buffer);

    [LibraryImport(Library, EntryPoint = "oidnWriteBuffer")]
    public static unsafe partial void WriteBuffer(IntPtr buffer, nuint byteOffset, nuint byteSize, void* source);

    [LibraryImport(Library, EntryPoint = "oidnReadBuffer")]
    public static unsafe partial void ReadBuffer(IntPtr buffer, nuint byteOffset, nuint byteSize, void* destination);

    [LibraryImport(Library, EntryPoint = "oidnNewFilter", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr NewFilter(IntPtr device, string type);

    [LibraryImport(Library, EntryPoint = "oidnReleaseFilter")]
    public static partial void ReleaseFilter(IntPtr filter);

    [LibraryImport(Library, EntryPoint = "oidnSetFilterImage", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetFilterImage(IntPtr filter, string name, IntPtr buffer, Format format, nuint width, nuint height,
        nuint byteOffset, nuint pixelByteStride, nuint rowByteStride);

    [LibraryImport(Library, EntryPoint = "oidnSetFilterBool", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetFilterBool(IntPtr filter, string name, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(Library, EntryPoint = "oidnSetFilterInt", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetFilterInt(IntPtr filter, string name, int value);

    [LibraryImport(Library, EntryPoint = "oidnSetFilterFloat", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetFilterFloat(IntPtr filter, string name, float value);

    [LibraryImport(Library, EntryPoint = "oidnCommitFilter")]
    public static partial void CommitFilter(IntPtr filter);

    [LibraryImport(Library, EntryPoint = "oidnExecuteFilter")]
    public static partial void ExecuteFilter(IntPtr filter);
}
