using System.Diagnostics;

namespace CodeViewer;

/// <summary>Read-mostly Docker view: containers and images via the docker CLI, with logs/inspect/start/stop.</summary>
public sealed class DockerPanel : UserControl
{
    private readonly ListView _containers;
    private readonly ListView _images;
    private readonly Label _status;
    private readonly Action<string, string, string?> _openTab; // (title, content, fakeFileNameForHighlighting)

    public DockerPanel(Action<string, string, string?> openTab)
    {
        _openTab = openTab;
        Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4, 4, 0, 0) };
        var refreshBtn = new Button { Text = "Refresh", AutoSize = true };
        refreshBtn.Click += async (_, _) => await RefreshAsync();
        _status = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
        toolbar.Controls.Add(refreshBtn);
        toolbar.Controls.Add(_status);

        _containers = MakeList(new[] { "ID", "Image", "Name", "Status", "Ports" }, new[] { 110, 200, 160, 180, 180 });
        _images = MakeList(new[] { "Repository", "Tag", "ID", "Size", "Created" }, new[] { 260, 120, 110, 100, 160 });

        var containersMenu = new ContextMenuStrip();
        containersMenu.Items.Add("Logs (last 500 lines)", null, async (_, _) => await ContainerAction("logs"));
        containersMenu.Items.Add("Inspect", null, async (_, _) => await ContainerAction("inspect"));
        containersMenu.Items.Add(new ToolStripSeparator());
        containersMenu.Items.Add("Start", null, async (_, _) => await ContainerAction("start"));
        containersMenu.Items.Add("Stop", null, async (_, _) => await ContainerAction("stop"));
        _containers.ContextMenuStrip = containersMenu;
        _containers.DoubleClick += async (_, _) => await ContainerAction("logs");

        var imagesMenu = new ContextMenuStrip();
        imagesMenu.Items.Add("Inspect", null, async (_, _) => await ImageInspect());
        _images.ContextMenuStrip = imagesMenu;
        _images.DoubleClick += async (_, _) => await ImageInspect();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 4 };
        split.Panel1.Controls.Add(_containers);
        split.Panel1.Controls.Add(Header("Containers"));
        split.Panel2.Controls.Add(_images);
        split.Panel2.Controls.Add(Header("Images"));

        Controls.Add(split);
        Controls.Add(toolbar);
        split.BringToFront();
    }

    public async Task RefreshAsync()
    {
        _status.Text = "Loading...";
        _containers.Items.Clear();
        _images.Items.Clear();

        var (ok, output) = await RunDocker("ps -a --no-trunc=false --format \"{{.ID}}\\t{{.Image}}\\t{{.Names}}\\t{{.Status}}\\t{{.Ports}}\"");
        if (!ok)
        {
            _status.Text = "Docker not available: " + FirstLine(output);
            return;
        }
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length >= 4)
                _containers.Items.Add(new ListViewItem(new[] { parts[0], parts[1], parts[2], parts[3], parts.Length > 4 ? parts[4] : "" }));
        }

        var (ok2, output2) = await RunDocker("images --format \"{{.Repository}}\\t{{.Tag}}\\t{{.ID}}\\t{{.Size}}\\t{{.CreatedSince}}\"");
        if (ok2)
            foreach (var line in output2.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length >= 5)
                    _images.Items.Add(new ListViewItem(parts));
            }

        _status.Text = $"{_containers.Items.Count} container(s), {_images.Items.Count} image(s)";
    }

    private async Task ContainerAction(string action)
    {
        if (_containers.SelectedItems.Count == 0) return;
        var id = _containers.SelectedItems[0].Text;
        var name = _containers.SelectedItems[0].SubItems[2].Text;

        switch (action)
        {
            case "logs":
            {
                _status.Text = $"Fetching logs for {name}...";
                var (ok, output) = await RunDocker($"logs --tail 500 {id}");
                if (ok) _openTab($"logs: {name}", output.Length > 0 ? output : "(no log output)", null);
                _status.Text = ok ? "" : "logs failed: " + FirstLine(output);
                break;
            }
            case "inspect":
            {
                var (ok, output) = await RunDocker($"inspect {id}");
                if (ok) _openTab($"inspect: {name}", output, "inspect.json");
                _status.Text = ok ? "" : "inspect failed: " + FirstLine(output);
                break;
            }
            case "start":
            case "stop":
            {
                _status.Text = $"{action} {name}...";
                var (ok, output) = await RunDocker($"{action} {id}");
                _status.Text = ok ? $"{action} ok" : $"{action} failed: " + FirstLine(output);
                await RefreshAsync();
                break;
            }
        }
    }

    private async Task ImageInspect()
    {
        if (_images.SelectedItems.Count == 0) return;
        var id = _images.SelectedItems[0].SubItems[2].Text;
        var repo = _images.SelectedItems[0].Text;
        var (ok, output) = await RunDocker($"inspect {id}");
        if (ok) _openTab($"image: {repo}", output, "inspect.json");
        _status.Text = ok ? "" : "inspect failed: " + FirstLine(output);
    }

    private static async Task<(bool ok, string output)> RunDocker(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            var done = await Task.Run(() => proc.WaitForExit(20000));
            if (!done) { try { proc.Kill(true); } catch { } return (false, "docker command timed out"); }
            var output = await stdout;
            var err = await stderr;
            return proc.ExitCode == 0 ? (true, output.Length > 0 ? output : err) : (false, err.Length > 0 ? err : output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message); // docker CLI not installed / not on PATH
        }
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return (i >= 0 ? s[..i] : s).Trim();
    }

    private static ListView MakeList(string[] columns, int[] widths)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
        };
        for (int i = 0; i < columns.Length; i++)
            list.Columns.Add(columns[i], widths[i]);
        return list;
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 22,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Padding = new Padding(6, 4, 0, 0),
        BackColor = Color.FromArgb(240, 240, 240),
    };
}
