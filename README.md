# codeviewer

Lightweight Windows code viewer/editor. WinForms + Scintilla (the same editing engine as Notepad++). Not an IDE: open, read, edit, save.

~13 MB private memory / ~55 MB working set with several tabs open. 4.4 MB on disk (framework-dependent build).

## Install on another PC

Grab `codeviewer.exe` from [Releases](https://github.com/aaditkedia/codereviewer/releases): a single self-contained exe, no .NET install needed. Put it anywhere and run it.

Or build from source (needs the .NET 10 SDK):

```
git clone https://github.com/aaditkedia/codereviewer.git
cd codereviewer
dotnet build -c Release
bin\Release\net10.0-windows\codeviewer.exe [files or folders...]
```

## Features

- Syntax coloring: TS/JS/JSX, Python, Java, C#, C/C++, Go, Rust, PHP, Kotlin, SQL, HTML/CSS, JSON, YAML, XML, Markdown, LaTeX/BibTeX, shell/bash, PowerShell, Dockerfile, Terraform, batch, ini/env/toml, Makefile, proto
- **Dark mode by default** with VS Code Dark+-style syntax colors; View > Light Mode toggles the light theme. Choice persists across runs (`%APPDATA%\codeviewer\settings.txt`).
- **Markdown preview**: View > Markdown Preview (Ctrl+Shift+V) renders the file side by side, live as you type. `codeviewer --preview notes.md` opens with the preview already on.
- **Markdown compile**: Tools > Compile Markdown (F7) saves the active `.md` file, writes a clean standalone `.html` file next to it, and opens it in your browser.
- **LaTeX compile**: Tools > Compile LaTeX (F6) runs pdflatex / xelatex / tectonic (whichever is installed) on the active .tex file and opens the PDF. Compile errors open in a tab.
- **Docker**: Tools > Docker (Ctrl+Shift+D) lists containers and images. Double-click a container for its logs; right-click for Inspect / Start / Stop. `codeviewer --docker` jumps straight there.
- Tabs (middle-click or Ctrl+W to close, right-click for Close All / Copy Path)
- Folder sidebar (File > Open Folder or drop a folder on the window), skips node_modules/.git/bin/obj
- Drag & drop files or folders onto the window
- Indentation guides, auto-indent on Enter (extra level after `{` or `:`), line numbers, current-line highlight
- Word wrap toggle (View menu), Ctrl+scroll zoom
- Keeps each file's original encoding/BOM on save, warns on binary files, refuses files over 50 MB
- Shortcuts: Ctrl+O open file, Ctrl+K open folder, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+W close tab

## File associations (Open with / default app)

```
dotnet publish -c Release -o dist   # stable exe location the registry points at
.\register.ps1                      # HKCU only, no admin
```

`register.ps1` adds codeviewer to the "Open with" dropdown for ~60 code/data extensions and makes it the double-click default for any extension no other app owns. Extensions already claimed by another app (Windows protects those with UserChoice) need a one-time right-click > Open with > codeviewer > Always. `unregister.ps1` undoes everything.

Don't move or delete `dist\` after registering, the associations point at it.
