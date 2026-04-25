using System.Drawing.Drawing2D;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class TrayMenuBuilder
{
    private const int MenuMinWidth = 300;
    private const int MenuMaxWidth = 680;
    private const int HeaderHeight = 32;
    private const int StandardItemHeight = 30;
    private const int MenuSidePadding = 58;
    private const int MenuColumnGap = 20;
    private const int MaxStatusDisplayLength = 16;

    private static readonly Image ConfigGlyph = CreateConfigGlyph();
    private static readonly Image CoreGlyph = CreateCoreGlyph();
    private static readonly Image FolderGlyph = CreateFolderGlyph();
    private static readonly Image ExitGlyph = CreateExitGlyph();
    private static readonly Image RunningStateGlyph = CreateStateGlyph(Color.FromArgb(34, 197, 94));
    private static readonly Image StoppedStateGlyph = CreateStateGlyph(Color.FromArgb(245, 158, 11));
    private static readonly Image BusyStateGlyph = CreateStateGlyph(Color.FromArgb(59, 130, 246));
    private static readonly Image ErrorStateGlyph = CreateStateGlyph(Color.FromArgb(239, 68, 68));
    private static readonly Image UnavailableStateGlyph = CreateStateGlyph(Color.FromArgb(148, 163, 184));

    private readonly HeaderMenuItem _toggleItem;
    private readonly ToolStripMenuItem _importConfigItem;
    private readonly ToolStripMenuItem _importCoreItem;
    private readonly ToolStripMenuItem _openDataFolderItem;
    private readonly ToolStripMenuItem _exitItem;
    private ContextMenuStrip? _menu;

    public TrayMenuBuilder(
        EventHandler toggleHandler,
        EventHandler importConfigHandler,
        EventHandler importCoreHandler,
        EventHandler openDataFolderHandler,
        EventHandler exitHandler)
    {
        _toggleItem = new HeaderMenuItem(toggleHandler)
        {
            AutoSize = false,
            Size = new Size(MenuMinWidth, HeaderHeight),
            ShowShortcutKeys = true,
            Image = StoppedStateGlyph,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(2)
        };

        _importConfigItem = CreateStatusItem("Import Config", importConfigHandler);
        _importConfigItem.Image = ConfigGlyph;

        _importCoreItem = CreateStatusItem("Import Core", importCoreHandler);
        _importCoreItem.Image = CoreGlyph;

        _openDataFolderItem = CreateStandardItem("Open Data Folder", openDataFolderHandler);
        _openDataFolderItem.Image = FolderGlyph;

        _exitItem = CreateStandardItem("Exit", exitHandler);
        _exitItem.Image = ExitGlyph;
    }

    public ContextMenuStrip Build()
    {
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            ShowCheckMargin = false,
            ShowItemToolTips = true,
            Padding = new Padding(6),
            Renderer = new TrayMenuRenderer(),
            BackColor = Color.FromArgb(249, 252, 255),
            Items =
            {
                _toggleItem,
                new ToolStripSeparator { Margin = new Padding(8, 4, 8, 4) },
                _importConfigItem,
                _importCoreItem,
                _openDataFolderItem,
                _exitItem
            }
        };

        UpdateMenuWidth();
        return _menu;
    }

    public void ApplyStatus(StatusInfo? status, bool serviceAvailable)
    {
        if (!serviceAvailable || status is null)
        {
            _toggleItem.SetState("Unavailable");
            _toggleItem.Image = UnavailableStateGlyph;
            _toggleItem.ForeColor = Color.FromArgb(100, 116, 139);
            _toggleItem.Enabled = false;
            _toggleItem.Checked = false;

            SetStatusItem(_importConfigItem, "Unavailable", enabled: false);
            SetStatusItem(_importCoreItem, "Unavailable", enabled: false);
            _openDataFolderItem.Enabled = true;
            UpdateMenuWidth();
            return;
        }

        var stateLabel = status.RunState switch
        {
            RunState.Running => "Running",
            RunState.Stopped => "Stopped",
            RunState.Starting => "Starting",
            RunState.Stopping => "Stopping",
            RunState.Error => "Error",
            _ => "Unknown"
        };

        _toggleItem.SetState(stateLabel);
        _toggleItem.Image = status.RunState switch
        {
            RunState.Running => RunningStateGlyph,
            RunState.Error => ErrorStateGlyph,
            RunState.Starting => BusyStateGlyph,
            RunState.Stopping => BusyStateGlyph,
            _ => StoppedStateGlyph
        };
        _toggleItem.ForeColor = ResolveHeaderColor(status.RunState);
        _toggleItem.Enabled = status.RunState is not RunState.Starting and not RunState.Stopping;
        _toggleItem.Checked = status.RunState == RunState.Running;

        var importEnabled = status.RunState is not RunState.Starting and not RunState.Stopping and not RunState.Running;
        SetStatusItem(_importConfigItem, BuildConfigStatusLabel(status), enabled: importEnabled);
        SetStatusItem(_importCoreItem, BuildCoreStatusLabel(status), enabled: importEnabled);
        _openDataFolderItem.Enabled = true;

        UpdateMenuWidth();
    }

    private static ToolStripMenuItem CreateStatusItem(string text, EventHandler clickHandler)
    {
        return new ToolStripMenuItem(text, null, clickHandler)
        {
            AutoSize = false,
            Size = new Size(MenuMinWidth, StandardItemHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            ShowShortcutKeys = true,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(2)
        };
    }

    private static ToolStripMenuItem CreateStandardItem(string text, EventHandler clickHandler)
    {
        return new ToolStripMenuItem(text, null, clickHandler)
        {
            AutoSize = false,
            Size = new Size(MenuMinWidth, StandardItemHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(2)
        };
    }

    private void UpdateMenuWidth()
    {
        if (_menu is null)
        {
            return;
        }

        var items = new ToolStripMenuItem[]
        {
            _toggleItem,
            _importConfigItem,
            _importCoreItem,
            _openDataFolderItem,
            _exitItem
        };

        var requiredWidth = MenuMinWidth;
        foreach (var item in items)
        {
            requiredWidth = Math.Max(requiredWidth, MeasureMenuItemWidth(item));
        }

        requiredWidth = Math.Clamp(requiredWidth, MenuMinWidth, MenuMaxWidth);

        foreach (var item in items)
        {
            item.Width = requiredWidth;
        }

        _menu.MinimumSize = new Size(requiredWidth, 0);
    }

    private static int MeasureMenuItemWidth(ToolStripMenuItem item)
    {
        var text = item.Text ?? string.Empty;
        var textWidth = TextRenderer.MeasureText(text, item.Font).Width;
        var imageWidth = item.Image is null ? 0 : item.Image.Width + 16;
        var shortcutWidth = string.IsNullOrWhiteSpace(item.ShortcutKeyDisplayString)
            ? 0
            : TextRenderer.MeasureText(item.ShortcutKeyDisplayString, item.Font).Width + MenuColumnGap;

        return textWidth + shortcutWidth + imageWidth + MenuSidePadding;
    }

    private static void SetStatusItem(ToolStripMenuItem item, string status, bool enabled)
    {
        item.ShortcutKeyDisplayString = TruncateStatus(status);
        item.ToolTipText = status;
        item.Enabled = enabled;
    }

    private static string TruncateStatus(string status)
    {
        if (string.IsNullOrEmpty(status) || status.Length <= MaxStatusDisplayLength)
        {
            return status;
        }

        return string.Concat(status.AsSpan(0, MaxStatusDisplayLength - 1), "\u2026");
    }

    private static string BuildConfigStatusLabel(StatusInfo status)
    {
        if (!status.Config.Installed)
        {
            return "Unconfigured";
        }

        if (!status.Core.Installed || !status.Core.Valid)
        {
            return "Waiting";
        }

        if (!status.Config.Valid)
        {
            return "Error";
        }

        if (string.IsNullOrWhiteSpace(status.Config.FileName))
        {
            return "Configured";
        }

        return Path.GetFileName(status.Config.FileName!);
    }

    private static string BuildCoreStatusLabel(StatusInfo status)
    {
        if (!status.Core.Installed)
        {
            return "Missing";
        }

        if (!status.Core.Valid)
        {
            return "Error";
        }

        if (string.IsNullOrWhiteSpace(status.Core.Version))
        {
            return "Ready";
        }

        var version = status.Core.Version!.Trim();
        if (version.StartsWith("sing-box ", StringComparison.OrdinalIgnoreCase))
        {
            version = version["sing-box ".Length..].TrimStart();
        }

        if (version.StartsWith("version ", StringComparison.OrdinalIgnoreCase))
        {
            version = version["version ".Length..].TrimStart();
        }

        return version;
    }

    private static Color ResolveHeaderColor(RunState runState)
    {
        return runState switch
        {
            RunState.Running => Color.FromArgb(22, 101, 52),
            RunState.Error => Color.FromArgb(153, 27, 27),
            RunState.Starting => Color.FromArgb(30, 64, 175),
            RunState.Stopping => Color.FromArgb(30, 64, 175),
            _ => Color.FromArgb(55, 65, 81)
        };
    }

    private static Image CreateStateGlyph(Color color)
    {
        return CreateGlyph(graphics =>
        {
            using var fill = new SolidBrush(color);
            using var border = new Pen(Color.FromArgb(190, color.R, color.G, color.B), 1f);
            graphics.FillEllipse(fill, 3, 3, 10, 10);
            graphics.DrawEllipse(border, 3, 3, 10, 10);
        });
    }

    private static Image CreateConfigGlyph()
    {
        return CreateGlyph(graphics =>
        {
            using var fill = new SolidBrush(Color.FromArgb(224, 233, 252));
            using var border = new Pen(Color.FromArgb(72, 99, 165), 1.2f);
            using var line = new Pen(Color.FromArgb(98, 122, 183), 1f);
            graphics.FillRectangle(fill, 3, 2, 10, 12);
            graphics.DrawRectangle(border, 3, 2, 10, 12);
            graphics.DrawLine(line, 5, 6, 11, 6);
            graphics.DrawLine(line, 5, 9, 11, 9);
        });
    }

    private static Image CreateCoreGlyph()
    {
        return CreateGlyph(graphics =>
        {
            using var fill = new SolidBrush(Color.FromArgb(225, 245, 233));
            using var border = new Pen(Color.FromArgb(32, 121, 78), 1.2f);
            graphics.FillRectangle(fill, 2, 4, 12, 9);
            graphics.DrawRectangle(border, 2, 4, 12, 9);
            graphics.DrawLine(border, 2, 7, 14, 7);
            graphics.DrawLine(border, 8, 4, 8, 13);
        });
    }

    private static Image CreateFolderGlyph()
    {
        return CreateGlyph(graphics =>
        {
            using var fill = new SolidBrush(Color.FromArgb(252, 228, 176));
            using var border = new Pen(Color.FromArgb(176, 121, 34), 1.2f);
            graphics.FillRectangle(fill, 2, 5, 12, 8);
            graphics.FillRectangle(fill, 4, 3, 5, 3);
            graphics.DrawRectangle(border, 2, 5, 12, 8);
            graphics.DrawLine(border, 4, 3, 9, 3);
            graphics.DrawLine(border, 9, 3, 10, 5);
        });
    }

    private static Image CreateExitGlyph()
    {
        return CreateGlyph(graphics =>
        {
            using var pen = new Pen(Color.FromArgb(198, 58, 58), 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pen, 4, 4, 12, 12);
            graphics.DrawLine(pen, 12, 4, 4, 12);
        });
    }

    private static Bitmap CreateGlyph(Action<Graphics> draw)
    {
        const int glyphSize = 16;
        var bitmap = new Bitmap(glyphSize, glyphSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        draw(graphics);
        return bitmap;
    }
}

internal sealed class HeaderMenuItem : ToolStripMenuItem
{
    public HeaderMenuItem(EventHandler clickHandler) : base("SingTray", null, clickHandler)
    {
        ShortcutKeyDisplayString = "Loading";
        TextAlign = ContentAlignment.MiddleLeft;
        ForeColor = Color.FromArgb(55, 65, 81);
        Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold);
    }

    public void SetState(string state)
    {
        ShortcutKeyDisplayString = state;
    }
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = CreateRoundedRectanglePath(bounds, 6);
        using var brush = new SolidBrush(Color.FromArgb(226, 237, 254));
        using var border = new Pen(Color.FromArgb(191, 209, 238));
        var previousSmoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);
        e.Graphics.SmoothingMode = previousSmoothing;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.ContentRectangle.Top + (e.Item.ContentRectangle.Height / 2);
        using var pen = new Pen(Color.FromArgb(219, 227, 240));
        e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left + 12, y, e.Item.ContentRectangle.Right - 12, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem menuItem && !string.IsNullOrWhiteSpace(menuItem.ShortcutKeyDisplayString))
        {
            var isShortcutColumn = (e.TextDirection == ToolStripTextDirection.Horizontal)
                && e.Text == menuItem.ShortcutKeyDisplayString;

            if (isShortcutColumn)
            {
                var statusColor = e.Item.Enabled
                    ? Color.FromArgb(100, 116, 139)
                    : Color.FromArgb(178, 190, 205);

                var rightPadding = menuItem.Padding.Right + 4;
                var statusBounds = new Rectangle(
                    e.Item.ContentRectangle.Left,
                    e.Item.ContentRectangle.Top,
                    e.Item.ContentRectangle.Width - rightPadding,
                    e.Item.ContentRectangle.Height);

                TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    statusBounds,
                    statusColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
        }

        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(31, 41, 55)
            : Color.FromArgb(148, 163, 184);
        base.OnRenderItemText(e);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TrayMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(249, 252, 255);
    public override Color MenuBorder => Color.FromArgb(202, 214, 235);
    public override Color MenuItemBorder => Color.FromArgb(191, 209, 238);
    public override Color MenuItemSelected => Color.FromArgb(226, 237, 254);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(226, 237, 254);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(226, 237, 254);
    public override Color ImageMarginGradientBegin => Color.FromArgb(249, 252, 255);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(249, 252, 255);
    public override Color ImageMarginGradientEnd => Color.FromArgb(249, 252, 255);
    public override Color SeparatorDark => Color.FromArgb(219, 227, 240);
    public override Color SeparatorLight => Color.FromArgb(219, 227, 240);
}
