# Feature map

Paths are folders unless an entry point is named. All app code is under `RESTTest/`.

## Collections and base URLs
Create, rename and delete collections; each has a list of named base URLs and an active one chosen from a toolbar combo box. Switching it re-targets every request. A "Manage URLs" dialog edits the list live.
- Owner: `ViewModels/CollectionViewModel.cs`, `EnvironmentViewModel.cs`; commands in `MainViewModel` (Collection commands region); dialog in `Views/EnvironmentsDialog.*`.
- Persistence: `Models/CollectionModel.cs`, `EnvironmentModel.cs`.

## Request tree (sidebar)
Folders and requests in a tree with inline rename (F2 / on create), duplicate (deep copy, fresh ids), delete with confirmation (counts nested items), context menu, and drag-and-drop reorder/reparent with an insertion indicator and hover-to-expand. Ctrl+N new request, Ctrl+Shift+N new folder.
- Owner: `ViewModels/NodeViewModel.cs`, tree operations in `CollectionViewModel`, drop contract `INodeDropHandler`.
- View: `Behaviors/` (`TreeViewDragDrop`, `TreeViewSelection`, `FocusBehavior`), sidebar section of `Views/MainWindow.xaml`.

## Building a request
Method, relative path, query parameters and headers (enable/disable per row, tab headers show the active count), auth (none or bearer token), and a body with a type picker: JSON, Text, XML, Form (urlencoded), Form (multipart, with file uploads via a Browse button) or No body. JSON and XML have Format and Minify; Text and XML use the uncoloured editor; a warning shows when the method will not send a body. Switching type keeps the typed text, so switching back loses nothing. A live "resolved URL" preview with copy.
- Body: `RequestViewModel.BodyMode` (`BodyKind`), `FormFields` (`KeyValueViewModel`), `MultipartParts` (`MultipartPartViewModel`); XML helpers in `Services/XmlText.cs`; Content-Type guesses for uploads in `Services/MimeTypes.cs`. The file picker is `MainViewModel.PickMultipartFile` via `IDialogService.PickFile`.
- What goes on the wire: `Services/HttpRequestSender.BuildContent`.
- Owner: `ViewModels/RequestViewModel.cs`, `KeyValueViewModel.cs`.
- URL composition: `Services/HttpRequestSender.BuildUrl` (an absolute http(s) path overrides the base URL).
- JSON helpers: `Services/JsonText.cs`. Editor: `Controls/JsonEditor.cs`, `BracketHighlightRenderer.cs`, `Services/JsonBracketMatcher.cs`, syntax rules in `Resources/Json.xshd`.

## Sending and viewing responses
Send (Ctrl+Enter) with cancel and a 120 s timeout; response pane shows status pill (coloured by class), time, size, pretty-printed Preview, Raw, and Headers tabs, plus copy-body. Transport errors are shown as a "No response" state with a message.
- Owner: `ViewModels/MainViewModel.cs` (Sending region), `ResponseViewModel.cs`, `Services/HttpRequestSender.cs`, `HttpExchangeResult.cs`.
- Colours: `Converters/StatusBrushConverter.cs`, `MethodBrushConverter.cs`.

## Assertions (the Tests tab)
Per-request checks against the response: target (status code, response time, header, JSON path, body text), operator (equals, does not equal, contains, does not contain, exists, less/greater than) and an expected value. The operator list narrows to what suits the target. Evaluated after every Send; the response pane shows "n/m tests passed" next to the status pill and a Tests tab listing each result. Disabled rows are skipped. A transport failure fails every assertion.
- Owner: `ViewModels/AssertionViewModel.cs` (persisted row), `RequestViewModel` (`Assertions`, `AddAssertion`/`RemoveAssertion`), `ResponseViewModel` (`TestResults`, runtime only).
- Logic: `Services/AssertionEvaluator.cs` (pure), `Services/JsonPathLite.cs` (`$.a.b[0]` and `['quoted']` only, no wildcards or filters), `AssertionResult` record.
- Persistence: `Models/AssertionModel.cs`, `AssertionTarget.cs`, `AssertionOperator.cs`; stored in `RequestModel.Assertions`.
- View: Tests tab in `Views/MainWindow.xaml`; labels via `Converters/AssertionLabelConverter.cs`.

