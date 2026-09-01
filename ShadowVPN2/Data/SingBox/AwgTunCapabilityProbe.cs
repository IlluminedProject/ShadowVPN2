using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ShadowVPN2.Data.SingBox;

public sealed class AwgTunCapabilityProbe {
    private const short IffTun = 0x0001;
    private const short IffNoPi = 0x1000;
    private const short IffVnetHdr = 0x4000;
    private const uint TunFCsum = 0x0001;
    private const uint TunFTso4 = 0x0002;
    private const uint TunFTso6 = 0x0004;
    private const ulong TunsetIff = 0x400454CA;
    private const ulong TunsetOffload = 0x400454D0;
    private readonly Lazy<bool> _result = new(Probe);

    public bool IsSupported() {
        return _result.Value;
    }

    private static bool Probe() {
        if (!OperatingSystem.IsLinux())
            return false;

        try {
            using var tun = File.OpenHandle("/dev/net/tun", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var request = new byte[40];
            var interfaceName = Encoding.ASCII.GetBytes("sv2-probe");
            Array.Copy(interfaceName, request, interfaceName.Length);
            BitConverter.GetBytes((short)(IffTun | IffNoPi | IffVnetHdr)).CopyTo(request, 16);

            if (Ioctl(tun, TunsetIff, request) < 0)
                return false;

            return Ioctl(tun, TunsetOffload, TunFCsum | TunFTso4 | TunFTso6) == 0;
        }
        catch (IOException) {
            return false;
        }
        catch (UnauthorizedAccessException) {
            return false;
        }
        catch (Win32Exception) {
            return false;
        }
    }

    private static int Ioctl(SafeFileHandle handle, ulong request, byte[] data) {
        var handleValue = handle.DangerousGetHandle();
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try {
            return ioctl(handleValue, request, pinned.AddrOfPinnedObject());
        }
        finally {
            pinned.Free();
        }
    }

    private static int Ioctl(SafeFileHandle handle, ulong request, uint value) {
        return ioctl(handle.DangerousGetHandle(), request, value);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fileDescriptor, ulong request, IntPtr argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fileDescriptor, ulong request, uint argument);
}