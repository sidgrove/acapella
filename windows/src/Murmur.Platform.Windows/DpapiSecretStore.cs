using System.Runtime.InteropServices;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>
/// DPAPI for the current Windows user: what it encrypts only this user, on this PC, can read.
/// Holds the sync device token. Logic-free; the file handling is in <c>SyncAccount</c>.
/// </summary>
/// <remarks>
/// <c>CryptProtectData</c> directly rather than the <c>ProtectedData</c> package, which would
/// be one more pinned dependency for two calls.
/// </remarks>
public sealed class DpapiSecretStore : ISecretStore
{
    private const int UiForbidden = 0x1;

    /// <inheritdoc />
    public byte[] Protect(byte[] plain) =>
        Transform(plain, protect: true) ?? throw new System.Security.Cryptography.CryptographicException("DPAPI could not encrypt the secret.");

    /// <inheritdoc />
    public byte[]? Unprotect(byte[] cipher) => Transform(cipher, protect: false);

    private static byte[]? Transform(byte[] input, bool protect)
    {
        var inputBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(Math.Max(input.Length, 1)) };
        var outputBlob = default(DataBlob);
        try
        {
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            var ok = protect
                ? CryptProtectData(ref inputBlob, null, 0, 0, 0, UiForbidden, ref outputBlob)
                : CryptUnprotectData(ref inputBlob, 0, 0, 0, 0, UiForbidden, ref outputBlob);
            if (!ok)
            {
                PlatformDiagnostics.Warn($"DPAPI {(protect ? "protect" : "unprotect")} failed: error {Marshal.GetLastPInvokeError()}");
                return null;
            }
            var output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
            return output;
        }
        finally
        {
            Marshal.FreeHGlobal(inputBlob.Data);
            if (outputBlob.Data != 0) LocalFree(outputBlob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, nint entropy, nint reserved, nint prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
