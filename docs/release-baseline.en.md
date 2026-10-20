# Building a release baseline

From a clean Windows checkout with the .NET 8 SDK installed, run:

```powershell
.\scripts\Build-Release.ps1
```

The same script runs in CI. It restores, builds Release with warnings treated as
errors, runs all solution tests, and publishes the API and WPF client. Any failed
command stops packaging. Each run creates a new directory under `artifacts/releases`
with test results, `api.zip`, `client.zip`, and a final `manifest.json` containing
the source commit, dirty-source status, SDK version and ZIP SHA-256 checksums.
Only runs containing a manifest have completed successfully. A release baseline
must have `sourceDirty: false`; local runs with changes are review packages.

Packages are framework-dependent Windows builds and require the .NET 8 ASP.NET
Core and Desktop runtimes on the target computer. Package reproduction means
repeating the build and verification from the recorded revision; ZIP timestamps
and SDK servicing updates can change the archive checksum between runs.

The script creates packages only. Configure the installation identity, explicit
database path, operator roles and network settings using the existing deployment
guides before installing. Existing services and databases are not modified by a
release build. Generated videos and PDFs under `output/` are local documentation
outputs and are not required to build the application from a fresh checkout.

GitHub Actions retains the verified ZIPs and manifest as `transportorders-release`
and test evidence as `test-results`. Remote CI verification requires pushing the
source revision and observing a successful workflow run.
