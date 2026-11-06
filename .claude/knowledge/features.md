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
Method, relative path, query parameters and headers (enable/disable per row, tab headers show the active count), auth (none, bearer token, Basic username/password, or API key sent as a header or a query parameter), and a body with a type picker: JSON, Text, XML, Form (urlencoded), Form (multipart, with file uploads via a Browse button) or No body. JSON and XML have Format and Minify; Text and XML use the uncoloured editor; a warning shows when the method will not send a body. Switching type keeps the typed text, so switching back loses nothing. A live "resolved URL" preview with copy.
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
**Copy as cURL** (the "cURL" button beside the resolved URL, or the tree context menu) puts the open request on the clipboard as a multi-line command for POSIX shells (bash, zsh, Git Bash, WSL): method, resolved URL with query, enabled headers, bearer and API-key-header auth as headers, Basic auth as `-u` (a header when the username holds a colon), a query-string API key inside the URL, and the body (`--data-raw`, `--data-urlencode`, or `-F` / `--form-string` with `;type=` for files). **Import cURL** (top bar, or context menu) opens a multi-line paste box and adds the command to the open collection as a new request next to the selection. It reads browser "Copy as cURL (bash)" output, including `$'...'` quoting and `\` line continuations; a command it cannot read is explained and the text offered again.
- A URL on the collection's active base URL becomes a relative path; any other host is kept whole in the path (the sender accepts that) and the user is told. Query strings become editable rows. A bearer `Authorization` header becomes the Bearer auth type; `-u` and a decodable `Authorization: Basic ...` header become the Basic auth type; `-b`, `-A`, `-e` become headers; `--json`, `-G`, `-I` and `-F` are understood.
- Ignored, with a note when it changes meaning: `-o/-O`, proxies, `-T`, `-k`, bodies or cookies read from a file. Windows cmd (`^`) commands are refused with a pointer to the bash flavour.
- Owner: `Services/CurlExporter.cs`, `Services/CurlParser.cs` (pure text/model code); `MainViewModel.CopyAsCurl` and `ImportCurl`; the paste box is `PromptDialog` in multi-line mode (`IDialogService.Prompt(..., multiline: true)`).

## Import a collection (Postman, Insomnia, OpenAPI)
The **Import** button in the sidebar toolbar (or the tree context menu, "Import collection file...") opens a file picker and adds the file as a **new collection**; it never touches the open one. JSON only: a Postman v2.0/2.1 collection, an Insomnia v4 export, or an OpenAPI 3.x / Swagger 2.0 document. YAML (including Insomnia v5 and most hand-written OpenAPI) is refused with a message saying so.
- **Base URLs.** Other tools put a whole URL on every request, so the importer finds the hosts: a Postman variable or Insomnia environment value that is a URL becomes a named base URL (a base with a path, like `https://api.x.com/v1`, keeps it); otherwise the origin does. The most-used one is the active base URL and the requests on it are shortened to paths; requests on other hosts keep their full URL in the path, as with cURL import. Insomnia's named environments (Staging...) that redefine the same URL variable become extra base URLs. OpenAPI `servers` (variables filled with their defaults) or Swagger `host`+`basePath` become the base URLs.
- **What carries over.** Folders (OpenAPI: one per first tag), names, method, query rows, headers (enabled state kept), bodies (JSON, text, XML, urlencoded, multipart incl. file paths, GraphQL as a JSON body), and Basic, Bearer and API-key auth (Postman folder/collection auth is inherited; OpenAPI security follows operation then document). OpenAPI JSON bodies are generated from the schema (examples first, `$ref`s followed, depth-limited).
- **What does not, and is reported.** Scripts, OAuth/other auth, binary file bodies, cookie parameters, path parameters like `{id}` (left in the path), and `{{placeholders}}` with no value in the file (left as typed). A notes dialog lists each loss once.
- Owners: `Services/Importing/` (`CollectionImporter` detects the format; `PostmanImporter`, `InsomniaImporter`, `OpenApiImporter`; shared `BaseUrlGrouper`, `PlaceholderResolver`), `MainViewModel.ImportCollection`.

## Response diff (the Diff tab)
The response pane has a **Diff** tab that shows what changed since the last time this request was sent. After a send it compares the new response with the request's **previous** response; **Pin this response** keeps one as the baseline instead (later sends of that request are compared with it until **Unpin**). The first send has nothing to compare with and says so.
- **What is compared.** The summary line gives a status change ("Status 200 OK → 404 Not Found") and the body result ("Body: +3 -4" or "no differences"). Bodies are pretty-printed the same way first, so a reformatted but equal JSON body is not a change. Binary bodies are compared byte for byte ("identical" or "differs", with sizes) and have no line list.
- **The list** shows removed lines (red, `-`) and added lines (green, `+`) with old and new line numbers, plus 3 lines of context; longer unchanged runs fold into "N unchanged lines". **Show unchanged lines** lists everything. A text over 20,000 lines is not compared; one that differs in more than about 2,000 lines is shown as all removed then all added.
- **In memory only.** Previous and pinned responses are per request, kept while the app runs, never saved (a response is never persisted). Selecting another request clears the pane, so a comparison appears after a send. A failed send is not a response: it is not compared and does not replace the baseline.
- At the minimum window size the response pane is short, so the list shows only a couple of lines and scrolls; drag the splitter above it for more.
- Not built: comparing two history entries.
- Owners: `Services/TextDiff.cs` (Myers line diff), `DiffResult.cs`, `DiffLine.cs`, `DiffKind.cs`; `ViewModels/ResponseBaselines.cs` (per-request memory), `ResponseBaseline.cs`, `ResponseViewModel` (Diff state, `PinBaselineCommand`), `MainViewModel.PinResponse`.

