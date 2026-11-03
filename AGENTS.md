# Soneca maintenance

The user authorized publishing improvements and delivering automatic updates through GitHub releases.

- Keep the interface in Brazilian Portuguese.
- Never execute or schedule a real shutdown during tests. Use `--demo` and `--self-test`.
- Preserve the per-user scheduled task and its cancellation across releases.
- Do not add `/f` or a nonzero `/t` to shutdown: nonzero timeouts imply forced app termination.
- On requested improvements, increment the semantic version, run `scripts/build.ps1`, commit and push, then create and push the matching version tag.
- Wait for the release workflow to complete successfully. Download the published installer and verify against the published SHA256SUMS.txt before reporting delivery.
- Automatic updates must verify official repository URLs and checksums, and defer while a schedule is active.
