using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CodeViewer;

/// <summary>A lightweight, read-only image surface with zooming and mouse panning.</summary>
sealed class ImageViewer : Control
{
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 32f;

    private Image? _image;
    private float _zoom = 1f;
    private bool _fitToWindow = true;
    private PointF _pan;
    private Point _dragStart;
    private PointF _panStart;
    private bool _dragging;
    private Color _surfaceBack = Color.FromArgb(30, 30, 30);

    public event EventHandler? ViewChanged;

    public Image? Image => _image;
    public bool IsFitToWindow => _fitToWindow;
    public int ZoomPercent => (int)Math.Round(CurrentScale * 100f);

    public ImageViewer(Image image)
    {
        _image = image;
        Dock = DockStyle.Fill;
        TabStop = true;
        Cursor = Cursors.Default;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    public void ApplyTheme(Theme theme)
    {
        _surfaceBack = theme.EditorBack;
        BackColor = _surfaceBack;
        Invalidate();
    }

    public void Fit()
    {
        _fitToWindow = true;
        _pan = PointF.Empty;
        RaiseViewChanged();
    }

    public void ActualSize()
    {
        _fitToWindow = false;
        _zoom = 1f;
        _pan = PointF.Empty;
        RaiseViewChanged();
    }

    public void ZoomIn() => ZoomAt(new Point(ClientSize.Width / 2, ClientSize.Height / 2), CurrentScale * 1.25f);
    public void ZoomOut() => ZoomAt(new Point(ClientSize.Width / 2, ClientSize.Height / 2), CurrentScale / 1.25f);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        var destination = ImageBounds(CurrentScale);
        if (HasTransparency(_image)) PaintTransparencyGrid(e.Graphics, destination);

        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.InterpolationMode = CurrentScale >= 4f
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, destination);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_fitToWindow) RaiseViewChanged();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float factor = e.Delta > 0 ? 1.25f : 0.8f;
        ZoomAt(e.Location, CurrentScale * factor);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = e.Location;
        _panStart = _pan;
        Cursor = Cursors.Hand;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _fitToWindow = false;
        _pan = new PointF(_panStart.X + e.X - _dragStart.X, _panStart.Y + e.Y - _dragStart.Y);
        RaiseViewChanged();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Cursor = Cursors.Default;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_fitToWindow) ActualSize(); else Fit();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key is Keys.Add or Keys.Oemplus or Keys.Subtract or Keys.OemMinus or Keys.D0 or Keys.NumPad0 or Keys.F
            || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Add:
            case Keys.Oemplus:
                ZoomIn();
                e.Handled = true;
                break;
            case Keys.Subtract:
            case Keys.OemMinus:
                ZoomOut();
                e.Handled = true;
                break;
            case Keys.D0:
            case Keys.NumPad0:
                ActualSize();
                e.Handled = true;
                break;
            case Keys.F:
                Fit();
                e.Handled = true;
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }
        base.Dispose(disposing);
    }

    private float CurrentScale => _fitToWindow ? FitScale() : _zoom;

    private float FitScale()
    {
        if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return 1f;
        return Math.Min((float)ClientSize.Width / _image.Width, (float)ClientSize.Height / _image.Height);
    }

    private RectangleF ImageBounds(float scale)
    {
        if (_image == null) return RectangleF.Empty;
        float width = _image.Width * scale;
        float height = _image.Height * scale;
        return new RectangleF(
            (ClientSize.Width - width) / 2f + _pan.X,
            (ClientSize.Height - height) / 2f + _pan.Y,
            width,
            height);
    }

    private void ZoomAt(Point anchor, float newScale)
    {
        if (_image == null) return;
        float oldScale = CurrentScale;
        newScale = Math.Clamp(newScale, MinZoom, MaxZoom);
        var oldBounds = ImageBounds(oldScale);
        float imageX = (anchor.X - oldBounds.X) / oldScale;
        float imageY = (anchor.Y - oldBounds.Y) / oldScale;

        _fitToWindow = false;
        _zoom = newScale;
        float naturalX = (ClientSize.Width - _image.Width * newScale) / 2f;
        float naturalY = (ClientSize.Height - _image.Height * newScale) / 2f;
        _pan = new PointF(
            anchor.X - imageX * newScale - naturalX,
            anchor.Y - imageY * newScale - naturalY);
        RaiseViewChanged();
    }

    private void RaiseViewChanged()
    {
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool HasTransparency(Image image) =>
        Image.IsAlphaPixelFormat(image.PixelFormat) || (image.Flags & (int)ImageFlags.HasAlpha) != 0;

    private void PaintTransparencyGrid(Graphics graphics, RectangleF bounds)
    {
        var clipped = Rectangle.Ceiling(RectangleF.Intersect(bounds, ClientRectangle));
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        const int cell = 12;
        var light = ControlPaint.Light(_surfaceBack, 0.12f);
        var dark = ControlPaint.Dark(_surfaceBack, 0.08f);
        using var lightBrush = new SolidBrush(light);
        using var darkBrush = new SolidBrush(dark);
        var state = graphics.Save();
        graphics.SetClip(clipped);
        for (int y = (int)bounds.Top; y < bounds.Bottom; y += cell)
            for (int x = (int)bounds.Left; x < bounds.Right; x += cell)
                graphics.FillRectangle((((x - (int)bounds.Left) / cell + (y - (int)bounds.Top) / cell) & 1) == 0 ? lightBrush : darkBrush,
                    x, y, cell, cell);
        graphics.Restore(state);
    }
}
