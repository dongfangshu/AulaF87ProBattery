using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AulaBattery.Windows;

internal static class NativeHid
{
    private const uint PresentDeviceInterfaces = 0x12;
    internal const uint Overlapped = 0x40000000;
    internal const uint ReadWrite = 0xC0000000;

    internal static IReadOnlyList<HidDevice> Enumerate()
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, PresentDeviceInterfaces);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new List<HidDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) // ERROR_NO_MORE_ITEMS
                        break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(set, ref info, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var detail = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null)
                        continue;
                    using var handle = Open(path, 0, false);
                    if (handle.IsInvalid)
                        continue; // Protected system collections are not battery endpoints.
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || !HidD_GetPreparsedData(handle, out var data))
                        continue;
                    try
                    {
                        if (HidP_GetCaps(data, out var caps) != 0x00110000)
                            continue;
                        devices.Add(new HidDevice(path, attributes.VendorId, attributes.ProductId,
                            caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength));
                    }
                    finally
                    {
                        HidD_FreePreparsedData(data);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return devices;
    }

    internal static SafeFileHandle Open(string path, uint access, bool overlapped) =>
        CreateFile(path, access, 3, IntPtr.Zero, 3, overlapped ? Overlapped : 0, IntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        internal int Size;
        internal Guid Guid;
        internal uint Flags;
        internal UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        internal int Size;
        internal ushort VendorId, ProductId, Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        internal ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        internal ushort[] Reserved;
        internal ushort LinkNodes, InputButtons, InputValues, InputIndices;
        internal ushort OutputButtons, OutputValues, OutputIndices;
        internal ushort FeatureButtons, FeatureValues, FeatureIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_FlushQueue(SafeFileHandle handle);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] report, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid,
        uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data,
        IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}

internal sealed record HidDevice(string Path, ushort VendorId, ushort ProductId,
    ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength)
{
    public string UsbId => $"{VendorId:X4}:{ProductId:X4}";
    public string UsageId => $"{UsagePage:X4}:{Usage:X4}";
    public bool IsReceiver => VendorId == Core.BatteryProtocol.VendorId && ProductId == Core.BatteryProtocol.ProductId;
    public bool IsBatteryEndpoint => IsReceiver && UsagePage is 0xFF02 or 0xFF04 &&
        InputLength >= Core.BatteryProtocol.ReportLength && OutputLength >= Core.BatteryProtocol.ReportLength;
}
