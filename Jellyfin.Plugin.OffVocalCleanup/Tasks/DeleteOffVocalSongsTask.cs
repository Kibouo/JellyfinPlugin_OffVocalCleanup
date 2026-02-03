using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MediaType = Jellyfin.Data.Enums.MediaType;

namespace Jellyfin.Plugin.OffVocalCleanup.Tasks;

/// <summary>
/// Scheduled task to identify and delete off-vocal songs.
/// All code heavily based on https://github.com/jellyfin/jellyfin-plugin-subtitleextract/blob/master/Jellyfin.Plugin.SubtitleExtract/Tasks/ExtractSubtitlesTask.cs.
/// </summary>
public class DeleteOffVocalSongsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localizationManager;
    private readonly ILogger<DeleteOffVocalSongsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteOffVocalSongsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/> interface.</param>
    /// <param name="localizationManager">Instance of <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of <see cref="ILogger"/> interface.</param>
    public DeleteOffVocalSongsTask(ILibraryManager libraryManager, ILocalizationManager localizationManager, ILogger<DeleteOffVocalSongsTask> logger)
    {
        _libraryManager = libraryManager;
        _localizationManager = localizationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => "DeleteOffVocalSongs";

    /// <inheritdoc />
    public string Name => "Delete Off-Vocal Songs";

    /// <inheritdoc/>
    public string Description => "Scans the Media Library for off-vocal tracks and deletes them.";

    /// <inheritdoc />
    public string Category => _localizationManager.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startProgress = 0d;

        var config = Plugin.Instance.Configuration;
        var libs = config.SelectedMediaLibraries;

        Guid[] parentIds = [];
        if (libs.Length > 0)
        {
            // Try to get parent ids from the selected libraries
            parentIds = _libraryManager.GetVirtualFolders()
                .Where(vf => libs.Contains(vf.Name))
                .Select(vf => Guid.Parse(vf.ItemId))
                .ToArray();
        }

        if (parentIds.Length > 0)
        {
            // In case parent ids are found, run the extraction on each found library
            foreach (var parentId in parentIds)
            {
                startProgress = ProcessWithProgress(progress, parentId, parentIds, startProgress, cancellationToken);
            }
        }
        else
        {
            // Otherwise run it on everything
            ProcessWithProgress(progress, null, [], startProgress, cancellationToken);
        }

        progress.Report(100);

        // no async code but the interface expects this function to be async...
        await Task.FromResult(false).ConfigureAwait(false);
    }

    private double ProcessWithProgress(
        IProgress<double> progress,
        Guid? parentId,
        Guid[] parentIds,
        double startProgress,
        CancellationToken cancellationToken)
    {
        // re-used values
        var queryPageLimit = 100;
        var config = Plugin.Instance.Configuration;
        _logger.LogInformation("Execution mode: {ExecutionMode}", config.ExecutionMode);

        // data to process
        var keywords = config.OffVocalKeywords.Split("|");
        _logger.LogDebug("Keyword(s) to look for in filenames: {Keywords}", keywords);

        // progress reporting
        var libsCount = parentIds.Length > 0 ? parentIds.Length : 1;
        var completedKeywords = 0;
        var keywordsCount = keywords.Length;
        _logger.LogDebug("Amount of keywords: {KeywordsCount}", keywordsCount);

        // start work
        foreach (var keyword in keywords)
        {
            // DB query for keyword
            _logger.LogDebug("Now querying for keyword: {Keyword}", keyword);
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IsVirtualItem = false,
                IncludeItemTypes = [BaseItemKind.Audio, BaseItemKind.AudioBook, BaseItemKind.Recording, BaseItemKind.MusicVideo],
                DtoOptions = new(false), // don't return all fields
                MediaTypes = [MediaType.Audio],
                SourceTypes = [SourceType.Library],
                Limit = queryPageLimit,
                NameContains = keyword,
            };
            if (!parentId.IsNullOrEmpty())
            {
                query.ParentId = parentId.Value;
            }

            // pagination result
            var entriesForKeyword = new List<BaseItem>();
            // pagination helpers
            var startIndex = 0;
            var queryResultLength = _libraryManager.GetCount(query);
            _logger.LogDebug("Resulting amount of matched files: {QueryResultLength}", queryResultLength);
            // pagination processing
            while (startIndex < queryResultLength)
            {
                query.StartIndex = startIndex;

                foreach (var entry in _libraryManager.GetItemList(query))
                {
                    entriesForKeyword.Add(entry);

                    // report the progress using "startProgress" that allows to track progress across multiple libraries
                    progress.Report(startProgress + (100d * entriesForKeyword.Count / queryResultLength / completedKeywords / keywordsCount / libsCount));
                    // check if user cancelled
                    cancellationToken.ThrowIfCancellationRequested();
                }

                startIndex += queryPageLimit;
            }

            _logger.LogInformation("Keyword {Keyword} matched {FileCount} file(s).", keyword, entriesForKeyword.Count);
            _logger.LogDebug("Matched files: {EntriesForKeyword}", entriesForKeyword);
            if (config.ExecutionMode == Configuration.ExecutionMode.Destructive)
            {
                var deleteOptions = new DeleteOptions { DeleteFileLocation = true };
                foreach (var entry in entriesForKeyword)
                {
                    try
                    {
                        _libraryManager.DeleteItem(entry, deleteOptions, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting item: {Entry} ({Path})", entry, entry.Path);
                    }
                }
            }
        }

        // update the startProgress to the current progress for next libraries
        startProgress += 100d / libsCount;
        return startProgress;
    }
}
