using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class TrayMenuBuilder
{
    private const int MenuWidth = 240;
    private const int HeaderHeight = 52;
    private const int StandardItemHeight = 28;

    private readonly HeaderMenuItem _toggleItem;
    private readonly StatusMenuItem _importConfigItem;
    private readonly StatusMenuItem _importCoreItem;
    private readonly ToolStripMenuItem _openDataFolderItem;
    private readonly ToolStripMenuItem _exitItem;

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
            Size = new Size(MenuWidth, HeaderHeight)
        };

        _importConfigItem = new StatusMenuItem("Import Config", importConfigHandler)
        {
            AutoSize = false,
            Size = new Size(MenuWidth, StandardItemHeight)
        };

        _importCoreItem = new StatusMenuItem("Import Core", importCoreHandler)
        {
            AutoSize = false,
            Size = new Size(MenuWidth, StandardItemHeight)
        };

        _openDataFolderItem = CreateStandardItem("Open Data Folder", openDataFolderHandler);
        _exitItem = CreateStandardItem("Exit", exitHandler);
    }

    public ContextMenuStrip Build()
    {
        return new ContextMenuStrip
        {
            ShowImageMargin = false,
            Items =
            {
                _toggleItem,
                new ToolStripSeparator(),
                _importConfigItem,
                _importCoreItem,
                _openDataFolderItem,
                _exitItem
            }
        };
    }

    public void ApplyStatus(StatusInfo? status, bool serviceAvailable)
    {
        if (!serviceAvailable || status is null)
        {
            _toggleItem.SetState("Service unavailable");
            _toggleItem.Enabled = false;
            _toggleItem.Checked = false;

            _importConfigItem.SetStatus("Unavailable");
            _importConfigItem.Enabled = false;

            _importCoreItem.SetStatus("Unavailable");
            _importCoreItem.Enabled = false;

            _openDataFolderItem.Enabled = true;
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
        _toggleItem.Enabled = status.RunState is not RunState.Starting and not RunState.Stopping;
        _toggleItem.Checked = status.RunState == RunState.Running;

        _importConfigItem.SetStatus(BuildConfigStatusLabel(status));
        _importConfigItem.Enabled = !status.SingBoxRunning;

        _importCoreItem.SetStatus(BuildCoreStatusLabel(status));
        _importCoreItem.Enabled = !status.SingBoxRunning;

        _openDataFolderItem.Enabled = true;
    }

    private static ToolStripMenuItem CreateStandardItem(string text, EventHandler clickHandler)
    {
        return new ToolStripMenuItem(text, null, clickHandler)
        {
            AutoSize = false,
            Size = new Size(MenuWidth, StandardItemHeight),
            TextAlign = ContentAlignment.MiddleLeft
        };
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

        return string.IsNullOrWhiteSpace(status.Config.FileName) ? "Configured" : status.Config.FileName!;
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

        var version = status.Core.Version!;
        return version.StartsWith("sing-box ", StringComparison.OrdinalIgnoreCase)
            ? version["sing-box ".Length..]
            : version;
    }
}

internal sealed class HeaderMenuItem : ToolStripMenuItem
{
    private string _state = "Loading";

    public HeaderMenuItem(EventHandler clickHandler) : base("SingTray", null, clickHandler)
    {
    }

    public void SetState(string state)
    {
        _state = state;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, Size);
        var selected = Selected && Enabled;
        using var background = new SolidBrush(selected ? Color.FromArgb(210, 228, 246) : SystemColors.ControlLightLight);
        e.Graphics.FillRectangle(background, bounds);

        using var border = new Pen(Color.FromArgb(192, 210, 230));
        e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);

        var left = 10;
        var titleRect = new Rectangle(left, 7, bounds.Width - (left * 2), 18);
        var stateRect = new Rectangle(left, 26, bounds.Width - (left * 2), 17);

        var headerFont = Font ?? SystemFonts.MenuFont ?? Control.DefaultFont;

        TextRenderer.DrawText(
            e.Graphics,
            "SingTray",
            new Font(headerFont, FontStyle.Bold),
            titleRect,
            SystemColors.ControlText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            _state,
            headerFont,
            stateRect,
            Color.FromArgb(64, 64, 64),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class StatusMenuItem : ToolStripMenuItem
{
    private readonly string _title;
    private string _status = string.Empty;

    public StatusMenuItem(string title, EventHandler clickHandler) : base(title, null, clickHandler)
    {
        _title = title;
    }

    public void SetStatus(string status)
    {
        _status = status;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, Size);
        var selected = Selected && Enabled;
        using var background = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.ControlLightLight);
        e.Graphics.FillRectangle(background, bounds);

        var leftRect = new Rectangle(10, 0, bounds.Width - 90, bounds.Height);
        var rightRect = new Rectangle(bounds.Width - 78, 0, 68, bounds.Height);
        var mainColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
        var secondaryColor = selected ? SystemColors.HighlightText : Color.FromArgb(90, 90, 90);

        TextRenderer.DrawText(
            e.Graphics,
            _title,
            Font ?? SystemFonts.MenuFont,
            leftRect,
            mainColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            _status,
            Font ?? SystemFonts.MenuFont,
            rightRect,
            secondaryColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
