# codeviewer

Lightweight Windows code viewer/editor. WinForms + Scintilla (the same editing engine as Notepad++). Not an IDE: open, read, edit, save.

~13 MB private memory / ~55 MB working set with several tabs open. 4.4 MB on disk (framework-dependent build).

## Install

### Windows

1. Open the [latest GitHub release](https://github.com/aaditkedia/codereviewer/releases/latest).
2. Under **Assets**, download `codeviewer.exe`.
3. Move it to a permanent folder such as `%LOCALAPPDATA%\Programs\codeviewer\`.
4. Double-click it to run. No installer, administrator access, or separate .NET installation is required.

codeviewer is currently unsigned, so Windows SmartScreen may show a warning on first launch. Confirm that the download came from this repository, select **More info**, then **Run anyway**.

To make codeviewer the default for a file type, right-click a file, choose **Open with > Choose another app > Choose an app on your PC**, select `codeviewer.exe`, and enable **Always**. Because it is a portable app, keep the executable in the same location afterward.

### macOS

Download the archive for your Mac from the latest release:

- Apple silicon (M1/M2/M3/M4/M5): `codeviewer-osx-arm64.zip`
- Intel Mac: `codeviewer-osx-x64.zip`

Unzip it, drag `codeviewer.app` into **Applications**, then Control-click the app and choose **Open** on first launch. The current macOS build is ad-hoc signed rather than Apple-notarized, so Gatekeeper may ask you to confirm it.

### Linux

Download `codeviewer-linux-x64.tar.gz` for most Intel/AMD computers or `codeviewer-linux-arm64.tar.gz` for an ARM64 computer. Then run:

```sh
tar -xzf codeviewer-linux-x64.tar.gz
cd codeviewer-linux-x64
./install.sh
```

The installer is per-user: it copies the app under `~/.local/share`, adds a launcher under `~/.local/bin`, and installs a desktop-menu entry. It does not need `sudo`. A modern graphical Linux distribution with X11 or XWayland is required.

### Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```
git clone https://github.com/aaditkedia/codereviewer.git
cd codereviewer
dotnet build -c Release
bin\Release\net10.0-windows\codeviewer.exe [files or folders...]
```

## Features

- Syntax coloring: TS/JS/JSX, Python, Java, C#, C/C++, Go, Rust, PHP, Kotlin, SQL, HTML/CSS, JSON, YAML, XML, Markdown, LaTeX/BibTeX, shell/bash, PowerShell, Dockerfile, Terraform, batch, ini/env/toml, Makefile, proto
- **Dark mode by default** with VS Code Dark+-style syntax colors. The always-visible **Dark mode** toolbar button toggles it, and the choice persists across runs (`%APPDATA%\codeviewer\settings.txt`).
- **Markdown preview**: View > Markdown Preview (Ctrl+Shift+V) renders the file side by side, live as you type. `codeviewer --preview notes.md` opens with the preview already on.
- **Image viewing**: PNG, JPEG, GIF, BMP, TIFF, ICO, WebP, AVIF, HEIC/HEIF, DDS, JPEG XR, and major camera RAW files open in image tabs. Modern and RAW formats use installed Windows codecs. Use the toolbar or mouse wheel to zoom, drag to pan, and double-click to switch between fit-to-window and actual size.
- **Markdown compile**: Tools > Compile Markdown (F7) saves the active `.md` file, writes a clean standalone `.html` file next to it, and opens it in your browser.
- **LaTeX compile**: Tools > Compile LaTeX (F6) saves the active `.tex` file, builds it, and opens the PDF. It prefers `latexmk`, then `tectonic`, then the engine itself, and repeats the run (invoking `bibtex` or `biber` when the document cites anything) until cross-references, the table of contents and the bibliography settle. The engine follows a `% !TEX program = ...` line, or switches to XeLaTeX/LuaLaTeX on its own for documents using `fontspec`, `unicode-math` or `polyglossia`. Compile errors open in a tab together with the `.log` file. TeX is looked up on `PATH` and in the usual install locations — MacTeX's `/Library/TeX/texbin`, TeX Live's year-stamped tree, MiKTeX's program directory, TinyTeX — because a windowed app does not inherit the PATH from your shell profile. Set `CODEVIEWER_LATEX_PATH` to the directory holding the binaries if yours lives somewhere else.
- **Docker**: Tools > Docker (Ctrl+Shift+D) lists containers and images. Double-click a container for its logs; right-click for Inspect / Start / Stop. `codeviewer --docker` jumps straight there.
- Tabs (middle-click or Ctrl+W to close, right-click for Close All / Copy Path)
- Folder sidebar (File > Open Folder or drop a folder on the window), skips node_modules/.git/bin/obj
- Drag & drop files or folders onto the window
- Indentation guides, auto-indent on Enter (extra level after `{` or `:`), line numbers, current-line highlight
- Always-visible **Wrap text** toggle (or Alt+Z), plus Ctrl+scroll editor zoom
- Keeps each text file's original encoding/BOM on save, warns on unknown binary files, and guards against excessively large files/images
- Shortcuts: Ctrl+O open file, Ctrl+K open folder, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+W close tab

The Windows build retains its native WinForms/Scintilla implementation and Windows codec integration. macOS and Linux use the separate Avalonia/AvaloniaEdit implementation, preserving the core editor, folder sidebar, themes, wrapping, image tabs, Markdown preview/compile, LaTeX compilation, and Docker overview without changing the Windows binary.

## Register file associations from source

```
dotnet publish -c Release -o dist   # stable exe location the registry points at
.\register.ps1                      # HKCU only, no admin
```

`register.ps1` adds codeviewer to the "Open with" dropdown for 88 code, data, and image extensions and makes it the double-click default for any extension no other app owns. Extensions already claimed by another app (Windows protects those with UserChoice) need a one-time right-click > Open with > codeviewer > Always. `unregister.ps1` undoes everything.

Don't move or delete `dist\` after registering, the associations point at it.

## Privacy and security

codeviewer has no accounts, telemetry, analytics, cloud backend, or bundled credentials. Editing, Markdown/LaTeX compilation, and Docker commands run locally. A Markdown document can still reference remote images, and clicking an `http` or `https` link opens it in your default browser. Raw HTML is disabled in Markdown rendering, and the embedded preview blocks navigation to local files and non-web URL schemes.

<!-- codeviewer -->
