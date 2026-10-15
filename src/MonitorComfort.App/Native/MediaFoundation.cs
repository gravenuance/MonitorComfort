using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

#pragma warning disable CA1040, CA1711, IDE1006 // COM vtable layouts: names and shapes are fixed by the Windows SDK.

// Media Foundation and DirectShow control interfaces, declared in vtable order. Only the members
// the light meter calls are used; the rest keep the slots aligned.
namespace MonitorComfort.App.Native;

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    void GetItem(ref Guid key, IntPtr value);
    void GetItemType(ref Guid key, out int type);
    void CompareItem(ref Guid key, IntPtr value, out bool result);
    void Compare(IMFAttributes theirs, int matchType, out bool result);
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    void GetDouble(ref Guid key, out double value);
    [PreserveSig] int GetGUID(ref Guid key, out Guid value);
    void GetStringLength(ref Guid key, out uint length);
    void GetString(ref Guid key, IntPtr buffer, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint length);
    void GetBlobSize(ref Guid key, out uint size);
    void GetBlob(ref Guid key, IntPtr buffer, uint size, out uint written);
    void GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
    void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
    void SetItem(ref Guid key, IntPtr value);
    void DeleteItem(ref Guid key);
    void DeleteAllItems();
    void SetUINT32(ref Guid key, uint value);
    void SetUINT64(ref Guid key, ulong value);
    void SetDouble(ref Guid key, double value);
    void SetGUID(ref Guid key, ref Guid value);
    void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void SetBlob(ref Guid key, IntPtr buffer, uint size);
    void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint count);
    void GetItemByIndex(uint index, out Guid key, IntPtr value);
    void CopyAllItems(IMFAttributes dest);
}

[ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate
{
    void GetItem(ref Guid key, IntPtr value);
    void GetItemType(ref Guid key, out int type);
    void CompareItem(ref Guid key, IntPtr value, out bool result);
    void Compare(IMFAttributes theirs, int matchType, out bool result);
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    void GetDouble(ref Guid key, out double value);
    [PreserveSig] int GetGUID(ref Guid key, out Guid value);
    void GetStringLength(ref Guid key, out uint length);
    void GetString(ref Guid key, IntPtr buffer, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint length);
    void GetBlobSize(ref Guid key, out uint size);
    void GetBlob(ref Guid key, IntPtr buffer, uint size, out uint written);
    void GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
    void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
    void SetItem(ref Guid key, IntPtr value);
    void DeleteItem(ref Guid key);
    void DeleteAllItems();
    void SetUINT32(ref Guid key, uint value);
    void SetUINT64(ref Guid key, ulong value);
    void SetDouble(ref Guid key, double value);
    void SetGUID(ref Guid key, ref Guid value);
    void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void SetBlob(ref Guid key, IntPtr buffer, uint size);
    void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint count);
    void GetItemByIndex(uint index, out Guid key, IntPtr value);
    void CopyAllItems(IMFAttributes dest);
    void ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void ShutdownObject();
    void DetachObject();
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    void GetItem(ref Guid key, IntPtr value);
    void GetItemType(ref Guid key, out int type);
    void CompareItem(ref Guid key, IntPtr value, out bool result);
    void Compare(IMFAttributes theirs, int matchType, out bool result);
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    void GetDouble(ref Guid key, out double value);
    [PreserveSig] int GetGUID(ref Guid key, out Guid value);
    void GetStringLength(ref Guid key, out uint length);
    void GetString(ref Guid key, IntPtr buffer, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint length);
    void GetBlobSize(ref Guid key, out uint size);
    void GetBlob(ref Guid key, IntPtr buffer, uint size, out uint written);
    void GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
    void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
    void SetItem(ref Guid key, IntPtr value);
    void DeleteItem(ref Guid key);
    void DeleteAllItems();
    void SetUINT32(ref Guid key, uint value);
    void SetUINT64(ref Guid key, ulong value);
    void SetDouble(ref Guid key, double value);
    void SetGUID(ref Guid key, ref Guid value);
    void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void SetBlob(ref Guid key, IntPtr buffer, uint size);
    void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint count);
    void GetItemByIndex(uint index, out Guid key, IntPtr value);
    void CopyAllItems(IMFAttributes dest);
    void GetMajorType(out Guid type);
    void IsCompressedFormat(out bool compressed);
    void IsEqual(IMFMediaType type, out int flags);
    void GetRepresentation(Guid rep, out IntPtr ptr);
    void FreeRepresentation(Guid rep, IntPtr ptr);
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    void GetStreamSelection(uint stream, out bool selected);
    void SetStreamSelection(uint stream, bool selected);
    [PreserveSig] int GetNativeMediaType(uint stream, uint index, out IMFMediaType type);
    void GetCurrentMediaType(uint stream, out IMFMediaType type);
    void SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
    void SetCurrentPosition(ref Guid format, IntPtr position);
    void ReadSample(uint stream, uint control, out uint actualStream, out uint flags, out long timestamp, out IMFSample? sample);
    void Flush(uint stream);
    void GetServiceForStream(uint stream, ref Guid service, ref Guid riid, out IntPtr ppv);
    void GetPresentationAttribute(uint stream, ref Guid attribute, IntPtr value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    void GetItem(ref Guid key, IntPtr value);
    void GetItemType(ref Guid key, out int type);
    void CompareItem(ref Guid key, IntPtr value, out bool result);
    void Compare(IMFAttributes theirs, int matchType, out bool result);
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    void GetDouble(ref Guid key, out double value);
    [PreserveSig] int GetGUID(ref Guid key, out Guid value);
    void GetStringLength(ref Guid key, out uint length);
    void GetString(ref Guid key, IntPtr buffer, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint length);
    void GetBlobSize(ref Guid key, out uint size);
    void GetBlob(ref Guid key, IntPtr buffer, uint size, out uint written);
    void GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);
    void GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
    void SetItem(ref Guid key, IntPtr value);
    void DeleteItem(ref Guid key);
    void DeleteAllItems();
    void SetUINT32(ref Guid key, uint value);
    void SetUINT64(ref Guid key, ulong value);
    void SetDouble(ref Guid key, double value);
    void SetGUID(ref Guid key, ref Guid value);
    void SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void SetBlob(ref Guid key, IntPtr buffer, uint size);
    void SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint count);
    void GetItemByIndex(uint index, out Guid key, IntPtr value);
    void CopyAllItems(IMFAttributes dest);
    void GetSampleFlags(out uint flags);
    void SetSampleFlags(uint flags);
    void GetSampleTime(out long time);
    void SetSampleTime(long time);
    void GetSampleDuration(out long duration);
    void SetSampleDuration(long duration);
    void GetBufferCount(out uint count);
    void GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
    void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    void AddBuffer(IMFMediaBuffer buffer);
    void RemoveBufferByIndex(uint index);
    void RemoveAllBuffers();
    void GetTotalLength(out uint length);
    void CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    void Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
    void Unlock();
    void GetCurrentLength(out uint length);
    void SetCurrentLength(uint length);
    void GetMaxLength(out uint length);
}

[ComImport, Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAMCameraControl
{
    [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int @default, out int flags);
    [PreserveSig] int Set(int property, int value, int flags);
    [PreserveSig] int Get(int property, out int value, out int flags);
}

[ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAMVideoProcAmp
{
    [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int @default, out int flags);
    [PreserveSig] int Set(int property, int value, int flags);
    [PreserveSig] int Get(int property, out int value, out int flags);
}

/// <summary>Media Foundation attribute keys, type IDs and control indexes used by the light meter.</summary>
internal static class MediaFoundation
{
    public const uint Version = 0x00020070;
    public const uint FirstVideoStream = 0xFFFFFFFC;

    // IAMCameraControl / IAMVideoProcAmp property indexes and flags.
    public const int Exposure = 4;
    public const int WhiteBalance = 7;
    public const int Gain = 9;
    public const int AutoFlag = 1;
    public const int ManualFlag = 2;

    public static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    public static readonly Guid SourceTypeVideoCapture = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    public static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    public static readonly Guid SymbolicLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    public static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid SubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid MediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
}
