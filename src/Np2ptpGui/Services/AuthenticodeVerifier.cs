namespace Np2ptpGui.Services;

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

// Verifies a file's Authenticode signature is cryptographically valid (the
// signed hash matches the file's actual content) via WinVerifyTrust - the
// same Win32 API `signtool verify` itself calls. This is deliberately NOT
// just reading whichever certificate happens to be embedded in the file:
// X509Certificate.CreateFromSignedFile alone extracts certificate metadata
// without validating that it actually, correctly signs the file's content -
// it would happily "succeed" against a tampered file carrying a stale or
// mismatched signature block.
//
// dwProvFlags is set to WTD_HASH_ONLY_FLAG so WinVerifyTrust checks ONLY
// signature/hash integrity and skips CA chain-of-trust validation. np2ptp is
// signed with a self-signed certificate (not issued by a publicly trusted
// CA) - without this flag, WinVerifyTrust fails with an untrusted-root error
// on every machine that hasn't manually imported that certificate, even
// though the file is genuinely untampered and signed by the exact expected
// cert. BinaryManager already pins the signer's exact thumbprint - that
// pinning IS the trust decision here, so we don't need (and can't rely on)
// the OS's own root store to also vouch for the cert.
//
// Struct layout matches Microsoft's own reference implementation
// (github.com/microsoft/workbooks Tools/InstallerVerifier/WinTrust.cs) - an
// earlier version of this file used class-typed fields for automatic pointer
// marshaling and omitted the trailing pSignatureSettings field (added in the
// Windows 7+/8 SDK); both caused a real AccessViolationException inside
// WinVerifyTrust, caught only by actually running this against a file rather
// than trusting the P/Invoke declaration would just work.
public static unsafe class AuthenticodeVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const int WtdUiNone = 2;
    private const int WtdRevokeNone = 0;
    private const int WtdChoiceFile = 1;
    private const int WtdStateActionVerify = 1;
    private const int WtdStateActionClose = 2;
    private const int WtdHashOnlyFlag = 0x00000200;

    // Returns the signer certificate's SHA1 thumbprint if the file's
    // Authenticode signature hash is intact; null if the file is unsigned or
    // has been tampered with (signature no longer matches the content).
    // Does not require the signer's CA to be trusted by this machine - see
    // the WtdHashOnlyFlag note above.
    public static string? GetVerifiedSignerThumbprint(string filePath)
    {
        var filePathPtr = Marshal.StringToHGlobalUni(filePath);
        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = filePathPtr,
            };

            var data = new WINTRUST_DATA
            {
                cbStruct = sizeof(WINTRUST_DATA),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = &fileInfo,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdHashOnlyFlag,
            };

            var actionId = WinTrustActionGenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, &actionId, &data);

            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(IntPtr.Zero, &actionId, &data);

            if (result != 0) return null;

            var cert = X509Certificate.CreateFromSignedFile(filePath);
            return cert.GetCertHashString();
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(filePathPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public int cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public int dwUIChoice;
        public int fdwRevocationChecks;
        public int dwUnionChoice;
        public WINTRUST_FILE_INFO* pFile;
        public int dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public int dwProvFlags;
        public int dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll")]
    private static extern int WinVerifyTrust(IntPtr hwnd, Guid* pgActionID, WINTRUST_DATA* pWVTData);
}
