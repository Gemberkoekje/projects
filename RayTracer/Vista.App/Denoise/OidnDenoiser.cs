using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Vista.Output;

namespace Vista.App.Denoise;

/// <summary>
/// <see cref="IDenoiser"/> on Intel Open Image Denoise (vista-plan §5.7, §10 M1.6): its <c>RT</c> filter on the HDR beauty with
/// the first hit's albedo and normal, on the CPU or a CUDA GPU. Nothing loads until the first <see cref="Denoise"/>, so a run
/// that never denoises never needs the library; when it is missing, or a device or filter fails, the result says why.
///
/// <para>A device is created once per kind and kept; the last filter (with its buffers) is kept too and reused while the image
/// size and settings stay the same, as a contact sheet's cells do. Not thread-safe: one caller at a time.</para>
/// </summary>
internal sealed class OidnDenoiser : IDenoiser, IDisposable
{
    /// <summary>Recorded in <see cref="DenoiserRun.Library"/>.</summary>
    public const string LibraryName = "Intel Open Image Denoise";

    /// <summary>The warning when the DLLs are not next to the executable.</summary>
    public const string MissingLibrary =
        "Intel Open Image Denoise is not next to Vista.App (OpenImageDenoise.dll not found): run tools/fetch-oidn.ps1, then rebuild Vista.App.";

    private readonly Dictionary<DenoiseDevice, OidnDevice> _devices = [];
    private PreparedFilter? _filter;

    /// <inheritdoc />
    public DenoiseResult Denoise(DenoiseInput input, DenoiseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        int values = input.Pixels * 3;
        if (input.Color.Length != values || input.Albedo.Length != values || input.Normal.Length != values)
            throw new ArgumentException($"Every image must hold {values} floats.", nameof(input));
        try
        {
            OidnDevice device = DeviceFor(settings.Device);
            var key = new FilterKey(settings.Device, input.Width, input.Height, settings.Quality, settings.Auxiliary, BitConverter.SingleToInt32Bits(input.InputScale));
            double commitSeconds = 0.0;
            if (_filter?.Key != key)
            {
                _filter?.Dispose();
                _filter = null;
                var commit = Stopwatch.StartNew();
                _filter = new PreparedFilter(device, key, input.InputScale);
                commitSeconds = commit.Elapsed.TotalSeconds;
            }
            var execute = Stopwatch.StartNew();
            float[] output = _filter.Execute(input);
            double executeSeconds = execute.Elapsed.TotalSeconds;
            return DenoiseResult.Denoised(output, new DenoiserRun(LibraryName, device.Version, device.Description, _filter.Description,
                float.IsFinite(input.InputScale) ? input.InputScale : null, commitSeconds, executeSeconds));
        }
        catch (DllNotFoundException)
        {
            return DenoiseResult.Failed(MissingLibrary);
        }
        catch (EntryPointNotFoundException e)
        {
            return DenoiseResult.Failed($"The Intel Open Image Denoise next to Vista.App lacks a function Vista calls ({e.Message}): run tools/fetch-oidn.ps1, then rebuild Vista.App.");
        }
        catch (OidnException e)
        {
            _filter?.Dispose();
            _filter = null;
            return DenoiseResult.Failed(e.Message);
        }
    }

    /// <summary>Releases every filter, buffer and device.</summary>
    public void Dispose()
    {
        _filter?.Dispose();
        _filter = null;
        foreach (OidnDevice device in _devices.Values)
            device.Dispose();
        _devices.Clear();
    }

    private OidnDevice DeviceFor(DenoiseDevice kind)
    {
        if (!_devices.TryGetValue(kind, out OidnDevice? device))
        {
            device = OidnDevice.Open(kind);
            _devices[kind] = device;
        }
        return device;
    }

    /// <summary>What a prepared filter was made for; a different key needs a new one.</summary>
    private readonly record struct FilterKey(DenoiseDevice Device, int Width, int Height, DenoiseQuality Quality, DenoiseAuxiliary Auxiliary, int InputScaleBits);

    /// <summary>An OIDN error, carried to <see cref="Denoise"/>, which turns it into a failed result.</summary>
    private sealed class OidnException(string message) : Exception(message);

