using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class TrayMenuBuilder
{
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _coreStatusItem;
    private readonly ToolStripMenuItem _configStatusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _importConfigItem;
    private readonly ToolStripMenuItem _importCoreItem;
    private readonly ToolStripMenuItem _openDataFolderItem;
    private readonly ToolStripMenuItem _exitItem;

    public TrayMenuBuilder(
        EventHandler toggleHandler,
        EventHandler importConfigHandler,
        EventHandler importCoreHandler,
        EventHandler openDataFolderHandler,
        EventHandler exitHandler)
    {
        _statusItem = CreateInfoItem("Status: Unavailable");
        _coreStatusItem = CreateInfoItem("Core: Unknown");
        _configStatusItem = CreateInfoItem("Config: Unknown");
        _toggleItem = CreateActionItem("Start", toggleHandler);
        _importConfigItem = CreateActionItem("Import Config...", importConfigHandler);
        _importCoreItem = CreateActionItem("Import Core...", importCoreHandler);
        _openDataFolderItem = CreateActionItem("Open Data Folder", openDataFolderHandler);
        _exitItem = CreateActionItem("Exit", exitHandler);
    }

    public ContextMenuStrip Build()
    {
        return new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            ShowItemToolTips = true,
            Items =
            {
                _statusItem,
                _coreStatusItem,
                _configStatusItem,
                new ToolStripSeparator(),
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
            SetInfoItem(_statusItem, "Status: Unavailable");
            SetInfoItem(_coreStatusItem, "Core: Unknown");
            SetInfoItem(_configStatusItem, "Config: Unknown");
            _toggleItem.Text = "Start";
            _toggleItem.Enabled = false;
            _importConfigItem.Enabled = false;
            _importCoreItem.Enabled = false;
            _openDataFolderItem.Enabled = true;
            return;
        }

        var (stateLabel, toggleText, toggleEnabled) = status.RunState switch
        {
            RunState.Running => ("Running", "Stop", true),
            RunState.Stopped => ("Stopped", "Start", true),
            RunState.Starting => ("Starting", "Starting...", false),
            RunState.Stopping => ("Stopping", "Stopping...", false),
            RunState.Error => ("Error", "Start", true),
            _ => ("Unavailable", "Start", false)
        };

        SetInfoItem(_statusItem, "Status: " + stateLabel);

        var fullCoreText = BuildCoreStatusLabel(status);
        SetInfoItem(_coreStatusItem, "Core: " + Ellipsize(fullCoreText), "Core: " + fullCoreText);

        var fullConfigText = BuildConfigStatusLabel(status);
        SetInfoItem(_configStatusItem, "Config: " + Ellipsize(fullConfigText), "Config: " + fullConfigText);

        _toggleItem.Text = toggleText;
        _toggleItem.Enabled = toggleEnabled;

        var importEnabled = status.RunState is RunState.Stopped or RunState.Error;
        _importConfigItem.Enabled = importEnabled;
        _importCoreItem.Enabled = importEnabled;
        _openDataFolderItem.Enabled = true;
    }

    private static ToolStripMenuItem CreateInfoItem(string text)
    {
        return new ToolStripMenuItem(text)
        {
            Enabled = false,
            AutoToolTip = false
        };
    }

    private static ToolStripMenuItem CreateActionItem(string text, EventHandler clickHandler)
    {
        return new ToolStripMenuItem(text, null, clickHandler);
    }

    private static void SetInfoItem(ToolStripMenuItem item, string text)
    {
        SetInfoItem(item, text, text);
    }

    private static void SetInfoItem(ToolStripMenuItem item, string text, string tooltipText)
    {
        item.Text = text;
        item.ToolTipText = tooltipText;
    }

    private static string Ellipsize(string? text, int maxChars = 36)
    {
        if (maxChars <= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars), "maxChars must be greater than 8.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= maxChars ? text : text[..(maxChars - 3)] + "...";
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
}
