using System.Diagnostics;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Markdig;
using TextMateSharp.Grammars;

namespace CodeViewer.CrossPlatform;

public sealed partial class MainWindow : Window
{
    private const long TextOpenSizeLimit = 50 * 1024 * 1024;
    private const long ImageOpenSizeLimit = 250 * 1024 * 1024;

    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().UseAdvancedExtensions().Build();

    private readonly Dictionary<TabItem, TabState> _states = [];
    private readonly string[] _startupArgs;
    private bool _darkMode;
    private bool _wordWrap;
    private bool _closingApproved;

    private sealed class TabState : IDisposable
    {
        public string? FilePath;
        public string Title = "Untitled";
        public string Language = "Plain text";
        public Encoding Encoding = new UTF8Encoding(false);
        public TextEditor? Editor;
        public Grid? EditorLayout;
        public ScrollViewer? Preview;
        public IDisposable? TextMate;
        public Bitmap? Bitmap;
        public TextBlock HeaderText = null!;
        public bool IsDirty;
        public bool IsReadOnly;

        public void Dispose()
        {
            TextMate?.Dispose();
            Bitmap?.Dispose();
        }
    }

    private enum SaveChoice { Save, Discard, Cancel }

    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        _startupArgs = args;
        (_darkMode, _wordWrap) = LoadSettings();

        DarkButton.IsChecked = _darkMode;
        DarkMenuItem.IsChecked = _darkMode;
        WrapButton.IsChecked = _wordWrap;
        WrapMenuItem.IsChecked = _wordWrap;
        ApplyTheme();

