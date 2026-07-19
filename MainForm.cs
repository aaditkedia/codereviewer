using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Markdig;
using ScintillaNET;

namespace CodeViewer;

public class MainForm : Form
{
    private const long OpenSizeLimit = 50 * 1024 * 1024; // refuse files above 50 MB

    private static readonly MarkdownPipeline MdPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly SplitContainer _split;
    private readonly TreeView _tree;
    private readonly ThemedTabControl _tabs;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusPath;
    private readonly ToolStripStatusLabel _statusLang;
    private readonly ToolStripStatusLabel _statusPos;
    private readonly ToolStripMenuItem _wordWrapMenu;
    private readonly ToolStripMenuItem _sidebarMenu;
    private readonly ToolStripMenuItem _lightModeMenu;
    private TabPage? _dockerPage;
    private Theme _theme = Theme.Load();

    private sealed class TabState
    {
        public Scintilla Editor = null!;
        public string? FilePath;
        public string? HighlightPath;
        public bool IsDirty;
        public Encoding Encoding = new UTF8Encoding(false);
        public string Language = "Plain text";
        public WebBrowser? Preview;
        public System.Windows.Forms.Timer? PreviewTimer;
    }

    public MainForm(string[] args)
    {
        Text = "codeviewer";
        Width = 1150;
        Height = 740;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        // menu
        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(MenuItem("Open &File...", Keys.Control | Keys.O, (_, _) => OpenFileDialogAction()));
        fileMenu.DropDownItems.Add(MenuItem("Open Fol&der...", Keys.Control | Keys.K, (_, _) => OpenFolderDialogAction()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(MenuItem("&Save", Keys.Control | Keys.S, (_, _) => SaveCurrent()));
        fileMenu.DropDownItems.Add(MenuItem("Save &As...", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveCurrentAs()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(MenuItem("&Close Tab", Keys.Control | Keys.W, (_, _) => { if (_tabs!.SelectedTab != null) CloseTab(_tabs.SelectedTab); }));
        fileMenu.DropDownItems.Add(MenuItem("E&xit", Keys.None, (_, _) => Close()));

        var viewMenu = new ToolStripMenuItem("&View");
        _wordWrapMenu = new ToolStripMenuItem("&Word Wrap") { CheckOnClick = true };
        _wordWrapMenu.Click += (_, _) =>
        {
            foreach (TabPage page in _tabs!.TabPages)
                if (State(page) is { } s)
                    s.Editor.WrapMode = _wordWrapMenu.Checked ? WrapMode.Word : WrapMode.None;
        };
        _sidebarMenu = new ToolStripMenuItem("Folder &Sidebar") { CheckOnClick = true, Checked = true };
        _sidebarMenu.Click += (_, _) => _split!.Panel1Collapsed = !_sidebarMenu.Checked;
        _lightModeMenu = new ToolStripMenuItem("&Light Mode") { CheckOnClick = true, Checked = !_theme.IsDark };
        _lightModeMenu.Click += (_, _) =>
        {
            _theme = _lightModeMenu.Checked ? Theme.Light : Theme.Dark;
            Theme.Save(_theme);
            ApplyTheme();
        };
        viewMenu.DropDownItems.Add(_wordWrapMenu);
        viewMenu.DropDownItems.Add(_sidebarMenu);
        viewMenu.DropDownItems.Add(_lightModeMenu);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(MenuItem("&Markdown Preview", Keys.Control | Keys.Shift | Keys.V, (_, _) => ToggleMarkdownPreview()));

        var toolsMenu = new ToolStripMenuItem("&Tools");
        toolsMenu.DropDownItems.Add(MenuItem("Compile &Markdown", Keys.F7, (_, _) => CompileMarkdown()));
        toolsMenu.DropDownItems.Add(MenuItem("Compile &LaTeX", Keys.F6, async (_, _) => await CompileLatex()));
        toolsMenu.DropDownItems.Add(MenuItem("&Docker", Keys.Control | Keys.Shift | Keys.D, async (_, _) => await ShowDocker()));

        menu.Items.Add(fileMenu);
        menu.Items.Add(viewMenu);
        menu.Items.Add(toolsMenu);
        MainMenuStrip = menu;

        // status bar
        _status = new StatusStrip { SizingGrip = false };
        _statusPath = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusLang = new ToolStripStatusLabel("");
        _statusPos = new ToolStripStatusLabel("");
        _status.Items.AddRange(new ToolStripItem[] { _statusPath, _statusLang, _statusPos });

        // sidebar tree + editor tabs
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = System.Windows.Forms.BorderStyle.None,
            ShowLines = false,
            Font = new Font("Segoe UI", 9f),
        };
        _tree.BeforeExpand += Tree_BeforeExpand;
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is string path && File.Exists(path)) OpenFile(path); };

        _tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed,
            Padding = new Point(12, 4),
            StripBack = _theme.TabStripBack,
        };
        _tabs.SelectedIndexChanged += (_, _) => UpdateStatus();
        _tabs.MouseDown += Tabs_MouseDown;
        _tabs.DrawItem += Tabs_DrawItem;

        var tabContext = new ContextMenuStrip();
        tabContext.Items.Add("Close", null, (_, _) => { if (_tabs.SelectedTab != null) CloseTab(_tabs.SelectedTab); });
        tabContext.Items.Add("Close All", null, (_, _) => { foreach (var p in _tabs.TabPages.Cast<TabPage>().ToList()) if (!CloseTab(p)) break; });
        tabContext.Items.Add("Copy Path", null, (_, _) => { if (State(_tabs.SelectedTab)?.FilePath is string p) Clipboard.SetText(p); });
        _tabs.ContextMenuStrip = tabContext;

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 230,
            SplitterWidth = 4,
        };
        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_tabs);

        Controls.Add(_split);
        Controls.Add(_status);
        Controls.Add(menu);
        _split.BringToFront();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += MainForm_FormClosing;

        foreach (var arg in args)
        {
            if (arg is "--preview" or "--docker") continue;
            if (File.Exists(arg)) OpenFile(Path.GetFullPath(arg));
            else if (Directory.Exists(arg)) OpenFolder(Path.GetFullPath(arg));
        }

        if (args.Contains("--preview"))
            Shown += (_, _) => ToggleMarkdownPreview();
        if (args.Contains("--docker"))
            Shown += async (_, _) => await ShowDocker();

        Shown += (_, _) => ApplyNativeDarkMode(); // child handles exist by now
        ApplyTheme();
    }

    private static ToolStripMenuItem MenuItem(string text, Keys keys, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text, null, onClick);
        if (keys != Keys.None) item.ShortcutKeys = keys;
        return item;
    }

    private static TabState? State(TabPage? page) => page?.Tag as TabState;

    // ---------- opening ----------

    public void OpenFile(string path)
    {
        foreach (TabPage existing in _tabs.TabPages)
        {
            if (string.Equals(State(existing)?.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = existing;
                return;
            }
        }

        var info = new FileInfo(path);
        if (info.Length > OpenSizeLimit)
        {
            MessageBox.Show(this, $"File is {info.Length / (1024 * 1024)} MB - too large for codeviewer.", "codeviewer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (LooksBinary(path) &&
            MessageBox.Show(this, "This looks like a binary file. Open anyway?", "codeviewer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        string text;
        var state = new TabState { FilePath = path };
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
            state.Encoding = reader.CurrentEncoding;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open file:\n{ex.Message}", "codeviewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AddEditorTab(state, Path.GetFileName(path), text, path);
    }

    /// <summary>Opens generated content (docker logs, latex output) in a new editor tab.</summary>
    public void OpenTextTab(string title, string content, string? fakeNameForHighlighting)
    {
        var state = new TabState();
        AddEditorTab(state, title, content, fakeNameForHighlighting);
    }

    private void AddEditorTab(TabState state, string title, string text, string? highlightPath)
    {
        var editor = CreateEditor();
        state.Editor = editor;
        state.HighlightPath = highlightPath;
        editor.Text = text;
        editor.EmptyUndoBuffer();
        editor.SetSavePoint();
        state.Language = ApplyEditorTheme(editor, highlightPath);

        var page = new TabPage(title) { Tag = state, ToolTipText = state.FilePath ?? title };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 4, Panel2Collapsed = true, BackColor = _theme.EditorBack };
        split.Panel1.Controls.Add(editor);
        page.Controls.Add(split);
        WireEditor(editor, page);

        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
        editor.Focus();
        UpdateStatus();
    }

    public void OpenFolder(string path)
    {
        _tree.Nodes.Clear();
        var root = CreateDirNode(path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path);
        _tree.Nodes.Add(root);
        root.Expand();
        if (_split.Panel1Collapsed) { _split.Panel1Collapsed = false; _sidebarMenu.Checked = true; }
        Text = $"{root.Text} - codeviewer";
    }

    private Scintilla CreateEditor()
    {
        var editor = new Scintilla
        {
            Dock = DockStyle.Fill,
            BorderStyle = ScintillaNET.BorderStyle.None,
            IndentationGuides = IndentView.LookBoth,
            TabWidth = 4,
            UseTabs = false,
            CaretLineVisible = true,
            WrapMode = _wordWrapMenu.Checked ? WrapMode.Word : WrapMode.None,
            AllowDrop = true,
        };

        editor.Margins[0].Type = MarginType.Number;
        editor.Margins[1].Width = 4;

        editor.DragEnter += OnDragEnter;
        editor.DragDrop += OnDragDrop;
        return editor;
    }

    /// <summary>Applies the active theme's editor colors and syntax palette; re-runnable on theme toggle.</summary>
    private string ApplyEditorTheme(Scintilla editor, string? highlightPath)
    {
        editor.Styles[ScintillaNET.Style.Default].Font = "Cascadia Mono";
        editor.Styles[ScintillaNET.Style.Default].Size = 10;
        editor.Styles[ScintillaNET.Style.Default].ForeColor = _theme.EditorFore;
        editor.Styles[ScintillaNET.Style.Default].BackColor = _theme.EditorBack;
        editor.StyleClearAll();

        editor.Styles[ScintillaNET.Style.LineNumber].ForeColor = _theme.LineNumberFore;
        editor.Styles[ScintillaNET.Style.LineNumber].BackColor = _theme.LineNumberBack;
        editor.CaretLineBackColor = _theme.CaretLineBack;
        editor.CaretForeColor = _theme.CaretFore;
        editor.SetSelectionBackColor(true, _theme.SelectionBack);
        editor.Styles[ScintillaNET.Style.IndentGuide].ForeColor = _theme.IndentGuideFore;
        editor.Styles[ScintillaNET.Style.IndentGuide].BackColor = _theme.EditorBack;
        editor.SetFoldMarginColor(true, _theme.EditorBack);
        editor.SetFoldMarginHighlightColor(true, _theme.EditorBack);

        var language = Languages.Apply(editor, highlightPath, _theme);
        SetLineNumberWidth(editor);
        return language;
    }

    private void WireEditor(Scintilla editor, TabPage page)
    {
        editor.SavePointLeft += (_, _) =>
        {
            if (State(page) is { } s && !s.IsDirty) { s.IsDirty = true; page.Text = "● " + TabTitle(page, s); }
        };
        editor.SavePointReached += (_, _) =>
        {
            if (State(page) is { } s) { s.IsDirty = false; page.Text = TabTitle(page, s); }
        };
        editor.UpdateUI += (_, _) => { if (_tabs.SelectedTab == page) UpdatePosition(editor); };
        editor.TextChanged += (_, _) =>
        {
            SetLineNumberWidth(editor);
            State(page)?.PreviewTimer?.Stop();
            State(page)?.PreviewTimer?.Start();
        };

        // keep indentation on Enter; extra level after { or :
        editor.CharAdded += (_, e) =>
        {
            if (e.Char != '\n') return;
            int line = editor.CurrentLine;
            if (line == 0) return;
            string prev = editor.Lines[line - 1].Text;
            int i = 0;
            while (i < prev.Length && (prev[i] == ' ' || prev[i] == '\t')) i++;
            string indent = prev[..i];
            string trimmed = prev.TrimEnd();
            if (trimmed.EndsWith('{') || trimmed.EndsWith(':')) indent += new string(' ', editor.TabWidth);
            if (indent.Length > 0) editor.ReplaceSelection(indent);
        };
    }

    private static string TabTitle(TabPage page, TabState s) =>
        s.FilePath != null ? Path.GetFileName(s.FilePath) : page.Text.TrimStart('●', ' ');

    private static void SetLineNumberWidth(Scintilla editor)
    {
        int digits = Math.Max(3, editor.Lines.Count.ToString().Length);
        editor.Margins[0].Width = editor.TextWidth(ScintillaNET.Style.LineNumber, new string('9', digits)) + 8;
    }

    private static bool LooksBinary(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[512];
            int read = fs.Read(buf);
            for (int i = 0; i < read; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch { return false; }
    }

    // ---------- markdown preview ----------

    private void ToggleMarkdownPreview()
    {
        var page = _tabs.SelectedTab;
        if (page == null || State(page) is not { } state) return;
        if (!state.Language.StartsWith("Markdown", StringComparison.OrdinalIgnoreCase))
        {
            _statusPath.Text = "Markdown preview only works on .md files";
            return;
        }

        var split = (SplitContainer)page.Controls[0];
        if (state.Preview != null)
        {
            split.Panel2Collapsed = true;
            state.PreviewTimer?.Dispose();
            state.PreviewTimer = null;
            state.Preview.Dispose();
            state.Preview = null;
            return;
        }

        var browser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true,
            AllowWebBrowserDrop = false,
        };
        browser.Navigating += (_, e) =>
        {
            // open real links externally, keep preview rendering internal
            if (e.Url != null && (e.Url.Scheme == "http" || e.Url.Scheme == "https"))
            {
                e.Cancel = true;
                try { Process.Start(new ProcessStartInfo(e.Url.ToString()) { UseShellExecute = true }); } catch { }
            }
        };
        state.Preview = browser;
        split.Panel2.Controls.Add(browser);
        split.Panel2Collapsed = false;
        split.SplitterDistance = split.Width / 2;

        state.PreviewTimer = new System.Windows.Forms.Timer { Interval = 500 };
        state.PreviewTimer.Tick += (_, _) => { state.PreviewTimer!.Stop(); RenderMarkdown(state); };
        RenderMarkdown(state);
    }

    private void RenderMarkdown(TabState state)
    {
        if (state.Preview == null) return;

        state.Preview.DocumentText = BuildMarkdownHtmlDocument(state.Editor.Text, state.FilePath, _theme.IsDark);
    }

    private static string BuildMarkdownHtmlDocument(string markdown, string? title, bool dark)
    {
        string body;
        try { body = Markdown.ToHtml(markdown, MdPipeline); }
        catch (Exception ex) { body = "<pre>render error: " + WebUtility.HtmlEncode(ex.Message) + "</pre>"; }

        var safeTitle = WebUtility.HtmlEncode(title != null ? Path.GetFileName(title) : "Markdown output");
        var style = dark
            ? "body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;font-size:16px;line-height:1.65;color:#d4d4d4;max-width:900px;margin:0 auto;padding:32px 40px;background:#1e1e1e;}" +
              "h1,h2{border-bottom:1px solid #3c3c3c;padding-bottom:.3em;}h1{font-size:2em;}h2{font-size:1.45em;margin-top:1.8em;}h3{font-size:1.2em;margin-top:1.5em;}" +
              "p,ul,ol,blockquote,pre,table{margin:0 0 1em;}ul,ol{padding-left:1.6em;}li+li{margin-top:.25em;}" +
              "code{background:#2d2d2d;padding:.15em .35em;border-radius:4px;font-family:'Cascadia Mono',Consolas,monospace;font-size:.9em;}" +
              "pre{background:#252526;padding:16px;border-radius:6px;overflow-x:auto;}pre code{background:none;padding:0;font-size:.9em;}" +
              "blockquote{border-left:4px solid #3c3c3c;padding-left:16px;color:#9d9d9d;}" +
              "table{border-collapse:collapse;display:block;overflow-x:auto;}th,td{border:1px solid #3c3c3c;padding:6px 12px;}th{background:#252526;}" +
              "img{max-width:100%;height:auto;}a{color:#4fc1ff;}hr{border:0;border-top:1px solid #3c3c3c;margin:24px 0;}"
            : "body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;font-size:16px;line-height:1.65;color:#1f2328;max-width:900px;margin:0 auto;padding:32px 40px;background:#fff;}" +
              "h1,h2{border-bottom:1px solid #d8dee4;padding-bottom:.3em;}h1{font-size:2em;}h2{font-size:1.45em;margin-top:1.8em;}h3{font-size:1.2em;margin-top:1.5em;}" +
              "p,ul,ol,blockquote,pre,table{margin:0 0 1em;}ul,ol{padding-left:1.6em;}li+li{margin-top:.25em;}" +
              "code{background:#f0f1f2;padding:.15em .35em;border-radius:4px;font-family:'Cascadia Mono',Consolas,monospace;font-size:.9em;}" +
              "pre{background:#f6f8fa;padding:16px;border-radius:6px;overflow-x:auto;}pre code{background:none;padding:0;font-size:.9em;}" +
              "blockquote{border-left:4px solid #d8dee4;padding-left:16px;color:#59636e;}" +
              "table{border-collapse:collapse;display:block;overflow-x:auto;}th,td{border:1px solid #d8dee4;padding:6px 12px;}th{background:#f6f8fa;}" +
              "img{max-width:100%;height:auto;}a{color:#0969da;}hr{border:0;border-top:1px solid #d8dee4;margin:24px 0;}";
        return
            "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><style>" + style +
            "</style><title>" + safeTitle + "</title></head><body>" + body + "</body></html>";
    }

    private void CompileMarkdown()
    {
        var page = _tabs.SelectedTab;
        if (page == null || State(page) is not { } state || state.FilePath == null ||
            !state.Language.StartsWith("Markdown", StringComparison.OrdinalIgnoreCase))
        {
            _statusPath.Text = "Compile Markdown needs a saved .md file in the active tab";
            return;
        }
        if (state.IsDirty && !SaveTab(page)) return;

        var outputPath = Path.ChangeExtension(state.FilePath, ".html");
        try
        {
            // standalone output always uses the light stylesheet, regardless of app theme
            var html = BuildMarkdownHtmlDocument(state.Editor.Text, state.FilePath, dark: false);
            File.WriteAllText(outputPath, html, new UTF8Encoding(false));
            _statusPath.Text = $"Compiled Markdown -> {outputPath}";
            Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not compile Markdown:\n{ex.Message}", "codeviewer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------- latex ----------

    private async Task CompileLatex()
    {
        var page = _tabs.SelectedTab;
        if (page == null || State(page) is not { } state || state.FilePath == null ||
            !Path.GetExtension(state.FilePath).Equals(".tex", StringComparison.OrdinalIgnoreCase))
        {
            _statusPath.Text = "Compile LaTeX needs a saved .tex file in the active tab";
            return;
        }
        if (state.IsDirty && !SaveTab(page)) return;

        var dir = Path.GetDirectoryName(state.FilePath)!;
        var file = Path.GetFileName(state.FilePath);
        string[] compilers = { "pdflatex", "xelatex", "tectonic" };
        Exception? lastError = null;

        foreach (var compiler in compilers)
        {
            var args = compiler == "tectonic" ? $"\"{file}\"" : $"-interaction=nonstopmode -halt-on-error \"{file}\"";
            ProcessStartInfo psi = new(compiler, args)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            Process proc;
            try { proc = Process.Start(psi)!; }
            catch (Exception ex) { lastError = ex; continue; } // compiler not installed, try next

            _statusPath.Text = $"Compiling {file} with {compiler}...";
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            var done = await Task.Run(() => proc.WaitForExit(120000));
            if (!done) { try { proc.Kill(true); } catch { } _statusPath.Text = "LaTeX compile timed out"; return; }

            var output = await stdout + await stderr;
            if (proc.ExitCode == 0)
            {
                var pdf = Path.Combine(dir, Path.ChangeExtension(file, ".pdf"));
                _statusPath.Text = $"Compiled OK -> {pdf}";
                if (File.Exists(pdf))
                    try { Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true }); } catch { }
            }
            else
            {
                _statusPath.Text = $"{compiler} failed (exit {proc.ExitCode}) - output opened in tab";
                OpenTextTab($"latex output: {file}", output, null);
            }
            return;
        }

        MessageBox.Show(this,
            "No LaTeX compiler found on PATH (tried pdflatex, xelatex, tectonic).\nInstall MiKTeX (miktex.org) or TeX Live.\n\n" + lastError?.Message,
            "codeviewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ---------- docker ----------

    private async Task ShowDocker()
    {
        if (_dockerPage == null || !_tabs.TabPages.Contains(_dockerPage))
        {
            var panel = new DockerPanel(OpenTextTab);
            panel.ApplyTheme(_theme);
            _dockerPage = new TabPage("Docker");
            _dockerPage.Controls.Add(panel);
            _tabs.TabPages.Add(_dockerPage);
            _tabs.SelectedTab = _dockerPage;
            await panel.RefreshAsync();
            panel.ApplyNativeTheme(_theme.IsDark);
        }
        else
        {
            _tabs.SelectedTab = _dockerPage;
            await ((DockerPanel)_dockerPage.Controls[0]).RefreshAsync();
        }
    }

    // ---------- saving / closing ----------

    private void SaveCurrent() { if (_tabs.SelectedTab != null) SaveTab(_tabs.SelectedTab); }
    private void SaveCurrentAs() { if (_tabs.SelectedTab != null) SaveTabAs(_tabs.SelectedTab); }

    private bool SaveTab(TabPage page)
    {
        if (State(page) is not { } state) return false;
        if (state.FilePath == null) return SaveTabAs(page);

        try
        {
            File.WriteAllText(state.FilePath, state.Editor.Text, state.Encoding);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save:\n{ex.Message}", "codeviewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        state.Editor.SetSavePoint();
        _statusPath.Text = $"Saved {state.FilePath}";
        return true;
    }

    private bool SaveTabAs(TabPage page)
    {
        if (State(page) is not { } state) return false;
        using var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileName(state.FilePath ?? "untitled.txt"),
            InitialDirectory = state.FilePath != null ? Path.GetDirectoryName(state.FilePath) : null,
            Filter = "All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        state.FilePath = dlg.FileName;
        state.HighlightPath = dlg.FileName;
        page.ToolTipText = dlg.FileName;
        state.Language = Languages.Apply(state.Editor, dlg.FileName, _theme);
        if (!SaveTab(page)) return false;
        page.Text = Path.GetFileName(dlg.FileName);
        UpdateStatus();
        return true;
    }

    private bool CloseTab(TabPage page)
    {
        if (State(page) is { IsDirty: true } state)
        {
            var name = Path.GetFileName(state.FilePath ?? "untitled");
            var result = MessageBox.Show(this, $"Save changes to {name}?", "codeviewer",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes && !SaveTab(page)) return false;
        }
        State(page)?.PreviewTimer?.Dispose();
        if (page == _dockerPage) _dockerPage = null;
        _tabs.TabPages.Remove(page);
        page.Dispose();
        UpdateStatus();
        return true;
    }

    private void Tabs_MouseDown(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabs.TabCount; i++)
        {
            if (!_tabs.GetTabRect(i).Contains(e.Location)) continue;
            if (e.Button == MouseButtons.Middle) CloseTab(_tabs.TabPages[i]);
            else if (e.Button == MouseButtons.Right) _tabs.SelectedIndex = i;
            return;
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var dirty = _tabs.TabPages.Cast<TabPage>().Where(p => State(p) is { IsDirty: true }).ToList();
        if (dirty.Count == 0) return;

        var result = MessageBox.Show(this, $"{dirty.Count} file(s) have unsaved changes. Save before exiting?",
            "codeviewer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) { e.Cancel = true; return; }
        if (result == DialogResult.Yes)
            foreach (var page in dirty)
                if (!SaveTab(page)) { e.Cancel = true; return; }
    }

    // ---------- folder tree ----------

    private static TreeNode CreateDirNode(string dir, string? label = null)
    {
        var node = new TreeNode(label ?? Path.GetFileName(dir)) { Tag = dir };
        node.Nodes.Add(new TreeNode("...") { Tag = "placeholder" });
        return node;
    }

    private void Tree_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node!;
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag as string != "placeholder") return;
        node.Nodes.Clear();
        if (node.Tag is not string dir) return;

        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (name is "node_modules" or ".git" or "bin" or "obj" or "__pycache__" or ".next" or ".venv" or "venv") continue;
                node.Nodes.Add(CreateDirNode(sub));
            }
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                node.Nodes.Add(new TreeNode(Path.GetFileName(file)) { Tag = file });
        }
        catch (UnauthorizedAccessException) { }
    }

    // ---------- status / drag-drop / dialogs ----------

    private void UpdateStatus()
    {
        var page = _tabs.SelectedTab;
        if (page == null || State(page) is not { } state)
        {
            _statusPath.Text = page == _dockerPage && page != null ? "Docker" : "Ready";
            _statusLang.Text = "";
            _statusPos.Text = "";
            return;
        }
        _statusPath.Text = state.FilePath ?? page.Text;
        _statusLang.Text = state.Language;
        UpdatePosition(state.Editor);
    }

    private void UpdatePosition(Scintilla editor)
    {
        _statusPos.Text = $"Ln {editor.CurrentLine + 1}, Col {editor.GetColumn(editor.CurrentPosition) + 1}";
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) OpenFolder(path);
            else if (File.Exists(path)) OpenFile(path);
        }
    }

    private void OpenFileDialogAction()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Filter = "All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            foreach (var file in dlg.FileNames)
                OpenFile(file);
    }

    private void OpenFolderDialogAction()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK)
            OpenFolder(dlg.SelectedPath);
    }

    // ---------- theming ----------

    private void ApplyTheme()
    {
        BackColor = _theme.PanelBack;
        ToolStripManager.Renderer = new ThemeRenderer(new ThemeColorTable(_theme), _theme);
        if (MainMenuStrip != null) MainMenuStrip.BackColor = _theme.MenuBack;
        // explicit colors: the professional renderer skips its gradient once BackColor
        // is non-default, and the ambient form BackColor already makes it non-default
        _status.BackColor = _theme.StatusBack;
        _status.ForeColor = _theme.StatusFore;

        _tree.BackColor = _theme.SidebarBack;
        _tree.ForeColor = _theme.SidebarFore;
        _split.BackColor = _theme.TabStripBack;
        _split.Panel1.BackColor = _theme.SidebarBack;
        _split.Panel2.BackColor = _theme.TabStripBack;
        _tabs.BackColor = _theme.TabStripBack;
        _tabs.StripBack = _theme.TabStripBack;

        foreach (TabPage page in _tabs.TabPages)
        {
            if (State(page) is { } state)
            {
                if (page.Controls.Count > 0 && page.Controls[0] is SplitContainer tabSplit)
                    tabSplit.BackColor = _theme.EditorBack;
                state.Language = ApplyEditorTheme(state.Editor, state.HighlightPath);
                RenderMarkdown(state);
            }
            else if (page == _dockerPage && page.Controls.Count > 0 && page.Controls[0] is DockerPanel dockerPanel)
            {
                dockerPanel.ApplyTheme(_theme);
            }
        }
        _tabs.Invalidate();
        UpdateStatus();

        ApplyNativeDarkMode();
    }

    private void Tabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var page = _tabs.TabPages[e.Index];
        bool selected = e.Index == _tabs.SelectedIndex;
        var back = selected ? _theme.TabActiveBack : _theme.TabInactiveBack;
        var fore = selected ? _theme.TabActiveFore : _theme.TabInactiveFore;

        using var backBrush = new SolidBrush(back);
        e.Graphics.FillRectangle(backBrush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, page.Text, _tabs.Font, e.Bounds, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>TabControl that repaints the native header strip and pane border, which ignore BackColor.</summary>
    private sealed class ThemedTabControl : TabControl
    {
        public Color StripBack = SystemColors.Control;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F) PaintChrome(); // WM_PAINT
        }

        private void PaintChrome()
        {
            if (!IsHandleCreated || IsDisposed) return;
            using var g = Graphics.FromHwnd(Handle);
            using var region = new Region(ClientRectangle);
            region.Exclude(DisplayRectangle);
            for (int i = 0; i < TabCount; i++) region.Exclude(GetTabRect(i));
            using var brush = new SolidBrush(StripBack);
            g.FillRegion(brush, region);
        }
    }

    // ---------- native dark chrome (best effort) ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Applies dark scrollbars/list chrome to a control's handle; safe to call anywhere, never throws.</summary>
    internal static void ApplyDarkScrollbars(IntPtr handle, bool dark)
    {
        try { SetWindowTheme(handle, dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyNativeDarkMode();
    }

    private void ApplyNativeDarkMode()
    {
        if (!IsHandleCreated) return;

        try
        {
            int useDark = _theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch { }

        if (_tree.IsHandleCreated) ApplyDarkScrollbars(_tree.Handle, _theme.IsDark);
        foreach (TabPage page in _tabs.TabPages)
        {
            if (State(page) is { } s && s.Editor.IsHandleCreated)
                ApplyDarkScrollbars(s.Editor.Handle, _theme.IsDark);
            else if (page == _dockerPage && page.Controls.Count > 0 && page.Controls[0] is DockerPanel dockerPanel)
                dockerPanel.ApplyNativeTheme(_theme.IsDark);
        }
    }
}
