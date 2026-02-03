using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OffVocalCleanup.Configuration;

/// <summary>
/// Safety/debug switch.
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Logs the paths of the identified files.
    /// </summary>
    Audit,

    /// <summary>
    /// Actually does perform removal of identified files.
    /// </summary>
    Destructive
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ExecutionMode = ExecutionMode.Audit;
        SelectedMediaLibraries = [];
        OffVocalKeywords = "karaoke|カラオケ|offvocal|off vocal|off-vocal|オフボーカル|instrumental|instr.|インストゥルメンタル|accoustic|acoustic|acapella|アカペラ|orchestra|オーケストラ";
    }

    /// <summary>
    /// Gets or sets the list of selected libraries (empty means all).
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public string[] SelectedMediaLibraries { get; set; } = [];
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// Gets or sets an enum option.
    /// </summary>
    public ExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// Gets or sets a pipe-separated (`|`) list of keywords matched against on-disk filenames to identify off-vocal songs.
    /// </summary>
    public string OffVocalKeywords { get; set; }
}
