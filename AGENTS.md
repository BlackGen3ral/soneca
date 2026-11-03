# Artho Control maintenance

The user authorized publishing improvements and delivering automatic updates through GitHub releases.

- Keep the interface in Brazilian Portuguese.
- Never execute or schedule a real shutdown during tests. Use `--demo` and `--self-test`.
- Preserve the per-user scheduled task and its cancellation across releases.
- Do not add `/f` or a nonzero `/t` to shutdown: nonzero timeouts imply forced app termination.
- On requested improvements, increment the semantic version, run `scripts/build.ps1`, commit and push, then create and push the matching version tag.
- Wait for the release workflow to complete successfully. Download the published installer and verify against the published SHA256SUMS.txt before reporting delivery.
- Automatic updates must verify official repository URLs and checksums, and defer while a schedule is active.
- Preserve the existing Soneca install AppId, executable, task identifier, repository URL, and legacy installer asset so previous versions can update to Artho Control.
- Never test cleanup against the user's actual Temp, Prefetch or Recycle Bin. Use isolated fixtures or `--demo`; real-world analysis may be read-only.
- Cleanup must skip links, junctions, files in use, changed files, and paths outside each approved root. Prefetch cleanup is limited to top-level .pf files. The Recycle Bin is opt-in.
- The elevated helper must only clean the fixed Windows Prefetch location; never accept arbitrary paths or shell commands.
