using System.Diagnostics;
using System.Runtime.InteropServices;
using MonitorComfort.App.Native;
using MonitorComfort.Core.Ambient;

namespace MonitorComfort.App.Ambient;

/// <summary>A failed Media Foundation call while using the camera; HResult carries the Windows error.</summary>
internal sealed class CameraException : Exception
{
    public CameraException()
    {
    }

    public CameraException(string message)
        : base(message)
    {
    }

    public CameraException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CameraException(string message, int hresult)
        : base(message) => HResult = hresult;
}

/// <summary>The locked exposure and gain a reading was taken at, the white balance automatic exposure settled on, and the picture brightness.</summary>
internal sealed record CameraSample(int ExposureLog2, int Gain, int? WhiteBalanceKelvin, double MeanLinearY);

/// <summary>Picture brightness across exposure and gain settings of one unchanging scene.</summary>
internal sealed record CameraSweeps(IReadOnlyList<(int ExposureLog2, double MeanY)> Exposure, IReadOnlyList<(int Gain, double MeanY)> Gain);

/// <summary>Per-frame picture brightness after one locked setting.</summary>
internal sealed record CameraProbeStep(int ExposureLog2, int Gain, IReadOnlyList<double> FramesY);

/// <summary>What automatic exposure reported, the frames under it, and the frames after each locked step.</summary>
internal sealed record CameraProbe(int SettledExposureLog2, int SettledGain, IReadOnlyList<double> SettledFramesY, IReadOnlyList<CameraProbeStep> Steps);

