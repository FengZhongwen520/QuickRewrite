using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace QuickRewrite.Services;

internal static class DpapiSecretStore
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const string DpapiPrefix = "DPAPI:";
    private const string LocalPrefix = "LOCAL1:";

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "";
        }

        try
        {
            return DpapiPrefix + ProtectWithDpapi(plainText);
        }
        catch
        {
            // Some restricted or portable environments do not expose a usable DPAPI profile.
            return LocalPrefix + ProtectWithLocalKey(plainText);
        }
    }

    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return "";
        }

        try
        {
            if (cipherText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                return UnprotectWithDpapi(cipherText[DpapiPrefix.Length..]);
            }

            if (cipherText.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                return UnprotectWithLocalKey(cipherText[LocalPrefix.Length..]);
            }

            // Backward compatibility with the first local prototype.
            return UnprotectWithDpapi(cipherText);
        }
        catch
        {
            return "";
        }
    }

    private static string ProtectWithDpapi(string plainText)
    {
        var clearBytes = Encoding.UTF8.GetBytes(plainText);
        var input = CreateBlob(clearBytes);
        try
        {
            if (!CryptProtectData(ref input, "QuickRewrite API Key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                return Convert.ToBase64String(ReadBlob(output));
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Array.Clear(clearBytes);
            FreeBlob(ref input);
        }
    }

    private static string UnprotectWithDpapi(string cipherText)
    {
        var encrypted = Convert.FromBase64String(cipherText);
        var input = CreateBlob(encrypted);
        try
        {
            if (!CryptUnprotectData(ref input, out var description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                return Encoding.UTF8.GetString(ReadBlob(output));
            }
            finally
            {
                if (description != IntPtr.Zero) LocalFree(description);
                LocalFree(output.Data);
            }
        }
        finally
        {
            FreeBlob(ref input);
        }
    }

    private static string ProtectWithLocalKey(string plainText)
    {
        var key = DeriveLocalKey();
        var nonce = new byte[12];
        var tag = new byte[16];
        var clearBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[clearBytes.Length];
        RandomNumberGenerator.Fill(nonce);

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, clearBytes, cipherBytes, tag);

            var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
            return Convert.ToBase64String(payload);
        }
        finally
        {
            Array.Clear(clearBytes);
            Array.Clear(key);
        }
    }

    private static string UnprotectWithLocalKey(string cipherText)
    {
        var payload = Convert.FromBase64String(cipherText);
        if (payload.Length < 28)
        {
            return "";
        }

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipherBytes = payload.AsSpan(28);
        var clearBytes = new byte[cipherBytes.Length];
        var key = DeriveLocalKey();

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipherBytes, tag, clearBytes);
            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            Array.Clear(clearBytes);
            Array.Clear(key);
        }
    }

    private static byte[] DeriveLocalKey()
    {
        var identity = $"QuickRewrite|{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = data };
    }

    private static byte[] ReadBlob(DataBlob blob)
    {
        var result = new byte[blob.Size];
        if (blob.Size > 0)
        {
            Marshal.Copy(blob.Data, result, 0, blob.Size);
        }

        return result;
    }

    private static void FreeBlob(ref DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
            blob.Data = IntPtr.Zero;
        }
    }
}