        OpenFileItem.Click += async (_, _) => await OpenFileDialogAsync();
        OpenFolderItem.Click += async (_, _) => await OpenFolderDialogAsync();
        SaveItem.Click += async (_, _) => await SaveCurrentAsync();
        SaveAsItem.Click += async (_, _) => await SaveCurrentAsAsync();
        CloseTabItem.Click += async (_, _) => await CloseSelectedTabAsync();
        ExitItem.Click += (_, _) => Close();
        WrapButton.Click += (_, _) => SetWordWrap(WrapButton.IsChecked == true);
        WrapMenuItem.Click += (_, _) => SetWordWrap(WrapMenuItem.IsChecked == true);
        DarkButton.Click += (_, _) => SetDarkMode(DarkButton.IsChecked == true);
        DarkMenuItem.Click += (_, _) => SetDarkMode(DarkMenuItem.IsChecked == true);
        SidebarMenuItem.Click += (_, _) => SetSidebarVisible(SidebarMenuItem.IsChecked == true);
        MarkdownPreviewItem.Click += (_, _) => ToggleMarkdownPreview();
        CompileMarkdownItem.Click += async (_, _) => await CompileMarkdownAsync();
        CompileLatexItem.Click += async (_, _) => await CompileLatexAsync();
        DockerItem.Click += async (_, _) => await ShowDockerAsync();
        Tabs.SelectionChanged += (_, _) => UpdateStatus();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        Closing += OnClosing;
        Opened += async (_, _) => await OpenStartupArgumentsAsync();
    }

    private async Task OpenStartupArgumentsAsync()
    {
        foreach (var arg in _startupArgs)
        {
            if (File.Exists(arg)) await OpenFileAsync(Path.GetFullPath(arg));
            else if (Directory.Exists(arg)) OpenFolder(Path.GetFullPath(arg));
        }
    }

    private void SetWordWrap(bool enabled)
    {
        _wordWrap = enabled;
        WrapButton.IsChecked = enabled;
        WrapMenuItem.IsChecked = enabled;
        foreach (var state in _states.Values)
            if (state.Editor != null)
                state.Editor.WordWrap = enabled;
        SaveSettings();
        StatusPath.Text = enabled ? "Word wrap on" : "Word wrap off";
    }

    private void SetDarkMode(bool enabled)
    {
        _darkMode = enabled;
        DarkButton.IsChecked = enabled;
        DarkMenuItem.IsChecked = enabled;
        ApplyTheme();
        SaveSettings();
    }

    private void ApplyTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = _darkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (var state in _states.Values)
            if (state.Editor != null)
                ApplySyntaxHighlighting(state);
    }

    private void SetSidebarVisible(bool visible)
    {
        SidebarMenuItem.IsChecked = visible;
        Sidebar.IsVisible = visible;
        Workspace.ColumnDefinitions[0].Width = visible ? new GridLength(230) : new GridLength(0);
        Workspace.ColumnDefinitions[1].Width = visible ? new GridLength(4) : new GridLength(0);
    }

    private async Task OpenFileDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open files",
            AllowMultiple = true,
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path)
                await OpenFileAsync(path);
    }

    private async Task OpenFolderDialogAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open folder",
            AllowMultiple = false,
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            OpenFolder(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items == null) return;
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path == null) continue;
            if (Directory.Exists(path)) OpenFolder(path);
            else if (File.Exists(path)) await OpenFileAsync(path);
        }
    }

    private async Task OpenFileAsync(string path)
    {
        var existing = _states.FirstOrDefault(pair =>
            string.Equals(pair.Value.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing.Key != null)
        {
            Tabs.SelectedItem = existing.Key;
            return;
        }

        var info = new FileInfo(path);
        bool image = IsImagePath(path);
        long limit = image ? ImageOpenSizeLimit : TextOpenSizeLimit;
        if (info.Length > limit)
        {
            await ShowMessageAsync("File too large",
                $"{Path.GetFileName(path)} is {info.Length / (1024 * 1024)} MB. The limit is {limit / (1024 * 1024)} MB.");
            return;
        }

        if (image)
            await OpenImageAsync(path);
        else
            await OpenTextFileAsync(path);
    }

    private async Task OpenTextFileAsync(string path)
    {
        string text;
        Encoding encoding;
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false), true);
            text = await reader.ReadToEndAsync();
            encoding = reader.CurrentEncoding;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not open file", ex.Message);
            return;
        }

        var state = new TabState
        {
            FilePath = path,
            Title = Path.GetFileName(path),
            Encoding = encoding,
            Language = LanguageName(path),
        };
        AddEditorTab(state, text, path);
    }

    private void AddEditorTab(TabState state, string text, string? highlightingPath)
    {
        var editor = new TextEditor
        {
            Text = text,
            ShowLineNumbers = true,
            WordWrap = _wordWrap,
            FontFamily = new FontFamily("Cascadia Code, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = 14,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var preview = new ScrollViewer { IsVisible = false, Padding = new Thickness(24) };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,0") };
        Grid.SetColumn(editor, 0);
        Grid.SetColumn(preview, 1);
        layout.Children.Add(editor);
        layout.Children.Add(preview);

        state.Editor = editor;
        state.EditorLayout = layout;
        state.Preview = preview;
        var tab = CreateTab(state, layout);
        ApplySyntaxHighlighting(state, highlightingPath);

        editor.TextChanged += (_, _) =>
        {
            if (!state.IsDirty)
            {
                state.IsDirty = true;
                RefreshTabHeader(state);
            }
            if (preview.IsVisible) RenderMarkdownPreview(state);
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (Tabs.SelectedItem == tab) UpdateStatus();
        };
        editor.Focus();
    }

    private async Task OpenImageAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 100_000_000)
            {
                bitmap.Dispose();
                throw new InvalidDataException("Image dimensions exceed the 100-megapixel safety limit.");
            }

            var state = new TabState
            {
                FilePath = path,
                Title = Path.GetFileName(path),
                Language = $"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} image",
                IsReadOnly = true,
                Bitmap = bitmap,
            };
            var image = new Avalonia.Controls.Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var scroller = new ScrollViewer
            {
                Content = image,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            double zoom = 1;
            bool fit = true;
            var zoomText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

            void ApplyZoom()
            {
                if (fit)
                {
                    image.Width = double.NaN;
                    image.Height = double.NaN;
                    image.Stretch = Stretch.Uniform;
                    image.HorizontalAlignment = HorizontalAlignment.Stretch;
                    image.VerticalAlignment = VerticalAlignment.Stretch;
                    zoomText.Text = "Fit";
                }
                else
                {
                    image.Stretch = Stretch.Fill;
                    image.HorizontalAlignment = HorizontalAlignment.Center;
                    image.VerticalAlignment = VerticalAlignment.Center;
                    image.Width = bitmap.PixelSize.Width * zoom;
                    image.Height = bitmap.PixelSize.Height * zoom;
                    zoomText.Text = $"{Math.Round(zoom * 100)}%";
                }
                UpdateStatus();
            }

            var zoomOut = new Button { Content = "−" };
            var fitButton = new Button { Content = "Fit" };
            var actual = new Button { Content = "100%" };
            var zoomIn = new Button { Content = "+" };
            ToolTip.SetTip(zoomOut, "Zoom out");
            ToolTip.SetTip(fitButton, "Fit to window");
            ToolTip.SetTip(actual, "Actual size");
            ToolTip.SetTip(zoomIn, "Zoom in");
            zoomOut.Click += (_, _) => { fit = false; zoom = Math.Max(.05, zoom / 1.25); ApplyZoom(); };
            fitButton.Click += (_, _) => { fit = true; ApplyZoom(); };
            actual.Click += (_, _) => { fit = false; zoom = 1; ApplyZoom(); };
            zoomIn.Click += (_, _) => { fit = false; zoom = Math.Min(32, zoom * 1.25); ApplyZoom(); };
            scroller.PointerWheelChanged += (_, e) =>
            {
                fit = false;
                zoom = Math.Clamp(zoom * (e.Delta.Y > 0 ? 1.25 : .8), .05, 32);
                ApplyZoom();
                e.Handled = true;
            };

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(6, 4),
                Children = { zoomOut, fitButton, actual, zoomIn, zoomText },
            };
            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            Grid.SetRow(toolbar, 0);
            Grid.SetRow(scroller, 1);
            content.Children.Add(toolbar);
            content.Children.Add(scroller);
            CreateTab(state, content);
            ApplyZoom();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not open image", ex.Message);
        }
    }

    private TabItem CreateTab(TabState state, Control content)
    {
        var title = new TextBlock { Text = state.Title, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "×",
            Padding = new Thickness(5, 0),
            MinWidth = 22,
            Background = Brushes.Transparent,
        };
        ToolTip.SetTip(close, "Close tab");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { title, close } };
        var tab = new TabItem { Header = header, Content = content };
        state.HeaderText = title;
        close.Click += async (_, e) => { e.Handled = true; await CloseTabAsync(tab); };
        _states[tab] = state;
        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        UpdateStatus();
        return tab;
    }

    private void ApplySyntaxHighlighting(TabState state, string? path = null)
    {
        if (state.Editor == null) return;
        state.TextMate?.Dispose();
        try
        {
            var options = new RegistryOptions(_darkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
            var installation = state.Editor.InstallTextMate(options);
            state.TextMate = installation;
            var extension = Path.GetExtension(path ?? state.FilePath ?? "");
            var language = options.GetLanguageByExtension(extension);
            if (language != null)
                installation.SetGrammar(options.GetScopeByLanguageId(language.Id));
        }
        catch
        {
            state.TextMate = null;
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (Tabs.SelectedItem is TabItem tab) await SaveTabAsync(tab);
    }

    private async Task SaveCurrentAsAsync()
    {
        if (Tabs.SelectedItem is TabItem tab && _states.TryGetValue(tab, out var state))
            await SaveTabAsAsync(tab, state);
    }

    private async Task<bool> SaveTabAsync(TabItem tab)
    {
        if (!_states.TryGetValue(tab, out var state) || state.Editor == null || state.IsReadOnly)
        {
            StatusPath.Text = "This tab is read-only";
            return false;
        }
        if (state.FilePath == null) return await SaveTabAsAsync(tab, state);
        try
        {
            await File.WriteAllTextAsync(state.FilePath, state.Editor.Text, state.Encoding);
            state.IsDirty = false;
            RefreshTabHeader(state);
            StatusPath.Text = $"Saved {state.FilePath}";
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not save", ex.Message);
            return false;
        }
    }

    private async Task<bool> SaveTabAsAsync(TabItem tab, TabState state)
    {
        if (state.Editor == null || state.IsReadOnly) return false;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save file",
            SuggestedFileName = state.FilePath == null ? state.Title : Path.GetFileName(state.FilePath),
        });
        if (file?.TryGetLocalPath() is not { } path) return false;
        state.FilePath = path;
        state.Title = Path.GetFileName(path);
        state.Language = LanguageName(path);
        ApplySyntaxHighlighting(state);
        return await SaveTabAsync(tab);
    }

    private async Task CloseSelectedTabAsync()
    {
        if (Tabs.SelectedItem is TabItem tab) await CloseTabAsync(tab);
    }

    private async Task<bool> CloseTabAsync(TabItem tab)
    {
        if (!_states.TryGetValue(tab, out var state))
        {
            Tabs.Items.Remove(tab);
            return true;
        }
        if (state.IsDirty)
        {
            var choice = await AskSaveAsync(state.Title);
            if (choice == SaveChoice.Cancel) return false;
            if (choice == SaveChoice.Save && !await SaveTabAsync(tab)) return false;
        }
        _states.Remove(tab);
        Tabs.Items.Remove(tab);
        state.Dispose();
        UpdateStatus();
        return true;
    }

    private void ToggleMarkdownPreview()
    {
        if (Tabs.SelectedItem is not TabItem tab || !_states.TryGetValue(tab, out var state) ||
            state.Editor == null || state.EditorLayout == null || state.Preview == null ||
            !IsMarkdown(state.FilePath))
        {
            StatusPath.Text = "Markdown preview requires an active .md file";
            return;
        }
        state.Preview.IsVisible = !state.Preview.IsVisible;
        state.EditorLayout.ColumnDefinitions[1].Width = state.Preview.IsVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (state.Preview.IsVisible) RenderMarkdownPreview(state);
    }

    private void RenderMarkdownPreview(TabState state)
    {
        if (state.Editor == null || state.Preview == null) return;
        var panel = new StackPanel { Spacing = 10, MaxWidth = 900 };
        bool inCode = false;
        var code = new StringBuilder();
        foreach (var raw in state.Editor.Text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    panel.Children.Add(new Border
                    {
                        Background = _darkMode ? new SolidColorBrush(Color.Parse("#252526")) : new SolidColorBrush(Color.Parse("#F0F1F2")),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(12),
                        Child = new SelectableTextBlock
                        {
                            Text = code.ToString().TrimEnd(),
                            FontFamily = new FontFamily("Cascadia Code, Menlo, monospace"),
                            TextWrapping = TextWrapping.Wrap,
                        },
                    });
                    code.Clear();
                }
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.AppendLine(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;

            int heading = line.TakeWhile(c => c == '#').Count();
            string display = heading > 0 && heading <= 6 ? line[heading..].TrimStart() : line;
            if (display.StartsWith("- ") || display.StartsWith("* ")) display = "• " + display[2..];
            panel.Children.Add(new SelectableTextBlock
            {
                Text = display,
                TextWrapping = TextWrapping.Wrap,
                FontSize = heading switch { 1 => 28, 2 => 23, 3 => 19, _ => 16 },
                FontWeight = heading is > 0 and <= 3 ? FontWeight.Bold : FontWeight.Normal,
            });
        }
        state.Preview.Content = panel;
    }

    private async Task CompileMarkdownAsync()
    {
        if (Tabs.SelectedItem is not TabItem tab || !_states.TryGetValue(tab, out var state) ||
            state.Editor == null || state.FilePath == null || !IsMarkdown(state.FilePath))
        {
            StatusPath.Text = "Compile Markdown requires a saved .md file";
            return;
        }
        if (state.IsDirty && !await SaveTabAsync(tab)) return;
        try
        {
            var output = Path.ChangeExtension(state.FilePath, ".html");
            var title = WebUtility.HtmlEncode(Path.GetFileName(state.FilePath));
            var body = Markdown.ToHtml(state.Editor.Text, MarkdownPipeline);
            var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{title}</title>" +
                       "<style>body{font-family:system-ui,sans-serif;line-height:1.6;max-width:900px;margin:32px auto;padding:0 24px}pre,code{font-family:monospace}pre{padding:16px;background:#f4f4f4;overflow:auto}img{max-width:100%}</style>" +
                       $"</head><body>{body}</body></html>";
            await File.WriteAllTextAsync(output, html, new UTF8Encoding(false));
            OpenExternally(output);
            StatusPath.Text = $"Compiled Markdown → {output}";
        }
        catch (Exception ex) { await ShowMessageAsync("Markdown compile failed", ex.Message); }
    }

    private async Task CompileLatexAsync()
    {
        if (Tabs.SelectedItem is not TabItem tab || !_states.TryGetValue(tab, out var state) ||
            state.Editor == null || state.FilePath == null ||
            !Path.GetExtension(state.FilePath).Equals(".tex", StringComparison.OrdinalIgnoreCase))
        {
            StatusPath.Text = "Compile LaTeX requires a saved .tex file";
            return;
        }
        if (state.IsDirty && !await SaveTabAsync(tab)) return;

        var directory = Path.GetDirectoryName(state.FilePath)!;
        var file = Path.GetFileName(state.FilePath);
        foreach (var compiler in new[] { "pdflatex", "xelatex", "tectonic" })
        {
            var args = compiler == "tectonic" ? new[] { file } : new[] { "-interaction=nonstopmode", "-halt-on-error", file };
            var result = await RunProcessAsync(compiler, args, directory, 120_000);
            if (result.Missing) continue;
            if (result.ExitCode == 0)
            {
                var pdf = Path.Combine(directory, Path.ChangeExtension(file, ".pdf"));
                if (File.Exists(pdf)) OpenExternally(pdf);
                StatusPath.Text = $"Compiled LaTeX → {pdf}";
            }
            else
            {
                OpenGeneratedTextTab($"LaTeX output: {file}", result.Output, null);
                StatusPath.Text = $"{compiler} failed (exit {result.ExitCode})";
            }
            return;
        }
        await ShowMessageAsync("No LaTeX compiler found", "Install pdflatex, xelatex, or tectonic and ensure it is available on PATH.");
    }

    private async Task ShowDockerAsync()
    {
        var existing = _states.FirstOrDefault(x => x.Value.Title == "Docker");
        if (existing.Key != null) { Tabs.SelectedItem = existing.Key; return; }

        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code, Menlo, DejaVu Sans Mono, monospace"),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(output, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(output, ScrollBarVisibility.Auto);
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(6, 4) };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(refresh, 0); Grid.SetRow(output, 1);
        content.Children.Add(refresh); content.Children.Add(output);
        var state = new TabState { Title = "Docker", Language = "Docker", IsReadOnly = true };
        CreateTab(state, content);

        async Task RefreshAsync()
        {
            output.Text = "Loading Docker…";
            var containers = await RunProcessAsync("docker", ["ps", "-a"], null, 20_000);
            if (containers.Missing)
            {
                output.Text = "Docker is not installed or is not available on PATH.";
                return;
            }
            var images = await RunProcessAsync("docker", ["images"], null, 20_000);
            output.Text = "CONTAINERS\n" + containers.Output.TrimEnd() + "\n\nIMAGES\n" + images.Output.TrimEnd();
        }
        refresh.Click += async (_, _) => await RefreshAsync();
        await RefreshAsync();
    }

    private void OpenGeneratedTextTab(string title, string content, string? highlightPath)
    {
        var state = new TabState { Title = title, Language = LanguageName(highlightPath), IsReadOnly = true };
        AddEditorTab(state, content, highlightPath);
        if (state.Editor != null) state.Editor.IsReadOnly = true;
    }

    private void OpenFolder(string path)
    {
        FolderTree.Items.Clear();
        FolderTree.Items.Add(CreateDirectoryNode(path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))));
        SetSidebarVisible(true);
        Title = $"{Path.GetFileName(path)} — codeviewer";
    }

    private TreeViewItem CreateDirectoryNode(string path, string? title = null)
    {
        var node = new TreeViewItem { Header = string.IsNullOrEmpty(title) ? path : title, Tag = path };
        node.Items.Add(new TreeViewItem { Header = "Loading…" });
        bool loaded = false;
        node.Expanded += (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            node.Items.Clear();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(directory);
                    if (name is "node_modules" or ".git" or "bin" or "obj" or "__pycache__" or ".next" or ".venv" or "venv") continue;
                    node.Items.Add(CreateDirectoryNode(directory, name));
                }
                foreach (var file in Directory.EnumerateFiles(path).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var child = new TreeViewItem { Header = Path.GetFileName(file), Tag = file };
                    child.DoubleTapped += async (_, e) => { e.Handled = true; await OpenFileAsync(file); };
                    node.Items.Add(child);
                }
            }
            catch (UnauthorizedAccessException) { }
        };
        return node;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingApproved) return;
        var dirty = _states.Where(x => x.Value.IsDirty).Select(x => x.Key).ToList();
        if (dirty.Count == 0) return;
        e.Cancel = true;
        foreach (var tab in dirty)
        {
            Tabs.SelectedItem = tab;
            var state = _states[tab];
            var choice = await AskSaveAsync(state.Title);
            if (choice == SaveChoice.Cancel) return;
            if (choice == SaveChoice.Save && !await SaveTabAsync(tab)) return;
        }
        _closingApproved = true;
        Close();
    }

    private async Task<SaveChoice> AskSaveAsync(string title)
    {
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 430,
            Height = 165,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var save = new Button { Content = "Save", MinWidth = 84 };
        var discard = new Button { Content = "Don't save", MinWidth = 94 };
        var cancel = new Button { Content = "Cancel", MinWidth = 84 };
        save.Click += (_, _) => dialog.Close(SaveChoice.Save);
        discard.Click += (_, _) => dialog.Close(SaveChoice.Discard);
        cancel.Click += (_, _) => dialog.Close(SaveChoice.Cancel);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"Save changes to {title}?", FontSize = 16, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { save, discard, cancel } },
            },
        };
        return await dialog.ShowDialog<SaveChoice>(this);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var okay = new Button { Content = "OK", MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };
        okay.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, okay },
        };
        await dialog.ShowDialog(this);
    }

    private void UpdateStatus()
    {
        if (Tabs.SelectedItem is not TabItem tab || !_states.TryGetValue(tab, out var state))
        {
            StatusPath.Text = "Ready";
            StatusLanguage.Text = "";
            StatusPosition.Text = "";
            return;
        }
        StatusPath.Text = state.FilePath ?? state.Title;
        StatusLanguage.Text = state.Language;
        if (state.Bitmap != null)
            StatusPosition.Text = $"{state.Bitmap.PixelSize.Width} × {state.Bitmap.PixelSize.Height} px";
        else if (state.Editor != null)
            StatusPosition.Text = $"Ln {state.Editor.TextArea.Caret.Line}, Col {state.Editor.TextArea.Caret.Column}";
        else
            StatusPosition.Text = "";
    }

    private static void RefreshTabHeader(TabState state) =>
        state.HeaderText.Text = (state.IsDirty ? "● " : "") + state.Title;

    private static async Task<(int ExitCode, string Output, bool Missing)> RunProcessAsync(
        string executable, IEnumerable<string> arguments, string? workingDirectory, int timeoutMs)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(timeoutMs);
            try { await process.WaitForExitAsync(cancellation.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (-1, "Command timed out.", false);
            }
            var output = await stdout;
            var error = await stderr;
            return (process.ExitCode, output.Length > 0 ? output : error, false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            return (-1, ex.Message, true);
        }
    }

    private static void OpenExternally(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private static bool IsMarkdown(string? path) =>
        path != null && Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";

    private static bool IsImagePath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".gif" or ".bmp" or ".dib" or
        ".tif" or ".tiff" or ".ico" or ".webp" or ".avif" or ".heic" or ".heif" or
        ".dds" or ".jxr" or ".wdp" or ".hdp" or ".dng" or ".cr2" or ".cr3" or
        ".nef" or ".arw" or ".rw2" or ".orf" or ".raf";

    private static string LanguageName(string? path)
    {
        if (path == null) return "Plain text";
        var file = Path.GetFileName(path);
        if (file.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) return "Dockerfile";
        if (file.Equals("Makefile", StringComparison.OrdinalIgnoreCase)) return "Makefile";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "C#", ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".ts" => "TypeScript", ".jsx" => "JavaScript JSX", ".tsx" => "TypeScript JSX",
            ".py" => "Python", ".java" => "Java", ".c" => "C", ".h" => "C/C++ Header",
            ".cpp" or ".hpp" or ".cc" => "C++", ".go" => "Go", ".rs" => "Rust",
            ".json" or ".jsonc" => "JSON", ".yml" or ".yaml" => "YAML",
            ".xml" or ".csproj" or ".xaml" or ".svg" => "XML",
            ".html" or ".htm" => "HTML", ".css" or ".scss" => "CSS", ".sql" => "SQL",
            ".sh" or ".bash" or ".zsh" => "Shell", ".ps1" => "PowerShell",
            ".md" or ".markdown" => "Markdown", ".tex" => "LaTeX", ".bib" => "BibTeX",
            ".toml" => "TOML", ".ini" or ".editorconfig" => "INI", ".env" => "Env",
            _ => "Plain text",
        };
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codeviewer", "settings.txt");

    private static (bool Dark, bool Wrap) LoadSettings()
    {
        try
        {
            var settings = File.ReadAllText(SettingsPath);
            return (!settings.Contains("theme=light", StringComparison.OrdinalIgnoreCase),
                settings.Contains("wrap=true", StringComparison.OrdinalIgnoreCase));
        }
        catch { return (true, false); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, $"theme={(_darkMode ? "dark" : "light")}\nwrap={_wordWrap.ToString().ToLowerInvariant()}\n");
        }
        catch { }
    }
}