/// <summary>
/// One opening of the webcam. Other apps share its exposure, gain and white-balance settings, so
/// disposing always puts back exactly what was there, even after a failure. Not thread-safe.
/// </summary>
internal sealed class CameraSession : IDisposable
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SettledFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumSettle = TimeSpan.FromSeconds(2);
    private const int FramesAfterChange = 4;
    private const int FramesAveraged = 8;
    private const int MaximumEmptyReads = 200;
    private const int PixelStrideBytes = 16;

    private static readonly double[] LinearFromSrgb = BuildLinearTable();

    private readonly IMFActivate _activate;
    private readonly object _source;
    private readonly IAMCameraControl _camera;
    private readonly IAMVideoProcAmp _picture;
    private readonly IMFSourceReader _reader;
    private readonly (int Value, int Flags) _exposure;
    private readonly (int Value, int Flags) _gain;
    private readonly (int Value, int Flags)? _whiteBalance;
    private bool _disposed;

    private CameraSession(IMFActivate activate, object source, IMFSourceReader reader, string name)
    {
        _activate = activate;
        _source = source;
        _reader = reader;
        Name = name;
        _camera = (IAMCameraControl)source;
        _picture = (IAMVideoProcAmp)source;
        _exposure = Get(_camera.Get, MediaFoundation.Exposure) ?? throw new NotSupportedException("The camera has no exposure control.");
        _gain = Get(_picture.Get, MediaFoundation.Gain) ?? throw new NotSupportedException("The camera has no gain control.");
        _whiteBalance = Get(_picture.Get, MediaFoundation.WhiteBalance);
    }

    public string Name { get; }

    /// <summary>Opens the first video capture device, or returns null when there is none.</summary>
    public static unsafe CameraSession? OpenFirst()
    {
        var query = CreateAttributes();
        try
        {
            var sourceType = MediaFoundation.SourceType;
            var videoCapture = MediaFoundation.SourceTypeVideoCapture;
            query.SetGUID(ref sourceType, ref videoCapture);

            nint array;
            uint count;
            var queryPointer = Marshal.GetIUnknownForObject(query);
            try
            {
                Check(NativeMethods.MFEnumDeviceSources(queryPointer, &array, &count), "enumerate cameras");
            }
            finally
            {
                Marshal.Release(queryPointer);
            }

            IMFActivate? chosen = null;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var pointer = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                    if (chosen is null)
                    {
                        chosen = (IMFActivate)Marshal.GetObjectForIUnknown(pointer);
                    }

                    Marshal.Release(pointer);
                }
            }
            finally
            {
                NativeMethods.CoTaskMemFree(array);
            }

            return chosen is null ? null : Open(chosen);
        }
        finally
        {
            Marshal.ReleaseComObject(query);
        }
    }

    /// <summary>
    /// Lets automatic exposure and white balance settle, then measures the picture at a locked setting
    /// found from there; see <see cref="LockedMetering"/>.
    /// </summary>
    public CameraSample Measure(CancellationToken cancellationToken)
    {
        var (exposure, gain, whiteBalance) = Settle(cancellationToken);
        var (setting, meanY) = LockedMetering.Measure(
            Limits(),
            new CameraSetting(exposure, gain),
            s =>
            {
                Lock(s.ExposureLog2, s.Gain);
                return MeanLinearY(FramesAveraged, cancellationToken);
            });
        return new CameraSample(setting.ExposureLog2, setting.Gain, whiteBalance, meanY);
    }

    /// <summary>
    /// Steps exposure at a fixed gain, then gain at a fixed exposure, around where the camera settles
    /// in the current light. The scene must not change meanwhile; it takes about ten seconds.
    /// </summary>
    public CameraSweeps Sweep(CancellationToken cancellationToken)
    {
        var (settledExposure, _, _) = Settle(cancellationToken);
        var exposureRange = Range(_camera.GetRange, MediaFoundation.Exposure);
        var gainRange = Range(_picture.GetRange, MediaFoundation.Gain);

        var midGain = Limits().MeteringGain;
        var exposures = new List<(int, double)>();
        for (var exposure = Math.Max(exposureRange.Min, settledExposure - 5); exposure <= Math.Min(exposureRange.Max, settledExposure + 2); exposure++)
        {
            Lock(exposure, midGain);
            exposures.Add((exposure, MeanLinearY(FramesAveraged / 2, cancellationToken)));
        }

        // One stop darker than the settled exposure, so the highest gains stay below clipping.
        var gainExposure = Math.Max(exposureRange.Min, settledExposure - 1);
        var gains = new List<(int, double)>();
        var step = Math.Max(1, (gainRange.Max - gainRange.Min) / 8);
        for (var gain = gainRange.Min; gain <= gainRange.Max; gain = gain == gainRange.Max ? gain + 1 : Math.Min(gain + step, gainRange.Max))
        {
            Lock(gainExposure, gain);
            gains.Add((gain, MeanLinearY(FramesAveraged / 2, cancellationToken)));
        }

        return new CameraSweeps(exposures, gains);
    }

    /// <summary>
    /// Frame-by-frame picture brightness after locking what automatic exposure reported, then after each
    /// gain step up and back down at a fixed exposure, to show how long the camera takes to follow a change.
    /// </summary>
    public CameraProbe Probe(int framesPerStep, CancellationToken cancellationToken)
    {
        var (exposure, gain, _) = Settle(cancellationToken);
        var settledY = ReadFrames(framesPerStep / 2, cancellationToken);
        Lock(exposure, gain);
        var steps = new List<CameraProbeStep> { new(exposure, gain, ReadFrames(framesPerStep, cancellationToken)) };

        var gainRange = Range(_picture.GetRange, MediaFoundation.Gain);
        var gainExposure = Math.Max(Range(_camera.GetRange, MediaFoundation.Exposure).Min, exposure - 1);
        var step = Math.Max(1, (gainRange.Max - gainRange.Min) / 4);
        var gains = new List<int>();
        for (var g = gainRange.Min; g < gainRange.Max; g += step)
        {
            gains.Add(g);
        }

        gains.Add(gainRange.Max);
        gains.AddRange(Enumerable.Reverse(gains).Skip(1).ToList());
        foreach (var g in gains)
        {
            Lock(gainExposure, g);
            steps.Add(new CameraProbeStep(gainExposure, g, ReadFrames(framesPerStep, cancellationToken)));
        }

        return new CameraProbe(exposure, gain, settledY, steps);
    }

    private double[] ReadFrames(int count, CancellationToken cancellationToken)
    {
        var frames = new double[count];
        for (var i = 0; i < count; i++)
        {
            frames[i] = ReadFrame(cancellationToken);
        }

        return frames;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _camera.Set(MediaFoundation.Exposure, _exposure.Value, _exposure.Flags);
            _picture.Set(MediaFoundation.Gain, _gain.Value, _gain.Flags);
            if (_whiteBalance is { } whiteBalance)
            {
                _picture.Set(MediaFoundation.WhiteBalance, whiteBalance.Value, whiteBalance.Flags);
            }
        }
        finally
        {
            Release(_reader, _source, _activate);
        }
    }

    // Shuts the device down and frees the wrappers; a failing shutdown must not leak the rest or hide the original error.
    private static void Release(object? reader, object? source, IMFActivate activate)
    {
        try
        {
            activate.ShutdownObject();
        }
        catch (COMException)
        {
            // The device is gone or already shut down; the wrappers below still need releasing.
        }

        foreach (var wrapper in new[] { reader, source, activate })
        {
            if (wrapper is not null)
            {
                Marshal.ReleaseComObject(wrapper);
            }
        }
    }

    private static unsafe CameraSession Open(IMFActivate activate)
    {
        var friendlyName = MediaFoundation.FriendlyName;
        var name = activate.GetAllocatedString(ref friendlyName, out var value, out _) >= 0 ? value : "camera";
        var sourceId = MediaFoundation.MediaSource;
        object source;
        try
        {
            activate.ActivateObject(ref sourceId, out source);
        }
        catch
        {
            Release(null, null, activate);
            throw;
        }

        var readerAttributes = CreateAttributes();
        var output = CreateMediaType();
        var sourcePointer = Marshal.GetIUnknownForObject(source);
        var attributesPointer = Marshal.GetIUnknownForObject(readerAttributes);
        IMFSourceReader? reader = null;
        try
        {
            var processing = MediaFoundation.EnableVideoProcessing;
            readerAttributes.SetUINT32(ref processing, 1);

            nint readerPointer;
            Check(NativeMethods.MFCreateSourceReaderFromMediaSource(sourcePointer, attributesPointer, &readerPointer), "open camera stream");
            reader = (IMFSourceReader)Marshal.GetObjectForIUnknown(readerPointer);
            Marshal.Release(readerPointer);

            // Small RGB frames: plenty for an average, and cheap to scan.
            var major = MediaFoundation.MajorType;
            var video = MediaFoundation.Video;
            var subType = MediaFoundation.SubType;
            var rgb32 = MediaFoundation.Rgb32;
            var frameSize = MediaFoundation.FrameSize;
            output.SetGUID(ref major, ref video);
            output.SetGUID(ref subType, ref rgb32);
            output.SetUINT64(ref frameSize, (640UL << 32) | 480UL);
            reader.SetCurrentMediaType(MediaFoundation.FirstVideoStream, 0, output);

            return new CameraSession(activate, source, reader, name);
        }
        catch
        {
            Release(reader, source, activate);
            throw;
        }
        finally
        {
            Marshal.Release(sourcePointer);
            Marshal.Release(attributesPointer);
            Marshal.ReleaseComObject(readerAttributes);
            Marshal.ReleaseComObject(output);
        }
    }

    private (int Exposure, int Gain, int? WhiteBalance) Settle(CancellationToken cancellationToken)
    {
        _camera.Set(MediaFoundation.Exposure, _exposure.Value, MediaFoundation.AutoFlag);
        if (_whiteBalance is { } whiteBalance)
        {
            _picture.Set(MediaFoundation.WhiteBalance, whiteBalance.Value, MediaFoundation.AutoFlag);
        }

        var clock = Stopwatch.StartNew();
        (int, int, int?)? last = null;
        var stableSince = TimeSpan.Zero;
        while (clock.Elapsed < SettleTimeout)
        {
            ReadFrame(cancellationToken);
            var current = (Value(_camera.Get, MediaFoundation.Exposure), Value(_picture.Get, MediaFoundation.Gain), WhiteBalanceNow());
            if (current != last)
            {
                last = current;
                stableSince = clock.Elapsed;
            }
            else if (clock.Elapsed - stableSince >= SettledFor && clock.Elapsed >= MinimumSettle)
            {
                break;
            }
        }

        return last ?? throw new TimeoutException("The camera delivered no frames.");
    }

    private int? WhiteBalanceNow()
        => _whiteBalance is not null && _picture.Get(MediaFoundation.WhiteBalance, out var value, out _) >= 0 ? value : null;

    // A rejected write would leave automatic exposure running under a reading that assumes it locked.
    private void Lock(int exposure, int gain)
    {
        Check(_camera.Set(MediaFoundation.Exposure, exposure, MediaFoundation.ManualFlag), "lock the exposure");
        Check(_picture.Set(MediaFoundation.Gain, gain, MediaFoundation.ManualFlag), "lock the gain");
    }

    private double MeanLinearY(int frames, CancellationToken cancellationToken)
    {
        for (var i = 0; i < FramesAfterChange; i++)
        {
            ReadFrame(cancellationToken);
        }

        var total = 0.0;
        for (var i = 0; i < frames; i++)
        {
            total += ReadFrame(cancellationToken);
        }

        return total / frames;
    }

    // Returns the frame's mean linear luminance, sampling every fourth pixel.
    private unsafe double ReadFrame(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumEmptyReads; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reader.ReadSample(MediaFoundation.FirstVideoStream, 0, out _, out _, out _, out var sample);
            if (sample is null)
            {
                continue;
            }

            try
            {
                sample.ConvertToContiguousBuffer(out var buffer);
                try
                {
                    buffer.Lock(out var data, out _, out var length);
                    try
                    {
                        var pixels = new ReadOnlySpan<byte>((void*)data, (int)length);
                        double sum = 0;
                        var count = 0;
                        for (var i = 0; i + 2 < pixels.Length; i += PixelStrideBytes)
                        {
                            // BGRX byte order; Rec. 709 luminance weights.
                            sum += (0.0722 * LinearFromSrgb[pixels[i]]) + (0.7152 * LinearFromSrgb[pixels[i + 1]]) + (0.2126 * LinearFromSrgb[pixels[i + 2]]);
                            count++;
                        }

                        return count == 0 ? 0 : sum / count;
                    }
                    finally
                    {
                        buffer.Unlock();
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(buffer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }

        throw new TimeoutException("The camera delivered no frames.");
    }

    private delegate int ControlGet(int property, out int value, out int flags);

    private delegate int ControlRange(int property, out int min, out int max, out int step, out int defaultValue, out int flags);

    private static (int Value, int Flags)? Get(ControlGet get, int property)
        => get(property, out var value, out var flags) >= 0 ? (value, flags) : null;

    private static int Value(ControlGet get, int property)
    {
        Check(get(property, out var value, out _), "read a camera setting");
        return value;
    }

    private CameraLimits Limits()
    {
        var exposure = Range(_camera.GetRange, MediaFoundation.Exposure);
        var gain = Range(_picture.GetRange, MediaFoundation.Gain);
        return new CameraLimits(exposure.Min, exposure.Max, gain.Min, gain.Max);
    }

    private static (int Min, int Max) Range(ControlRange range, int property)
    {
        Check(range(property, out var min, out var max, out _, out _, out _), "read control range");
        return (min, max);
    }

    private static unsafe IMFAttributes CreateAttributes()
    {
        nint pointer;
        Check(NativeMethods.MFCreateAttributes(&pointer, 2), "create attributes");
        var attributes = (IMFAttributes)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        return attributes;
    }

    private static unsafe IMFMediaType CreateMediaType()
    {
        nint pointer;
        Check(NativeMethods.MFCreateMediaType(&pointer), "create media type");
        var mediaType = (IMFMediaType)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        return mediaType;
    }

    private static void Check(int hresult, string action)
    {
        if (hresult < 0)
        {
            throw new CameraException($"Could not {action}.", hresult);
        }
    }

    private static double[] BuildLinearTable()
    {
        var table = new double[256];
        for (var i = 0; i < table.Length; i++)
        {
            var c = i / 255.0;
            table[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return table;
    }
}
