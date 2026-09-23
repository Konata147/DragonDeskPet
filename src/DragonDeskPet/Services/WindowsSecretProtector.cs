using System.Runtime.InteropServices;
using System.Text;

namespace DragonDeskPet.Services;

public static class WindowsSecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var input = ToBlob(bytes);
        try
        {
            if (!CryptProtectData(ref input, "DragonDeskPet API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var encrypted = new byte[output.Size];
                Marshal.Copy(output.Data, encrypted, 0, output.Size);
                return Convert.ToBase64String(encrypted);
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(value);
            var input = ToBlob(encrypted);
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out var output))
                {
                    return string.Empty;
                }

                try
                {
                    var plain = new byte[output.Size];
                    Marshal.Copy(output.Data, plain, 0, output.Size);
                    return Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
            }
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
