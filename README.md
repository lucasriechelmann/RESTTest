# RESTTest

A lightweight REST client for Windows 11. No account, no login, no cloud: your collections are plain JSON files on your machine.

RESTTest does one thing differently from Postman and Insomnia: **base URLs belong to the collection, not to each request.** A collection holds a list of named base URLs (for example Local, Staging, Production). Pick one in the toolbar and every request in the collection points at it. Requests store only a method and a relative path, so there is nothing to edit per request and no `{{variable}}` syntax to learn.

## Features

- **Collections** with a folder and request tree: inline rename (F2), duplicate, delete, and drag-and-drop reordering.
- **Collection-wide base URLs**, switched from a toolbar combo box. A path that is itself a full `http(s)` URL overrides the base for one-off calls.
- **Request builder**: method, path, query parameters and headers (each row can be toggled off), and a live preview of the resolved URL.
- **JSON body editor** with syntax highlighting, bracket matching, Ctrl+F search, and Format / Minify.
- **Auth**: none or bearer token.
- **Response pane**: colour-coded status, time and size, pretty-printed preview, raw body, and headers. Transport errors (DNS, TLS, timeout) are shown clearly rather than as exceptions.
- **Auto-save**: edits are saved shortly after you stop typing, and the last open request is restored on launch.
- Follows the Windows light or dark theme.

### Shortcuts

| Key | Action |
|---|---|
| Ctrl+Enter | Send the request |
| Ctrl+N | New request |
| Ctrl+Shift+N | New folder |
| F2 | Rename the selected item |
| Del | Delete the selected item |

## Requirements

- Windows 10/11 (Windows 11 recommended for the Mica backdrop)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build and run

```bash
dotnet build RESTTest.slnx
dotnet run --project RESTTest
```

Run the tests:

```bash
dotnet test RESTTest.Tests
```

On first launch the app creates a "Sample API" collection pointed at JSONPlaceholder so there is something to send immediately.

## Where your data lives

Everything is stored under `%APPDATA%\RestTest`:

- `collections\<id>.json`: one human-readable, diffable file per collection.
- `settings.json`: the last open collection and request.

**Note:** bearer tokens are saved in plain text inside the collection files. Don't share those files if they contain real tokens.

TLS certificate errors are tolerated only for loopback hosts (`localhost`, `127.0.0.1`, `::1`), so self-signed local dev servers work while remote hosts are validated normally.

## Project layout

| Path | Purpose |
|---|---|
| `RESTTest/` | The WPF app: Models, Services, ViewModels, Views, Behaviors, Controls, Converters |
| `RESTTest.Tests/` | xUnit tests for ViewModels, the store, URL building and JSON helpers |

Built with .NET 10 and WPF using strict MVVM, with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [WPF-UI](https://github.com/lepoco/wpfui) and [AvalonEdit](https://github.com/icsharpcode/AvalonEdit).

For architecture, conventions and a feature-to-code map, see [CLAUDE.md](CLAUDE.md) and `.claude/knowledge/`.

## Not yet supported

Request history, import from Postman or Insomnia, variables beyond the base URL, saving responses to disk, non-JSON body types (form, multipart, XML), and auth types other than bearer.
