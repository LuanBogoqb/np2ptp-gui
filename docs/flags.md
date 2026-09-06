# Command-line flags

np2ptp-gui doesn't need any flags for normal use. This exists for the few cases where you do.

## `--no-check-cert`

Skips the Authenticode signature check the app runs against every downloaded `np2ptp.exe` before keeping it.

By default, np2ptp-gui refuses to keep a downloaded build unless it's signed with the same certificate np2ptp's own release pipeline uses. If the signature is missing or from a different certificate, the download gets deleted and the app shows an error instead of silently running an unverified binary.

**Corrected 2026-09-06:** this doc used to say the check would reject every release, because np2ptp's pipeline didn't sign its output yet. That stopped being true in July 2026. np2ptp's release workflow signs the Windows binary with `signtool` (Authenticode, SHA-256, timestamped) and publishes the thumbprint in its README; np2ptp-gui pins that same thumbprint in `src/Np2ptpGui/Services/BinaryManager.cs:18` (`36477BB5DCB10D2C0381A2D79533F0386C5CCACA`). So a signed release passes the check, and the flag is no longer needed to use the app.

Keep the flag for the cases it was really for: running an unsigned local build of np2ptp, or testing the download path against a fork whose releases aren't signed with that certificate.

```
Np2ptpGui.exe --no-check-cert
```

The app shows a warning dialog on startup whenever this flag is active, as a reminder it's on. Once np2ptp's releases are signed, drop the flag — you shouldn't need it again outside of local testing.
