# Contributing to EtherTransfer

Thank you for your interest in contributing! EtherTransfer prioritizes **stability, reliability, and security** over new features. 

## How to Contribute

1. **Report Bugs**: If you find a bug (especially crashes, UI freezes, or silent failures), please open an issue.
2. **Submit PRs**: Feel free to submit pull requests for bug fixes or optimizations.

## Development Guidelines

- **No Silent Failures**: All exceptions that impact functionality (e.g. failing to bind to a port, failing to read a file) must be bubbled up to the user with a clear error message. Do not use empty catch blocks unless absolutely necessary (and if so, explain why).
- **Asynchronous Code**: Use `Task`-based asynchronous patterns extensively. Ensure `CancellationToken`s are propagated properly to prevent resource leaks when transfers are cancelled.
- **Security**: Do not trust file paths received over the network. Always run them through `PathSanitizer.cs`.
- **UI Responsiveness**: Never block the Avalonia UI dispatcher thread with heavy file I/O or network calls.

Please ensure your code builds cleanly without warnings before submitting a PR.
