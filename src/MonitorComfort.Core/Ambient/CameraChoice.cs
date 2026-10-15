using System.Runtime.ExceptionServices;

namespace MonitorComfort.Core.Ambient;

/// <summary>A video capture device: Windows' symbolic link, which stays the same across restarts and USB ports, and its name.</summary>
public sealed record CameraDevice(string Id, string Name);

/// <summary>A frame size a camera offers natively.</summary>
public readonly record struct FrameSize(int Width, int Height)
{
    private static readonly FrameSize Preferred = new(640, 480);

    /// <summary>640×480 when offered, as most webcams do; otherwise the smallest, which is plenty for an average and cheap to scan.</summary>
    public static FrameSize? Pick(IEnumerable<FrameSize> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        FrameSize? smallest = null;
        foreach (var size in offered)
        {
            if (size == Preferred)
            {
                return size;
            }

            if (size.Width > 0 && size.Height > 0 && (smallest is not { } s || (long)size.Width * size.Height < (long)s.Width * s.Height))
            {
                smallest = size;
            }
        }

        return smallest;
    }
}

/// <summary>Which camera the light meter uses.</summary>
public static class CameraChoice
{
    /// <summary>
    /// The cameras to try, in order. With <paramref name="chosenId"/> set it is only that camera, so a reading never
    /// comes from a camera the calibration wasn't made with. A webcam without a serial number gets a new id on another
    /// USB port, so when the id isn't connected, the one connected camera of the same model stands in for it; with
    /// none or several, there is no candidate. Without a choice it is every camera, in Windows' order.
    /// </summary>
    public static IReadOnlyList<CameraDevice> Candidates(IReadOnlyList<CameraDevice> cameras, string? chosenId)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        if (chosenId is null)
        {
            return cameras;
        }

        var exact = cameras.FirstOrDefault(c => string.Equals(c.Id, chosenId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return [exact];
        }

        var model = ModelKey(chosenId);
        var sameModel = model is null
            ? []
            : cameras.Where(c => string.Equals(ModelKey(c.Id), model, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameModel.Count == 1 ? sameModel : [];
    }

    // A symbolic link reads \\?\usb#vid_046d&pid_0892&mi_00#<instance>#{interface}; the first two parts name the model.
    private static string? ModelKey(string id)
    {
        var parts = id.Split('#');
        return parts.Length >= 3 && parts[1].Length > 0 ? $"{parts[0]}#{parts[1]}" : null;
    }

    /// <summary>
    /// Opens the first candidate that can meter, skipping ones without the controls (such as virtual cameras).
    /// Any other failure, such as the camera being in use, is the answer and is thrown. Null when there are no
    /// candidates; when every candidate lacked the controls, the last such exception is thrown.
    /// </summary>
    public static T? OpenFirstUsable<T>(IReadOnlyList<CameraDevice> candidates, Func<CameraDevice, T> open, Action<CameraDevice, NotSupportedException> skipped)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(skipped);

        NotSupportedException? last = null;
        foreach (var camera in candidates)
        {
            try
            {
                return open(camera);
            }
            catch (NotSupportedException ex)
            {
                skipped(camera, ex);
                last = ex;
            }
        }

        if (last is not null)
        {
            ExceptionDispatchInfo.Throw(last);
        }

        return null;
    }
}