    /// <summary>One logical device and what the sidecar records about it.</summary>
    private sealed class OidnDevice : IDisposable
    {
        private OidnDevice(IntPtr handle, string version, string description)
        {
            Handle = handle;
            Version = version;
            Description = description;
        }

        public IntPtr Handle { get; private set; }

        /// <summary>The loaded library's version, e.g. "2.5.1".</summary>
        public string Version { get; }

        /// <summary>The device kind and its name, e.g. "CUDA: NVIDIA GeForce RTX 3070".</summary>
        public string Description { get; }

        /// <summary>Creates and commits a device on the first physical device of <paramref name="kind"/>.</summary>
        public static OidnDevice Open(DenoiseDevice kind)
        {
            Oidn.DeviceType type = kind switch
            {
                DenoiseDevice.Cpu => Oidn.DeviceType.Cpu,
                DenoiseDevice.Cuda => Oidn.DeviceType.Cuda,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown denoise device."),
            };
            string typeName = type.ToString().ToUpperInvariant();
            int count = Oidn.GetNumPhysicalDevices();
            int id = -1;
            string name = string.Empty;
            for (int i = 0; i < count && id < 0; i++)
            {
                if (Oidn.GetPhysicalDeviceInt(i, "type") == (int)type)
                {
                    id = i;
                    name = Marshal.PtrToStringUTF8(Oidn.GetPhysicalDeviceString(i, "name")) ?? string.Empty;
                }
            }
            if (id < 0)
                throw new OidnException($"Intel Open Image Denoise finds no {typeName} device here ({count} devices in all).");

            IntPtr handle = Oidn.NewDeviceById(id);
            if (handle == IntPtr.Zero)
            {
                ThrowOnError(IntPtr.Zero, $"creating the {typeName} device");
                throw new OidnException($"Intel Open Image Denoise failed creating the {typeName} device, and gave no reason.");
            }
            try
            {
                Oidn.CommitDevice(handle);
                ThrowOnError(handle, $"committing the {typeName} device");
                int version = Oidn.GetDeviceInt(handle, "version");
                return new OidnDevice(handle,
                    string.Create(CultureInfo.InvariantCulture, $"{version / 10000}.{version / 100 % 100}.{version % 100}"),
                    $"{typeName}: {name.Trim()}");
            }
            catch
            {
                Oidn.ReleaseDevice(handle);
                throw;
            }
        }

        /// <summary>Throws this device's pending error, if any, naming what was being done.</summary>
        public void ThrowOnError(string doing) => ThrowOnError(Handle, doing);

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                Oidn.ReleaseDevice(Handle);
            Handle = IntPtr.Zero;
        }

