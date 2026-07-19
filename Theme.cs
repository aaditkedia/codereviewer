namespace CodeViewer;

/// <summary>Named UI surface colors for the app; a dark (default) and light instance.</summary>
sealed class Theme
{
    public bool IsDark;

    public Color EditorBack, EditorFore;
    public Color LineNumberFore, LineNumberBack;
    public Color CaretLineBack, CaretFore;
    public Color SelectionBack;
    public Color IndentGuideFore;
    public Color SidebarBack, SidebarFore;
    public Color TabStripBack, TabActiveBack, TabInactiveBack, TabActiveFore, TabInactiveFore;
    public Color MenuBack, MenuFore, MenuHoverBack, MenuBorder;
    public Color StatusBack, StatusFore;
    public Color PanelBack, PanelFore, HeaderBack, HeaderFore, ListBack, ListFore;

    public static readonly Theme Dark = new()
    {
        IsDark = true,
        EditorBack = Rgb(0x1E1E1E), EditorFore = Rgb(0xD4D4D4),
        LineNumberFore = Rgb(0x858585), LineNumberBack = Rgb(0x1E1E1E),
        CaretLineBack = Rgb(0x282828), CaretFore = Rgb(0xAEAFAD),
        SelectionBack = Rgb(0x264F78),
        IndentGuideFore = Rgb(0x404040),
        SidebarBack = Rgb(0x252526), SidebarFore = Rgb(0xCCCCCC),
        TabStripBack = Rgb(0x252526), TabActiveBack = Rgb(0x1E1E1E), TabInactiveBack = Rgb(0x2D2D2D),
        TabActiveFore = Rgb(0xFFFFFF), TabInactiveFore = Rgb(0x969696),
        MenuBack = Rgb(0x2D2D30), MenuFore = Rgb(0xCCCCCC), MenuHoverBack = Rgb(0x3E3E40), MenuBorder = Rgb(0x454545),
        StatusBack = Rgb(0x007ACC), StatusFore = Color.White,
        PanelBack = Rgb(0x1E1E1E), PanelFore = Rgb(0xD4D4D4), HeaderBack = Rgb(0x2D2D30), HeaderFore = Rgb(0xCCCCCC),
        ListBack = Rgb(0x252526), ListFore = Rgb(0xCCCCCC),
    };

    public static readonly Theme Light = new()
    {
        IsDark = false,
        EditorBack = Color.White, EditorFore = Color.Black,
        LineNumberFore = Rgb(0x8C8C8C), LineNumberBack = Rgb(0xF8F8F8),
        CaretLineBack = Rgb(0xF5F7FA), CaretFore = Color.Black,
        SelectionBack = Rgb(0xADD6FF),
        IndentGuideFore = Rgb(0xD3D3D3),
        SidebarBack = Rgb(0xFAFAFA), SidebarFore = Color.Black,
        TabStripBack = Rgb(0xF3F3F3), TabActiveBack = Color.White, TabInactiveBack = Rgb(0xECECEC),
        TabActiveFore = Rgb(0x333333), TabInactiveFore = Rgb(0x6E6E6E),
        MenuBack = Rgb(0xF0F0F0), MenuFore = Color.Black, MenuHoverBack = Rgb(0xD8D8D8), MenuBorder = Rgb(0xC0C0C0),
        StatusBack = Rgb(0x007ACC), StatusFore = Color.White,
        PanelBack = Color.White, PanelFore = Color.Black, HeaderBack = Rgb(0xF0F0F0), HeaderFore = Color.Black,
        ListBack = Color.White, ListFore = Color.Black,
    };

    private static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // ---------- persistence ----------

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codeviewer", "settings.txt");

    /// <summary>Loads the persisted theme choice, defaulting to Dark on any error or absence.</summary>
    public static Theme Load()
    {
        try
        {
            var text = File.ReadAllText(SettingsPath);
            return text.Contains("theme=light", StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        }
        catch { return Dark; }
    }

    /// <summary>Best-effort persistence; swallows IO errors.</summary>
    public static void Save(Theme theme)
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, theme.IsDark ? "theme=dark" : "theme=light");
        }
        catch { }
    }
}

sealed class ThemeColorTable : ProfessionalColorTable
{
    private readonly Theme _t;
    public ThemeColorTable(Theme theme) => _t = theme;

    public override Color MenuStripGradientBegin => _t.MenuBack;
    public override Color MenuStripGradientEnd => _t.MenuBack;
    public override Color ToolStripDropDownBackground => _t.MenuBack;
    public override Color ImageMarginGradientBegin => _t.MenuBack;
    public override Color ImageMarginGradientMiddle => _t.MenuBack;
    public override Color ImageMarginGradientEnd => _t.MenuBack;
    public override Color MenuItemSelected => _t.MenuHoverBack;
    public override Color MenuItemSelectedGradientBegin => _t.MenuHoverBack;
    public override Color MenuItemSelectedGradientEnd => _t.MenuHoverBack;
    public override Color MenuItemPressedGradientBegin => _t.MenuHoverBack;
    public override Color MenuItemPressedGradientEnd => _t.MenuHoverBack;
    public override Color MenuItemBorder => _t.MenuBorder;
    public override Color MenuBorder => _t.MenuBorder;
    public override Color SeparatorDark => _t.MenuBorder;
    public override Color SeparatorLight => _t.MenuBorder;
    public override Color StatusStripGradientBegin => _t.StatusBack;
    public override Color StatusStripGradientEnd => _t.StatusBack;
    public override Color ToolStripBorder => _t.MenuBorder;
    public override Color CheckBackground => _t.MenuHoverBack;
    public override Color CheckSelectedBackground => _t.MenuHoverBack;
}

sealed class ThemeRenderer : ToolStripProfessionalRenderer
{
    private readonly Theme _theme;

    public ThemeRenderer(ThemeColorTable table, Theme theme) : base(table)
    {
        _theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Owner is StatusStrip ? _theme.StatusFore : _theme.MenuFore;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.MenuFore;
        base.OnRenderArrow(e);
    }
}
