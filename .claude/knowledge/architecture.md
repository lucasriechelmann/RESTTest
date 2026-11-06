# Architecture

## Projects and dependencies

```
RESTTest.Tests  --->  RESTTest (app)
```

Only one real dependency edge. The test project references the app project and needs `UseWPF` because the assembly under test contains WPF types (`DispatcherTimer` in `MainViewModel`, the attached behaviors).

Inside the app, the layers depend downward only:

```
Views / Behaviors / Controls / Converters   (WPF-aware)
        |
   ViewModels   (CommunityToolkit.Mvvm; no view types)
        |
Services (interfaces + implementations)  ->  Models (DTOs)
```

Exceptions to note: `IDialogService` lives in `Services/` but its signature mentions `CollectionViewModel`; its implementation `DialogService` lives in `Views/` so that no ViewModel ever touches a `Window`. `MainViewModel` uses `DispatcherTimer` (WPF) for the save debounce.

## Composition root

`App.xaml.cs` `OnStartup` wires everything by hand: `CollectionStore`, `HttpRequestSender`, `DialogService` into `MainViewModel`, then `MainWindow(viewModel)`, then `dialogs.Owner = window`. No DI container, no `StartupUri`. Theme follows the Windows setting via `ApplicationThemeManager.ApplySystemTheme()`.

## End-to-end data flow

1. **Startup.** `MainViewModel` ctor calls `Load()`: `ICollectionStore.LoadAll()` returns `CollectionModel`s, each becomes a `CollectionViewModel` via `FromModel`. First run with no files creates a "Sample API" collection (JSONPlaceholder). `AppSettings` restores the last collection and selected node.
2. **Editing.** Views bind to ViewModels. Any non-transient property change raises `EditableViewModel.Dirty`, which bubbles up the ownership chain (KeyValue → Request → Node → Collection) to `MainViewModel.OnCollectionDirty`.
3. **Saving.** `QueueSave` adds the collection to a pending set and restarts a 600 ms `DispatcherTimer`. On tick (or window close via `FlushPendingSaves`) each pending collection is converted with `ToModel()` and written by `ICollectionStore.Save`. Writes are atomic (temp file + `File.Replace`).
4. **Sending.** `SendCommand` calls `IHttpRequestSender.SendAsync(collection.ActiveBaseUrl, request.ToModel(), token)` with a 120 s timeout. The result (`HttpExchangeResult`, which also represents transport failures) is loaded into `ResponseViewModel` together with `AssertionEvaluator.EvaluateAll(model.Assertions, result)`. The response and the test results are never persisted.
4b. **Running.** `RunSelectedCommand` snapshots each request in scope with `ToModel()`, builds a `RunnerViewModel` and starts `RunAsync()` before `IDialogService.ShowRunner` blocks (the dialog's message loop delivers the continuations). Requests are sent sequentially against the base URL captured at start; each gets its own 120 s timeout linked to the run's cancellation. The run reuses `IHttpRequestSender` and `AssertionEvaluator`, so single sends and runs cannot disagree.
5. **URL preview.** `RefreshResolvedUrl` calls `IHttpRequestSender.BuildUrl` on every dirty event and selection change to show the absolute URL under the path box.
6. **Shutdown.** `MainWindow.OnClosing` flushes pending saves and calls `SaveUiState`.

## Data entities

All persisted DTOs are in `RESTTest/Models/`. Each has a matching ViewModel with `ToModel`/`FromModel` (or a model-taking constructor).

| Entity (Model) | ViewModel | Notes |
|---|---|---|
| `CollectionModel` | `CollectionViewModel` | Id, Name, `Environments`, `ActiveEnvironmentId`, `Items` (tree). One JSON file per collection. |
| `EnvironmentModel` | `EnvironmentViewModel` | A named base URL. Collection-wide by design. |
| `NodeModel` (`NodeKind`: Folder/Request) | `NodeViewModel` | Single recursive type for folders and requests. `Request` is set only for requests. Carries `IsExpanded`. |
| `RequestModel` | `RequestViewModel` | Method, Path (relative to the active base URL, or an absolute http(s) URL that overrides it), Body, `Auth` (`AuthKind`: None/BearerToken), BearerToken, QueryParams, Headers, Assertions. |
| `KeyValueModel` | `KeyValueViewModel` | Enabled/Key/Value row for params and headers. Also reused for response headers. |
| `AssertionModel` (`AssertionTarget`, `AssertionOperator`) | `AssertionViewModel` | Enabled, Target, Selector (header name or JSON path), Operator, Expected. Option lists and visibility flags on the ViewModel are transient. |
| `AssertionResult` (Services/, record) | (shown by `ResponseViewModel.TestResults` and `RunItemViewModel`) | Runtime only, never persisted. |
| (none) | `RunnerViewModel`, `RunItemViewModel` | Runtime only. A run holds `RequestModel` snapshots and results; not `EditableViewModel`s. |
| `AppSettings` | (none) | `LastCollectionId`, `LastSelectedNodeId`. Stored in `settings.json`. |
| `HttpExchangeResult` (Services/, record) | `ResponseViewModel` | Runtime only, never persisted. |

## Storage

`CollectionStore` writes to `%APPDATA%/RestTest/`: `collections/<id>.json` (one per collection, keyed on id so renames do not orphan files) and `settings.json`. JSON is indented, enums are written as names, nulls omitted, relaxed encoder so embedded JSON bodies stay readable. Unreadable collection files are skipped silently so one bad file cannot block startup. Tests construct the store with a temp path.

## External services

There is no fixed backend. The app calls whatever URL the user configures, through one static `HttpClient` (`SocketsHttpHandler`) in `HttpRequestSender`: redirects followed, cookies off, automatic decompression, infinite client timeout (per-request `CancellationTokenSource` instead), and TLS certificate errors tolerated **only for loopback hosts**. Only `http` and `https` URLs are accepted.

## Decisions and reasons

- **Collection-wide base URLs** instead of per-request hosts or variables. Reason: the owner wanted to run the same request set against different environments without per-request edits and without `{{baseUrl}}` indirection.
- **Strict MVVM with separate DTOs.** Reason: keeps the saved JSON format decoupled from UI state and keeps Views logic-free.
- **Dirty-event bubbling plus debounced save**, rather than reflection over the object graph or save-on-every-keystroke. Reason: one write per burst of edits.
- **Hand-wired composition root, no DI container.** Reason: a handful of services; explicit wiring is more readable.
- **Bearer tokens stored as plain text** in the collection JSON. Accepted for a local single-user tool; revisit if this ever syncs or is shared.
- **Assertions live on the request, as rows of target / operator / expected**, not as scripts. Reason: the app has no scripting layer and no variable substitution (see the base-URL decision), so a declarative row is enough for status, header, JSON-path and body checks and keeps the saved JSON human-editable. Chaining a value from one response into the next request is a separate, undecided feature.
- **Test results and runs are runtime-only.** Reason: like responses, they are a record of one moment, not authored state; keeping them out of `EditableViewModel` means a run can never trigger a save.
- **"Not" operators pass when the value is absent** (e.g. "header X does not contain Y" on a response without X). Reason: it is the logical reading; use "exists" to require presence.
- **The runner snapshots requests when it starts.** Reason: edits made while a run is going cannot change what is sent, and the runner never mutates editor state.
- **Single recursive `NodeModel`/`NodeViewModel`** for folders and requests. Reason: rename, reorder and drag-and-drop become one uniform operation.