        /// <summary>Throws the pending error of <paramref name="device"/> (null: of a failed device creation), if any.</summary>
        private static void ThrowOnError(IntPtr device, string doing)
        {
            Oidn.Error error = Oidn.GetDeviceError(device, out IntPtr message);
            if (error != Oidn.Error.None)
                throw new OidnException($"Intel Open Image Denoise failed {doing}: {error}: {Marshal.PtrToStringUTF8(message) ?? "no message"}.");
        }
    }

    /// <summary>
    /// The filters and buffers for one image size and setting: the <c>RT</c> filter on the beauty and, when the auxiliary
    /// images are prefiltered, one <c>RT</c> filter each on the albedo and the normal first.
    /// </summary>
    private sealed class PreparedFilter : IDisposable
    {
        private readonly OidnDevice _device;
        private readonly nuint _bytes;
        private readonly List<IntPtr> _buffers = [];
        private readonly List<IntPtr> _filters = [];
        private readonly IntPtr _color, _albedo, _normal, _output;

        public PreparedFilter(OidnDevice device, FilterKey key, float inputScale)
        {
            _device = device;
            Key = key;
            _bytes = (nuint)((long)key.Width * key.Height * 3 * sizeof(float));
            try
            {
                _color = Buffer();
                _albedo = Buffer();
                _normal = Buffer();
                _output = Buffer();
                Oidn.Quality quality = key.Quality switch
                {
                    DenoiseQuality.High => Oidn.Quality.High,
                    DenoiseQuality.Balanced => Oidn.Quality.Balanced,
                    DenoiseQuality.Fast => Oidn.Quality.Fast,
                    _ => throw new ArgumentOutOfRangeException(nameof(key), key.Quality, "Unknown denoise quality."),
                };
                string qualityName = key.Quality.ToString().ToLowerInvariant();

                IntPtr albedo = _albedo, normal = _normal;
                if (key.Auxiliary == DenoiseAuxiliary.Prefiltered)
                {
                    albedo = Buffer();
                    normal = Buffer();
                    Prefilter("albedo", _albedo, albedo, quality);
                    Prefilter("normal", _normal, normal, quality);
                }
                IntPtr beauty = Filter();
                Image(beauty, "color", _color);
                Image(beauty, "albedo", albedo);
                Image(beauty, "normal", normal);
                Image(beauty, "output", _output);
                Oidn.SetFilterBool(beauty, "hdr", true);
                Oidn.SetFilterInt(beauty, "quality", (int)quality);
                bool clean = key.Auxiliary is DenoiseAuxiliary.Clean or DenoiseAuxiliary.Prefiltered;
                Oidn.SetFilterBool(beauty, "cleanAux", clean);
                if (float.IsFinite(inputScale))
                    Oidn.SetFilterFloat(beauty, "inputScale", inputScale);
                Oidn.CommitFilter(beauty);
                _device.ThrowOnError("preparing the RT filter");

                string scale = float.IsFinite(inputScale) ? string.Create(CultureInfo.InvariantCulture, $"inputScale {inputScale:G6}") : "inputScale auto";
                Description = $"RT: hdr, quality {qualityName}, {(clean ? "cleanAux" : "noisy aux")}, {scale}"
                    + (key.Auxiliary == DenoiseAuxiliary.Prefiltered ? $"; albedo and normal prefiltered by RT (quality {qualityName})" : string.Empty);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public FilterKey Key { get; }

        /// <summary>The filter and its parameters, for the sidecar.</summary>
        public string Description { get; } = string.Empty;

        /// <summary>Uploads <paramref name="input"/>, runs the filters in order, and downloads the denoised beauty.</summary>
        public unsafe float[] Execute(DenoiseInput input)
        {
            Write(_color, input.Color);
            Write(_albedo, input.Albedo);
            Write(_normal, input.Normal);
            foreach (IntPtr filter in _filters)
            {
                Oidn.ExecuteFilter(filter);
                _device.ThrowOnError("filtering");
            }
            var output = new float[input.Pixels * 3];
            fixed (float* destination = output)
                Oidn.ReadBuffer(_output, 0, _bytes, destination);
            _device.ThrowOnError("reading the denoised image back");
            return output;
        }

        public void Dispose()
        {
            foreach (IntPtr filter in _filters)
                Oidn.ReleaseFilter(filter);
            _filters.Clear();
            foreach (IntPtr buffer in _buffers)
                Oidn.ReleaseBuffer(buffer);
            _buffers.Clear();
        }

        private unsafe void Write(IntPtr buffer, float[] data)
        {
            fixed (float* source = data)
                Oidn.WriteBuffer(buffer, 0, _bytes, source);
            _device.ThrowOnError("uploading an image");
        }

        private IntPtr Buffer()
        {
            IntPtr buffer = Oidn.NewBuffer(_device.Handle, _bytes);
            _device.ThrowOnError("allocating a buffer");
            _buffers.Add(buffer);
            return buffer;
        }

        private IntPtr Filter()
        {
            IntPtr filter = Oidn.NewFilter(_device.Handle, "RT");
            _device.ThrowOnError("creating an RT filter");
            _filters.Add(filter);
            return filter;
        }

        private void Image(IntPtr filter, string name, IntPtr buffer) =>
            Oidn.SetFilterImage(filter, name, buffer, Oidn.Format.Float3, (nuint)Key.Width, (nuint)Key.Height, 0, 0, 0);

        /// <summary>A filter that denoises the auxiliary image <paramref name="name"/> alone, run before the beauty's.</summary>
        private void Prefilter(string name, IntPtr input, IntPtr output, Oidn.Quality quality)
        {
            IntPtr filter = Filter();
            Image(filter, name, input);
            Image(filter, "output", output);
            Oidn.SetFilterInt(filter, "quality", (int)quality);
            Oidn.CommitFilter(filter);
            _device.ThrowOnError($"preparing the {name} prefilter");
        }
    }
}
