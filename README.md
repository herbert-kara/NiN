
Personal fork of [PattN](https://github.com/patterniha/PattN), based on [v2rayN](https://github.com/2dust/v2rayN). Original copyright and GPL-3.0 license are retained.

## Personal Version Changes

- NiN display name and NI icon; system tray color changes: clearing blue, system proxy red, no change purple, PAC green.
- Flag image next to the config name in both WPF and Avalonia UIs, without relying on Windows emoji rendering.
- Country is first extracted from the exit IP test result. For configs without a test result, the server address's public IP is geolocated automatically and asynchronously over HTTPS via ipwho.is; this country may differ from the actual connection exit (e.g. behind a CDN). Without a result, the name label is only a rough hint; an unknown country is never guessed.
- Privacy: only the public IP is sent to ipwho.is for geolocation — never the name, credentials, or config content. Local addresses are not geolocated. Results are kept under rate limits and cached temporarily; changing group or filter cancels the previous list lookups.

## Download and Test

[Releases](https://github.com/herbert-kara/NiN/releases) · [Build status](https://github.com/herbert-kara/NiN/actions/workflows/nin-release.yml)

## Custom Workflow

A dedicated workflow builds the tests and both Windows x64 UIs. Tags `v*-nin.*` are only published after tests and both builds succeed. Files are not digitally signed; a SHA-256 is placed next to the ZIP. A successful build is not a substitute for testing the connection on your device.

## Automatic Update from PattN

- The [sync task](https://github.com/herbert-kara/NiN/actions/workflows/nin-sync.yml) checks every 6 hours for the latest **stable release** of PattN; runs happen on GitHub's schedule, not immediately after a release. It also has a manual run button. No task is needed on your machine.
- New release source is merged with the NiN changes; NI icons, the NiN name, the update path, and the flag display connection are guarded and tested. A merge conflict or failed test/build blocks the release and opens an Issue; resolving conflicts requires human review.
- Cores and bundled data come from the same PattN release package, not from an independent `latest` version. The main PattN executable is never replaced by NiN.
- Updates inside NiN only pull the full NiN release; newer PattN code is already merged into it. Install `v7.25.1-nin.3` or newer; on older versions, if no update shows, grab the new ZIP manually once.
- Only the personal `herbert-kara/NiN` repository changes. GitHub may disable the schedule of a low-activity public repository after 60 days; in that case re-enable the workflow.

## Upstream

PattN adds Iran-focused defaults and support for cipherSuites / unsafe fingerprints, with a modified Xray core. Original upstream documentation and donation details: [PattN README](https://github.com/patterniha/PattN#readme).
