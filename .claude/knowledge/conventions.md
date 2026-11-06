# Conventions

Each pattern names one real file as the reference example.

## ViewModels

- **Source-generated MVVM.** Classes are `partial`; properties use `[ObservableProperty]` on a `_camelCase` field; commands use `[RelayCommand]` on a private method (`AddHeader()` becomes `AddHeaderCommand`). Reference: `ViewModels/RequestViewModel.cs`.
- **Derived-property notifications** go in `partial void OnXChanged(...)` hooks that call `OnPropertyChanged(nameof(Other))`. Reference: `RequestViewModel.OnMethodChanged` → `SupportsBody`.
- **Persisted ViewModels extend `EditableViewModel`**, own child ViewModels through `Attach`/`TrackChildren`, and override `IsTransient` for view-only or derived properties so they do not mark the collection dirty. Reference: `ViewModels/NodeViewModel.cs` (`IsSelected`, `IsRenaming`, `RenameText` are transient).
- **Model conversion.** `ToModel()` and either a static `FromModel(...)` or a constructor taking the model. Load paths set backing fields or add items *before* calling `Wire()`, so loading from disk is not mistaken for an edit. Reference: `ViewModels/CollectionViewModel.cs`.
- **Never name an `[ObservableProperty]` field `_property`** (or `_propertyName`-style names that make the toolkit emit `OnPropertyChanged(string)` / `OnPropertyChanging(string)`). The generated partial hook shadows `ObservableObject.OnPropertyChanged(string)` and silently turns every `OnPropertyChanged(nameof(...))` in that class into a no-op. Tests stay green and the UI just stops updating. Reference: `AssertionViewModel.Selector`, and `Changing_the_target_notifies_the_view_about_every_derived_property` which guards it.
- **Fix dependent state before announcing derived properties** when one property constrains another (target narrows the operator list): coerce first, then raise the option-list change, or a bound combo box writes null back. Reference: `AssertionViewModel.OnTargetChanged`.
- **Read-only runtime state** (not persisted) derives from `ObservableObject`, not `EditableViewModel`. Reference: `ViewModels/ResponseViewModel.cs`.
- **Instance properties for XAML bindings** (e.g. `MethodOptions`, `Methods` on `MainViewModel`) so plain `{Binding}` works without `x:Static`.
- **Tree surgery lives in `CollectionViewModel`** (`Move`, `CanMove`, `Remove`, `GetInsertTarget`), reached from the view via `INodeDropHandler`. Views never edit collections directly.

## Dialogs and UI services

- ViewModels call `IDialogService` (`Confirm`, `Prompt`, `ShowMessage`, `ShowEnvironments`, `ShowRunner`, `ShowHistory`, `PickFile` (optional file-dialog filter), `PickSavePath`, `ShowCookies`, `CopyToClipboard`). `ShowRunner` takes an already-started `RunnerViewModel` and cancels it when the dialog closes. When a dialog's ViewModel needs something only `MainViewModel` can do (e.g. history's "Save as request"), pass a callback property (`HistoryViewModel.SaveAsRequestHandler`) instead of a reference to the other ViewModel. The implementation in `Views/DialogService.cs` is the only place windows are constructed. Destructive actions confirm first (`MainViewModel.DeleteSelected`).
- The environments dialog binds straight to the live `CollectionViewModel`: no apply/cancel state, auto-save picks up edits as typed. Reference: `Views/EnvironmentsDialog.xaml.cs`.

## Views, behaviors, controls

- **Code-behind is minimal.** `MainWindow.xaml.cs` only flushes saves on close. Anything else belongs in a ViewModel or an attached behavior.
- **Attached behaviors** (static classes with attached DependencyProperties, a private `Controller`/hook state property to avoid leaks) carry WPF-only concerns: `Behaviors/TreeViewDragDrop.cs` (hit-testing, drop adorner, hover-to-expand; requires the row element named `NodeRow`), `TreeViewSelection.cs` (bindable `SelectedItem`, select on right-click), `FocusBehavior.cs` (focus on visible, commit on lost focus).
- **`Controls/JsonEditor.cs`** wraps AvalonEdit with a bindable `BoundText`, re-entrancy guard, caret preservation, bracket highlighting and a search panel. Use it for any JSON/code display. Set `HighlightJson="False"` to show plain text, XML or form lines without JSON colouring (the Body tab's Text/XML editor and history's non-JSON request bodies do). Style via `JsonEditorStyle` in `App.xaml`.
- **Converters** live in `Converters/`, are registered in `App.xaml` resources with short keys (`BoolToVis`, `InverseBoolToVis`, `MethodBrush`, `StatusBrush`, `AuthKindLabel`, `ApiKeyLocationLabel`, `AssertionLabel`). Prefer exposing a plain bool/instance property on the ViewModel over chaining converters.
- **Layout rules** (from a layout audit; every item below was a real clipped or hidden control):
  - The window's minimum is 1040 x 640 DIP. Check every view at that size, not only at the default.
  - Never set a fixed `Width` on a WPF-UI `CheckBox`: its template needs about 42 DIP and a smaller width clips the box to a sliver. Give the grid column a fixed 44 and let the box size itself.
  - In a table of rows, every column must be fixed or star-sized and identical in the header grid and the row grid. An `Auto` column is measured per row, so fields drift from row to row and stop lining up with their headers.
  - A horizontal `StackPanel` never shrinks or trims its children, so long text overflows or runs under its neighbour. Use a `Grid` or `DockPanel` with a star column (and `TextTrimming`), or a single `TextBlock` with several `Run`s to trim "name + path" as one line. Use a `WrapPanel` for toolbars and meta rows so the last item wraps instead of clipping.
  - Text a user must read to fix a problem (an error message, a path) wraps; do not trim it.
  - Toolbar actions beside a flexible control are icon-only with `ToolTip` and `AutomationProperties.Name`; flexible combo boxes get a `MinWidth` so they cannot collapse to a bare chevron.
  - Give an `ItemsControl` inside a `ScrollViewer` a right margin, or the scrollbar sits on its last column.
  - A tab whose content can be taller than the request pane at the minimum window size (the Auth forms) wraps its content in a `ScrollViewer`; the TabControl clips instead of scrolling, and a field below the fold simply disappears.
  - Judge layout from a physical-pixel capture of the window. UI Automation reports a control's assigned size, not what is drawn inside it, and does not show clipping.