## Collection runner
Run button (and context menu) runs every request in the selected folder, or the whole collection when a request or nothing is selected, one after another against the active base URL, then shows a report: per-request state (passed, failed, no tests, skipped), status, time, and the failing assertion messages, plus a summary. Run again and Stop buttons; closing the dialog cancels. It does not touch the main response pane and never marks the collection dirty.
- Owner: `ViewModels/RunnerViewModel.cs`, `RunItemViewModel.cs` (each holds a `RequestModel` snapshot taken at start); started from `MainViewModel.RunSelected`.
- View: `Views/RunnerDialog.xaml`, opened through `IDialogService.ShowRunner`.

## cURL copy and import
**Copy as cURL** (the "cURL" button beside the resolved URL, or the tree context menu) puts the open request on the clipboard as a multi-line command for POSIX shells (bash, zsh, Git Bash, WSL): method, resolved URL with query, enabled headers, bearer auth as a header, and the body (`--data-raw`, `--data-urlencode`, or `-F` / `--form-string` with `;type=` for files). **Import cURL** (top bar, or context menu) opens a multi-line paste box and adds the command to the open collection as a new request next to the selection. It reads browser "Copy as cURL (bash)" output, including `$'...'` quoting and `\` line continuations; a command it cannot read is explained and the text offered again.
- A URL on the collection's active base URL becomes a relative path; any other host is kept whole in the path (the sender accepts that) and the user is told. Query strings become editable rows. A bearer `Authorization` header becomes the Bearer auth type; `-u` becomes a Basic header; `-b`, `-A`, `-e` become headers; `--json`, `-G`, `-I` and `-F` are understood.
- Ignored, with a note when it changes meaning: `-o/-O`, proxies, `-T`, `-k`, bodies or cookies read from a file. Windows cmd (`^`) commands are refused with a pointer to the bash flavour.
- Owner: `Services/CurlExporter.cs`, `Services/CurlParser.cs` (pure text/model code); `MainViewModel.CopyAsCurl` and `ImportCurl`; the paste box is `PromptDialog` in multi-line mode (`IDialogService.Prompt(..., multiline: true)`).

## Request history
Every Send is recorded (not user-cancelled ones, not collection runs): the request as sent, the resolved URL, status, timing, size, headers, the response body and the test result. The History button (top bar, Ctrl+H) opens a dialog: entries newest first on the left; on the right the response, headers and request body. "Save as request" copies the stored request into the open collection next to the selection; Delete and Clear all (confirmed) remove entries. Global across collections, capped at 100 entries, response bodies cut at 100,000 characters (flagged in the dialog).
- Owner: `ViewModels/HistoryViewModel.cs` (list, record, commands), `HistoryEntryViewModel.cs` (read-only row); recording, saving and "Save as request" are wired in `MainViewModel`.
- Persistence: `Models/HistoryEntryModel.cs`, `Services/HistoryStore.cs` (`history.json` in the workspace).
- View: `Views/HistoryDialog.xaml`, opened through `IDialogService.ShowHistory`.

## Persistence and session restore
Auto-save (600 ms debounce) of collections as JSON, flush on close, and restoring the last collection and selected request. First run seeds a "Sample API" collection.
- Owner: `ViewModels/EditableViewModel.cs` (dirty bubbling), `MainViewModel` (Loading and saving region), `Services/CollectionStore.cs`, `Models/AppSettings.cs`.

## Search in editors
Ctrl+F find panel in the JSON editors with amber match highlighting. The panel keeps AvalonEdit's light styling on purpose.
- Owner: `Controls/JsonEditor.cs`.

## Not built (do not assume these exist)
Import from Postman/Insomnia, variables beyond the base URL, saving responses to disk, binary or GraphQL body types, XML syntax colouring, response diffing, auth types beyond bearer, cookies.

<!-- manual:start -->
<!-- manual:end -->