## Cookie jar
Cookies a server sets are kept and sent back on later requests to that host, like a browser: log in once, then call the protected endpoints. The **Cookies** button (cookie icon, top bar) opens the jar: a switch ("Keep cookies and send them on later requests"), a table of every cookie (host, name, value, path, expiry, flags), a delete button per row and "Clear all" (confirms first). Switched off, nothing is stored or sent and requests go out exactly as typed.
- **Typed Cookie header wins.** A request with its own enabled `Cookie` header skips the jar entirely, so the headers sent are the headers shown.
- **Redirects.** Cookies set on a redirect response (the usual shape of a login) are kept and sent to the redirect target.
- **One jar for the whole app,** keyed by host and path as in a browser, so two collections calling the same host share cookies; cookies ignore the port (`localhost:5000` and `localhost:5001` share).
- **Persisted** to `cookies.json` in the workspace, session cookies included (unlike a browser) so a login survives a restart; expired ones are dropped on load. Secrets are plain text, as for tokens.
- **Copy as cURL** adds `-b 'a=1; b=2'` with the cookies the jar would send (not when a Cookie header is typed). Importing a `-b` command gives a normal `Cookie` header.
- The response Headers tab lists each `Set-Cookie` as its own row.
- Owners: `Services/CookieJar.cs`, `CookieStore.cs`/`ICookieStore.cs`, `HttpRequestSender` (jar client), `ViewModels/CookiesViewModel.cs`, `Views/CookiesDialog.xaml`, `Models/CookieModel.cs` + `CookieJarModel.cs`.

## Binary responses and saving a response
A response body is read as raw bytes. `ResponseBody` decides text or binary: a known text type (text/*, JSON, XML, JavaScript, HTML, CSV...) is text; a known binary type (image, audio, video, font, PDF, zip, octet-stream, vnd.*) is binary; an unknown or missing type is sniffed (a NUL byte or invalid UTF-8 means binary). Text is decoded with the declared charset (a byte-order mark wins). A binary body is not shown as text: the Preview tab shows a notice with its size and type, and Copy explains there is nothing to copy.
The **Save** button (disk icon, left of Copy, in the response header) writes the body to a file **byte for byte** via the save dialog. The proposed name is the server's `Content-Disposition` filename (including RFC 5987 `filename*`, with any directory part and illegal characters removed), else the last URL segment, else `response` plus an extension from the content type (`.bin` for unknown binary, `.txt` for unknown text). A note under the status row says where it was saved; a failed write is explained in a dialog. Nothing can be saved for an error, an empty body or no response.
- Owners: `Services/ResponseBody.cs`, `Services/ResponseFileName.cs`, `Services/MimeTypes.ExtensionFor`, `ViewModels/ResponseViewModel` (`SaveToFileCommand`), `IDialogService.PickSavePath`. History stores text only, so a binary response appears there with an empty body.

## Request history
Every Send is recorded (not user-cancelled ones, not collection runs): the request as sent, the resolved URL, status, timing, size, headers, the response body and the test result. The History button (top bar, Ctrl+H) opens a dialog: entries newest first on the left; on the right the response, headers and request body. "Save as request" copies the stored request into the open collection next to the selection; Delete and Clear all (confirmed) remove entries. Global across collections, capped at 100 entries, response bodies cut at 100,000 characters (flagged in the dialog).
- Owner: `ViewModels/HistoryViewModel.cs` (list, record, commands), `HistoryEntryViewModel.cs` (read-only row); recording, saving and "Save as request" are wired in `MainViewModel`.
- Persistence: `Models/HistoryEntryModel.cs`, `Services/HistoryStore.cs` (`history.json` in the workspace).
- View: `Views/HistoryDialog.xaml`, opened through `IDialogService.ShowHistory`.

## Persistence and session restore
Auto-save (600 ms debounce) of collections as JSON, flush on close, and restoring the last collection and selected request. First run seeds a "Sample API" collection.
- Owner: `ViewModels/EditableViewModel.cs` (dirty bubbling), `MainViewModel` (Loading and saving region), `Services/CollectionStore.cs`, `Models/AppSettings.cs`.

## App icon
The icon (two opposing arrows, request out and response back, on a blue tile) is `RESTTest/Assets/app.ico` (16 to 256 px, PNG-compressed), used as the exe icon (`ApplicationIcon` in the csproj), the window icon and the title bar icon. `app.png` is the 256 px source image. It was drawn by a one-off script, so to change it edit the PNG/ICO directly or redraw it; there is no generator in the repo.

## Search in editors
Ctrl+F find panel in the JSON editors with amber match highlighting. The panel keeps AvalonEdit's light styling on purpose.
- Owner: `Controls/JsonEditor.cs`.

## Not built (do not assume these exist)
YAML import, Postman environment files, adding a cookie by hand, variables beyond the base URL, binary request bodies (sending a file as the whole body), a GraphQL editor, XML syntax colouring, OAuth and other auth types beyond bearer, Basic and API key.

<!-- manual:start -->
<!-- manual:end -->