- **Theme.** WPF-UI controls with `DynamicResource` theme brushes so light and dark both work; `ApplySystemTheme()` at startup.
- Pure, testable helpers are kept as static classes outside the controls: `Services/JsonText.cs` (format/minify/validate), `Services/JsonBracketMatcher.cs`.

## Error handling

- **Network failures are data, not exceptions.** `HttpRequestSender.SendAsync` never throws for transport problems; it returns `HttpExchangeResult.Failure(...)` with a human-readable message. A 500 is a *completed* exchange. Reference: `Services/HttpRequestSender.cs`.
- **Persistence is best-effort and non-fatal.** Unreadable collection files are skipped; settings failures are swallowed; collection save/delete failures surface via `MainViewModel.StatusMessage`. Reference: `Services/CollectionStore.cs`.
- **Validation returns `bool` + out result + error text** rather than throwing. Reference: `JsonText.TryFormat`.
- Clipboard failures are swallowed (`DialogService.CopyToClipboard`).

## Data access

- All HTTP goes through `IHttpRequestSender`, all disk access through `ICollectionStore` and `IHistoryStore`. They are interfaces so ViewModels can be tested with fakes (store tests use the real store against a temp path, see Tests). Both stores use `JsonFile.Options` and `JsonFile.WriteAtomic`, so add any new workspace file the same way rather than re-declaring serializer settings. Collection files are keyed on collection id, not name.
- Response facts shown as text (duration, size, status colour category) come from `Services/DisplayFormat.cs`; do not add another private formatter in a ViewModel.

## Naming and style

- File-scoped namespaces matching folders (`RESTTest.ViewModels`). Nullable enabled, implicit usings on. XML doc comments explain *why*, especially for non-obvious WPF behavior.
- Private fields `_camelCase`; one type per file; `sealed` for non-extended classes.
- IDs are `Guid.NewGuid().ToString("N")` strings.
- Persisted enums serialize as names.

## Logging

None. There is no logging framework. User-visible problems go to `StatusMessage` or the response pane.

## Tests

xUnit in `RESTTest.Tests/`, flat folder, one class per concern (`TreeMoveTests`, `DirtyTrackingTests`, `CollectionStoreTests`, `UrlBuildingTests`, `JsonTextTests`, `NodeViewModelTests`, `BracketMatchingTests`, `AssertionEvaluatorTests`, `JsonPathLiteTests`, `AssertionViewModelTests`, `RunnerViewModelTests`, `HistoryViewModelTests`, `HistoryStoreTests`, `DisplayFormatTests`, `BodyBuildingTests`, `BodyKindViewModelTests`, `XmlTextTests`, `RequestBodyPreviewTests`, `CurlParserTests`, `CurlExporterTests`, `CurlRoundTripTests`, `AuthTests`, `CollectionImporterTests`, `ResponseSavingTests`, `CookieJarTests`, `ResponseDiffTests`, `JsonPathQueryTests`). The cURL exporter and parser are also round-trip tested against each other (export a request, parse it back, compare); keep that pairing when changing either, and when a quoting change matters, check it against real bash and curl once, not only against the paired parser. `BodyBuildingTests` includes end-to-end cases that send through the real `HttpRequestSender` to a one-shot loopback `TcpListener` and assert on the raw bytes; reuse that helper (`BodyBuildingTests.StartCapturingServer`, internal) when a change must be proven on the wire, as `AuthTests` does; when several requests must be seen in a row (cookies, redirects), `CookieJarTests.Serve` answers each connection from a list of canned responses and returns every raw request. Runner tests use a hand-written `FakeSender : IHttpRequestSender` defined inside the test class; ViewModel tests that need dialogs share `FakeDialogService` (set `ConfirmResult`, read `ConfirmMessages`). `MainViewModel` itself is not unit tested (it owns a `DispatcherTimer`), so keep logic in the smaller ViewModels it composes. `CollectionStoreTests` uses a temp-directory workspace and is `IDisposable`. Tests cover Models, ViewModels and Services only; the UI is verified by hand (see CLAUDE.md).

## Competing patterns

| Concern | Canonical | Legacy / to avoid |
|---|---|---|
| Showing code or JSON | `JsonEditor` + `JsonEditorStyle` | `TextBox` + `CodeEditorStyle` (still used by the response Raw tab in `Views/MainWindow.xaml`) |
| Which methods send a body | `HttpRequestSender.BodyCapableMethods` | Duplicate set in `RequestViewModel`, kept only for the UI warning |

<!-- manual:start -->
<!-- manual:end -->
