# Command-line flags

np2ptp-gui doesn't need any flags for normal use. This exists for the few cases where you do.

## `--no-check-cert`

Skips the Authenticode signature check the app runs against every downloaded `np2ptp.exe` before keeping it.

By default, np2ptp-gui refuses to keep a downloaded build unless it's signed with the same certificate np2ptp's own release pipeline uses. If the signature is missing or from a different certificate, the download gets deleted and the app shows an error instead of silently running an unverified binary.

Right now that check will reject every release, because np2ptp's build pipeline doesn't sign its output yet — that's landing separately. Until it does, pass this flag if you want np2ptp-gui to actually download and run np2ptp:

```
Np2ptpGui.exe --no-check-cert
```

The app shows a warning dialog on startup whenever this flag is active, as a reminder it's on. Once np2ptp's releases are signed, drop the flag — you shouldn't need it again outside of local testing.
