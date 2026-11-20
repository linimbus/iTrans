using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Itrans.Capture;

public static class ScreenCapturer
{
    public static Task<Bitmap?> CaptureAsync()
    {
        var completion = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Rectangle virtualBounds;
        Bitmap snapshot;
        try
        {
            virtualBounds = SystemInformation.VirtualScreen;
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                throw new InvalidOperationException("无法获取屏幕尺寸。");

            snapshot = new Bitmap(virtualBounds.Width, virtualBounds.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(snapshot);
            graphics.CopyFromScreen(virtualBounds.Location, Point.Empty, virtualBounds.Size, CopyPixelOperation.SourceCopy);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return completion.Task;
        }

        var overlay = new ScreenCaptureOverlay(snapshot, virtualBounds, result => completion.TrySetResult(result));
        overlay.Show();
        overlay.Activate();
        overlay.Focus();
        return completion.Task;
    }
}

internal sealed class ScreenCaptureOverlay : Form
{
    private readonly Bitmap _snapshot;
    private readonly Rectangle _virtualBounds;
    private readonly Action<Bitmap?> _onDone;

    private Point _anchor;
    private Rectangle _selection;
    private bool _dragging;
    private bool _finished;

    public ScreenCaptureOverlay(Bitmap snapshot, Rectangle virtualBounds, Action<Bitmap?> onDone)
    {
        _snapshot = snapshot;
        _virtualBounds = virtualBounds;
        _onDone = onDone;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        Text = "itrans 截图";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtualBounds;
        Activate();
        Focus();
    }

    protected override void WndProc(ref Message m)
    {
        const int wmKeyDown = 0x0100;
        if (m.Msg == wmKeyDown && m.WParam.ToInt32() == (int)Keys.Escape)
        {
            Finish(null);
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Finish(null);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Finish(null);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _dragging = true;
        _anchor = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _selection = Normalize(_anchor, e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;

        _dragging = false;
        Capture = false;
        _selection = Rectangle.Intersect(Normalize(_anchor, e.Location), ClientRectangle);
        if (_selection.Width < 4 || _selection.Height < 4)
        {
            Finish(null);
            return;
        }

        Bitmap crop;
        try
        {
            crop = _snapshot.Clone(_selection, _snapshot.PixelFormat);
        }
        catch
        {
            Finish(null);
            return;
        }
        Finish(crop);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.DrawImage(_snapshot, new Rectangle(Point.Empty, ClientSize), new Rectangle(Point.Empty, ClientSize), GraphicsUnit.Pixel);

        using var dim = new SolidBrush(Color.FromArgb(115, 8, 12, 24));
        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            graphics.FillRectangle(dim, ClientRectangle);
            DrawHint(graphics);
            return;
        }

        graphics.FillRectangle(dim, 0, 0, ClientSize.Width, _selection.Top);
        graphics.FillRectangle(dim, 0, _selection.Bottom, ClientSize.Width, ClientSize.Height - _selection.Bottom);
        graphics.FillRectangle(dim, 0, _selection.Top, _selection.Left, _selection.Height);
        graphics.FillRectangle(dim, _selection.Right, _selection.Top, ClientSize.Width - _selection.Right, _selection.Height);

        using var pen = new Pen(Color.FromArgb(255, 74, 158, 255), 2);
        graphics.DrawRectangle(pen, _selection.X, _selection.Y, _selection.Width - 1, _selection.Height - 1);
        DrawSizeLabel(graphics);
    }

    private void DrawHint(Graphics graphics)
    {
        const string hint = "拖动鼠标选择区域 · Esc 或右键取消";
        var screen = Screen.FromPoint(Cursor.Position);
        var local = new Rectangle(
            screen.Bounds.X - _virtualBounds.X,
            screen.Bounds.Y - _virtualBounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height);

        using var font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        var size = graphics.MeasureString(hint, font);
        var boxWidth = size.Width + 4;
        var boxHeight = size.Height + 4;
        var x = local.X + (local.Width - boxWidth) / 2f;
        var y = local.Y + Math.Max(32f, local.Height / 10f);

        using var background = new SolidBrush(Color.FromArgb(210, 17, 24, 39));
        using var text = new SolidBrush(Color.White);
        graphics.FillRectangle(background, x, y, boxWidth, boxHeight);
        graphics.DrawString(hint, font, text, x + 2, y + 2);
    }

    private void DrawSizeLabel(Graphics graphics)
    {
        var label = $"{_selection.Width} × {_selection.Height}";
        using var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        var size = graphics.MeasureString(label, font);
        var boxWidth = size.Width + 4;
        var boxHeight = size.Height + 4;
        var x = (float)_selection.Left;
        var y = _selection.Top - boxHeight - 6f;
        if (y < 4) y = _selection.Top + 6f;
        if (x + boxWidth > ClientSize.Width - 4) x = ClientSize.Width - boxWidth - 4;

        using var background = new SolidBrush(Color.FromArgb(220, 17, 24, 39));
        using var text = new SolidBrush(Color.White);
        graphics.FillRectangle(background, x, y, boxWidth, boxHeight);
        graphics.DrawString(label, font, text, x + 2, y + 2);
    }

    private static Rectangle Normalize(Point a, Point b)
        => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private void Finish(Bitmap? result)
    {
        if (_finished) return;
        _finished = true;
        try
        {
            Close();
        }
        finally
        {
            _snapshot.Dispose();
        }
        _onDone(result);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!_finished)
        {
            _finished = true;
            _snapshot.Dispose();
            _onDone(null);
        }
        base.OnFormClosed(e);
    }
}
