# RESTTest

A Windows 11 desktop REST client (WPF, .NET 10) built as a no-login alternative to Postman and Insomnia. Its defining design choice: **base URLs belong to the collection, not to each request.** A collection holds a list of named base URLs, one is active, and every request stores only a method and a relative path. Switching the active URL re-targets every request at once. There is deliberately no per-request host field and no `{{variable}}` substitution layer; do not add either unless asked.

## Projects

| Project | Purpose |
|---|---|
| `RESTTest/` | The WPF app (`net10.0-windows`, WinExe). Models, Services, ViewModels, Views, Behaviors, Controls, Converters. |
| `RESTTest.Tests/` | xUnit tests for models, ViewModels, store, URL building, JSON helpers. References the app project. |

Solution file: `RESTTest.slnx`. Packages: CommunityToolkit.Mvvm (source-generated properties/commands), WPF-UI (Fluent theme, Mica window), AvalonEdit (JSON editor).

Build and test:

```bash
dotnet build RESTTest.slnx
dotnet test RESTTest.Tests
```

## Standing rules

- **Search before creating.** Before adding a helper, converter, behavior, or service, look for an existing one. The codebase is small; duplication is easy to avoid.
- **Strict MVVM, no shortcuts.** Models are plain serialization DTOs. ViewModels are the observable, command-bearing layer. Views contain no logic. ViewModels never reference a `Window`, `TreeView`, or any view type: dialogs go through `IDialogService`, drag-and-drop goes through an attached behavior calling `INodeDropHandler`. New persisted state means a DTO, a ViewModel, and a `ToModel`/`FromModel` pair. Do not make Models observable and bind to them.
- **Persisted ViewModels derive from `EditableViewModel`** so changes bubble a `Dirty` event up to `MainViewModel`'s debounced auto-save. View-only properties (selection, rename state) must be listed in `IsTransient`, or clicking around rewrites the file. `ResponseViewModel` is deliberately *not* an `EditableViewModel`.
- **Verify UI changes by running the app.** Green tests are not evidence the UI works. Launch the exe and click through it after any change to the tree, the editors, or the dialogs. Dialog XAML is only parsed the first time the dialog opens, so an unopened dialog is an untested one.
- **`JsonEditor` is the canonical control for showing code or JSON.** The response Raw tab still uses a plain `TextBox` with `CodeEditorStyle`; treat that as a leftover, not a pattern to copy.
- **`HttpRequestSender` is the source of truth for which methods send a body.** `RequestViewModel.BodyCapableMethods` duplicates it only to drive a UI warning; change the sender first, then keep the ViewModel in step.
- **Do not "fix" the white find bar** (Ctrl+F in the JSON editors). It keeps AvalonEdit's light chrome on purpose; a dark template was tried and reverted.
- **Never add a `Co-Authored-By` trailer** to commit messages in this repo (the owner's standing rule).

## Knowledge files

CLAUDE.md loads automatically; these do not. Open them when the condition applies:

- Before adding or changing a data entity, a service boundary, or a cross-project dependency, read `.claude/knowledge/architecture.md`.
- Before writing new code in an area you have not touched this session, read `.claude/knowledge/conventions.md`.
- Before working on a feature, find its owning modules in `.claude/knowledge/features.md`.

## Context maintenance

> In any change that alters the data schema, adds, removes, or renames a project or module, moves a feature to different code, or makes an architecture decision, update CLAUDE.md and the files in `.claude/knowledge/` in the same change, without being asked. When the change records an architecture decision, write the decision and its reason into the knowledge files. Before finishing such a change, re-read the affected sections and confirm they still match the code. Stale context is a bug.

## Team rules

<!-- manual:start -->
<!-- manual:end -->
