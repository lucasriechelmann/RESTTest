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
Method, relative path, query parameters and headers (enable/disable per row, tab headers show the active count), JSON body editor with Format and Minify and a warning when the method will not send a body, auth (none or bearer token). A live "resolved URL" preview with copy.
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

## Persistence and session restore
Auto-save (600 ms debounce) of collections as JSON, flush on close, and restoring the last collection and selected request. First run seeds a "Sample API" collection.
- Owner: `ViewModels/EditableViewModel.cs` (dirty bubbling), `MainViewModel` (Loading and saving region), `Services/CollectionStore.cs`, `Models/AppSettings.cs`.

## Search in editors
Ctrl+F find panel in the JSON editors with amber match highlighting. The panel keeps AvalonEdit's light styling on purpose.
- Owner: `Controls/JsonEditor.cs`.

## Not built (do not assume these exist)
Request history, import from Postman/Insomnia, variables beyond the base URL, saving responses to disk, non-JSON body types (form, multipart, XML), response diffing, auth types beyond bearer, cookies.

<!-- manual:start -->
<!-- manual:end -->
