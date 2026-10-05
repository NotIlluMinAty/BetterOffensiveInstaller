using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Windows.Forms;

namespace BetterOffensiveInstaller;

public sealed class MainForm : Form
{
    private const string DefaultInstallPath = @"C:\Program Files (x86)\Steam\steamapps\common\csgo legacy";
    private static readonly string InstallerTempBasePath = Path.Combine(Path.GetTempPath(), "BetterOffensiveInstaller");

    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly ReleaseService _releaseService;
    private readonly DefenderExclusionService _defenderExclusionService = new();

    private readonly Label _titleLabel;
    private readonly Label _introLabel;
    private readonly Label _languageLabel;
    private readonly ComboBox _languageComboBox;
    private readonly GroupBox _releaseGroup;
    private readonly Label _releaseLabel;
    private readonly Label _releaseDetailsLabel;
    private readonly GroupBox _modeGroup;
    private readonly RadioButton _fullRadioButton;
    private readonly RadioButton _patchRadioButton;
    private readonly Label _modeLabel;
    private readonly GroupBox _destinationGroup;
    private readonly Label _destinationLabel;
    private readonly TextBox _pathTextBox;
    private readonly Button _browseButton;
    private readonly Button _refreshButton;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;
    private readonly Button _exitButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;

    private ReleaseInfo? _currentRelease;
    private InstallerLanguage _currentLanguage;
    private CultureInfo _currentCulture;
    private bool _isBusy;
    private bool _isApplyingLanguage;
    private ReleaseUiState _releaseUiState = ReleaseUiState.Loading;
    private TextId _statusTextId = TextId.StatusLoading;
    private object[] _statusArgs = [];

    public MainForm()
    {
        _releaseService = new ReleaseService(_httpClient);
        _currentLanguage = LocalizationCatalog.Resolve(CultureInfo.CurrentUICulture);
        _currentCulture = LocalizationCatalog.GetCulture(_currentLanguage);

        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        ClientSize = new Size(680, 468);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(16, 14)
        };

        _languageLabel = new Label
        {
            AutoSize = true,
            Location = new Point(428, 17)
        };

        _languageComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(488, 13),
            Size = new Size(176, 23)
        };
        _languageComboBox.SelectedIndexChanged += LanguageComboBox_SelectedIndexChanged;

        _introLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 42),
            Size = new Size(648, 34)
        };

        _releaseGroup = new GroupBox
        {
            Location = new Point(16, 84),
            Size = new Size(648, 84)
        };

        _releaseLabel = new Label
        {
            AutoSize = true,
            Location = new Point(14, 24)
        };

        _releaseDetailsLabel = new Label
        {
            AutoSize = false,
            Location = new Point(14, 44),
            Size = new Size(620, 30)
        };

        _releaseGroup.Controls.Add(_releaseLabel);
        _releaseGroup.Controls.Add(_releaseDetailsLabel);

        _modeGroup = new GroupBox
        {
            Location = new Point(16, 176),
            Size = new Size(648, 94)
        };

        _fullRadioButton = new RadioButton
        {
            AutoSize = true,
            Checked = true,
            Location = new Point(16, 26)
        };
        _fullRadioButton.CheckedChanged += InstallModeChanged;

        _patchRadioButton = new RadioButton
        {
            AutoSize = true,
            Location = new Point(160, 26)
        };
        _patchRadioButton.CheckedChanged += InstallModeChanged;

        _modeLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 52),
            Size = new Size(615, 30)
        };

        _modeGroup.Controls.Add(_fullRadioButton);
        _modeGroup.Controls.Add(_patchRadioButton);
        _modeGroup.Controls.Add(_modeLabel);

        _destinationGroup = new GroupBox
        {
            Location = new Point(16, 278),
            Size = new Size(648, 92)
        };

        _destinationLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 28)
        };

        _pathTextBox = new TextBox
        {
            Location = new Point(16, 50),
            Size = new Size(526, 23),
            Text = DefaultInstallPath
        };
        _pathTextBox.TextChanged += (_, _) => UpdateControlState();

        _browseButton = new Button
        {
            Location = new Point(552, 48),
            Size = new Size(80, 25)
        };
        _browseButton.Click += BrowseButton_Click;

        _destinationGroup.Controls.Add(_destinationLabel);
        _destinationGroup.Controls.Add(_pathTextBox);
        _destinationGroup.Controls.Add(_browseButton);

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 380),
            Size = new Size(648, 18),
            Style = ProgressBarStyle.Marquee
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 404),
            Size = new Size(648, 24)
        };

        _refreshButton = new Button
        {
            Location = new Point(306, 434),
            Size = new Size(82, 27)
        };
        _refreshButton.Click += RefreshButton_Click;

        _installButton = new Button
        {
            Location = new Point(398, 434),
            Size = new Size(82, 27),
            Enabled = false
        };
        _installButton.Click += InstallButton_Click;

        _uninstallButton = new Button
        {
            Location = new Point(490, 434),
            Size = new Size(82, 27),
            Enabled = false
        };
        _uninstallButton.Click += UninstallButton_Click;

        _exitButton = new Button
        {
            Location = new Point(582, 434),
            Size = new Size(82, 27)
        };
        _exitButton.Click += (_, _) => Close();

        Controls.Add(_titleLabel);
        Controls.Add(_languageLabel);
        Controls.Add(_languageComboBox);
        Controls.Add(_introLabel);
        Controls.Add(_releaseGroup);
        Controls.Add(_modeGroup);
        Controls.Add(_destinationGroup);
        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
        Controls.Add(_refreshButton);
        Controls.Add(_installButton);
        Controls.Add(_uninstallButton);
        Controls.Add(_exitButton);

        PopulateLanguageList();
        ApplyLanguage(_currentLanguage);

        Shown += async (_, _) => await LoadLatestReleaseAsync(showDialogOnError: true);
        FormClosed += (_, _) => _httpClient.Dispose();
    }

    private InstallMode SelectedMode => _patchRadioButton.Checked ? InstallMode.Patch : InstallMode.Full;

    private async void RefreshButton_Click(object? sender, EventArgs e)
    {
        await LoadLatestReleaseAsync(showDialogOnError: true);
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = GetText(TextId.BrowseDialogDescription),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_pathTextBox.Text) ? _pathTextBox.Text : DefaultInstallPath
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void InstallModeChanged(object? sender, EventArgs e)
    {
        UpdateModeDescription();
    }

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        await RunInstallAsync();
    }

    private async void UninstallButton_Click(object? sender, EventArgs e)
    {
        await RunUninstallAsync();
    }

    private async Task LoadLatestReleaseAsync(bool showDialogOnError)
    {
        try
        {
            _releaseUiState = ReleaseUiState.Loading;
            SetBusyState(true, TextId.StatusCheckingRelease, true, 0);
            UpdateReleaseText();

            _currentRelease = await _releaseService.GetLatestReleaseAsync();
            _releaseUiState = ReleaseUiState.Ready;
            UpdateReleaseText();

            SetBusyState(false, TextId.StatusReady, false, 0);
        }
        catch (Exception ex)
        {
            _currentRelease = null;
            _releaseUiState = ReleaseUiState.Failed;
            UpdateReleaseText();

            var errorMessage = GetErrorMessage(ex);
            SetBusyState(false, TextId.ReleaseLookupFailedPrefix, false, 0, errorMessage);

            if (showDialogOnError)
            {
                MessageBox.Show(
                    this,
                    Format(TextId.ReleaseLookupFailedDialogFormat, errorMessage),
                    GetText(TextId.ReleaseLookupFailedTitle),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    private async Task RunInstallAsync()
    {
        if (string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            MessageBox.Show(
                this,
                GetText(TextId.MissingDestinationMessage),
                GetText(TextId.MissingDestinationTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_currentRelease is null)
        {
            await LoadLatestReleaseAsync(showDialogOnError: true);

            if (_currentRelease is null)
            {
                return;
            }
        }

        var selectedAsset = _currentRelease.GetAsset(SelectedMode);
        var rollbackExclusions = Array.Empty<string>();
        string installPath;
        string? tempRoot = null;
        string? archivePath = null;

        try
        {
            installPath = Path.GetFullPath(_pathTextBox.Text.Trim());
            tempRoot = Path.Combine(InstallerTempBasePath, Guid.NewGuid().ToString("N"));
            archivePath = Path.Combine(tempRoot, selectedAsset.Name);

            SetBusyState(true, TextId.StatusPreparingInstall, true, 0);

            Directory.CreateDirectory(InstallerTempBasePath);
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(installPath);

            SetStatus(TextId.StatusConfiguringExclusions, true, 0);
            ManagedExclusion[] managedExclusions;

            if (await _defenderExclusionService.IsDefenderAvailableAsync())
            {
                managedExclusions = (await _defenderExclusionService
                        .EnsureExcludedAsync([installPath, InstallerTempBasePath]))
                    .ToArray();
            }
            else
            {
                SetStatus(TextId.StatusManualExclusionRequired, true, 0);

                if (!PromptForManualAntivirusExclusions(installPath))
                {
                    SetBusyState(false, TextId.StatusReady, false, 0);
                    return;
                }

                managedExclusions = [];
            }

            rollbackExclusions = managedExclusions
                .Where(exclusion => exclusion.AddedByInstaller)
                .Select(exclusion => exclusion.Path)
                .ToArray();

            var existingState = await InstallStateStore.TryLoadAsync(installPath);
            var progress = new Progress<InstallProgress>(UpdateProgress);

            await DownloadAssetAsync(selectedAsset, archivePath, progress);
            var extractedContent = await ExtractArchiveAsync(archivePath, installPath, existingState, progress);

            SetStatus(TextId.StatusSavingInstallState, true, 95);
            var installState = BuildInstallState(installPath, existingState, _currentRelease.TagName, extractedContent, managedExclusions);
            await InstallStateStore.SaveAsync(installPath, installState);

            SetBusyState(false, TextId.InstallCompleteStatus, false, 100);

            MessageBox.Show(
                this,
                Format(TextId.InstallCompleteDialogFormat, _currentRelease.TagName, installPath),
                GetText(TextId.InstallCompleteTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            await TryRollbackExclusionsAsync(rollbackExclusions);

            var errorMessage = GetErrorMessage(ex);
            SetBusyState(false, TextId.InstallFailedPrefix, false, _progressBar.Value, errorMessage);

            MessageBox.Show(
                this,
                Format(TextId.InstallFailedDialogFormat, errorMessage),
                GetText(TextId.InstallFailedTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (tempRoot is not null)
            {
                TryDeleteDirectory(tempRoot);
            }
            UpdateControlState();
        }
    }

    private async Task RunUninstallAsync()
    {
        if (string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            MessageBox.Show(
                this,
                GetText(TextId.UninstallMissingStateMessage),
                GetText(TextId.UninstallMissingStateTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var installPath = Path.GetFullPath(_pathTextBox.Text.Trim());
            var state = await InstallStateStore.TryLoadAsync(installPath);

            if (state is null)
            {
                MessageBox.Show(
                    this,
                    GetText(TextId.UninstallMissingStateMessage),
                    GetText(TextId.UninstallMissingStateTitle),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    this,
                    Format(TextId.UninstallConfirmMessageFormat, installPath),
                    GetText(TextId.UninstallConfirmTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            SetBusyState(true, TextId.StatusPreparingUninstall, true, 0);

            var progress = new Progress<InstallProgress>(UpdateProgress);
            await RemoveInstalledContentAsync(installPath, state, progress);

            SetStatus(TextId.StatusRemovingExclusions, true, 92);
            await _defenderExclusionService.RemoveExclusionsAsync(
                state.Exclusions
                    .Where(exclusion => exclusion.AddedByInstaller)
                    .Select(exclusion => exclusion.Path));

            InstallStateStore.Delete(installPath);

            SetBusyState(false, TextId.UninstallCompleteStatus, false, 100);

            MessageBox.Show(
                this,
                Format(TextId.UninstallCompleteDialogFormat, installPath),
                GetText(TextId.UninstallCompleteTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var errorMessage = GetErrorMessage(ex);
            SetBusyState(false, TextId.UninstallFailedPrefix, false, _progressBar.Value, errorMessage);

            MessageBox.Show(
                this,
                Format(TextId.UninstallFailedDialogFormat, errorMessage),
                GetText(TextId.UninstallFailedTitle),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateControlState();
        }
    }

    private void UpdateProgress(InstallProgress progress)
    {
        SetStatus(progress.StatusTextId, progress.IsIndeterminate, (int)Math.Round(progress.Percent), progress.Arguments);
    }

    private bool PromptForManualAntivirusExclusions(string installPath)
    {
        return MessageBox.Show(
            this,
            Format(TextId.DefenderUnavailableDialogFormat, installPath),
            GetText(TextId.DefenderUnavailableTitle),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void SetBusyState(bool isBusy, TextId statusTextId, bool indeterminate, int progressPercent, params object[] args)
    {
        _isBusy = isBusy;
        SetStatus(statusTextId, indeterminate, progressPercent, args);
        UpdateControlState();
    }

    private void SetStatus(TextId statusTextId, bool indeterminate, int progressPercent, params object[] args)
    {
        _statusTextId = statusTextId;
        _statusArgs = args;
        _statusLabel.Text = Format(statusTextId, args);

        if (indeterminate)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
        else
        {
            if (_progressBar.Style != ProgressBarStyle.Continuous)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
            }

            _progressBar.Value = Math.Clamp(progressPercent, 0, 100);
        }
    }

    private void UpdateControlState()
    {
        var hasPath = !string.IsNullOrWhiteSpace(_pathTextBox.Text);
        var canInstall = !_isBusy && hasPath && _currentRelease is not null;
        var canUninstall = !_isBusy && hasPath && HasInstallStateForSelectedPath();

        _languageComboBox.Enabled = !_isBusy;
        _fullRadioButton.Enabled = !_isBusy;
        _patchRadioButton.Enabled = !_isBusy;
        _pathTextBox.Enabled = !_isBusy;
        _browseButton.Enabled = !_isBusy;
        _refreshButton.Enabled = !_isBusy;
        _installButton.Enabled = canInstall;
        _uninstallButton.Enabled = canUninstall;
        _exitButton.Enabled = !_isBusy;
    }

    private bool HasInstallStateForSelectedPath()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_pathTextBox.Text))
            {
                return false;
            }

            return InstallStateStore.Exists(Path.GetFullPath(_pathTextBox.Text.Trim()));
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadAssetAsync(ReleaseAsset asset, string destinationPath, IProgress<InstallProgress> progress, CancellationToken cancellationToken = default)
    {
        progress.Report(new InstallProgress(0, true, TextId.StatusDownloadStartingFormat, asset.Name));

        using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);

            if (bytesRead <= 0)
            {
                break;
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            if (totalBytes is > 0)
            {
                var ratio = totalBytesRead / (double)totalBytes.Value;
                progress.Report(new InstallProgress(
                    ratio * 80d,
                    false,
                    TextId.StatusDownloadingFormat,
                    asset.Name,
                    ratio));
            }
            else
            {
                progress.Report(new InstallProgress(0, true, TextId.StatusDownloadingUnknownFormat, asset.Name));
            }
        }

        progress.Report(new InstallProgress(80, false, TextId.StatusDownloadComplete));
    }

    private static Task<ExtractedContent> ExtractArchiveAsync(string archivePath, string installPath, InstallState? existingState, IProgress<InstallProgress> progress, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var totalEntries = Math.Max(archive.Entries.Count, 1);
            var installRoot = EnsureTrailingSeparator(Path.GetFullPath(installPath));
            var extractedFiles = new List<string>();
            var extractedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var backedUpFiles = new List<BackedUpFile>();
            var trackedBackups = new HashSet<string>(
                existingState?.BackedUpFiles?.Select(entry => NormalizeRelativeStatePath(entry.OriginalRelativePath)) ?? [],
                StringComparer.OrdinalIgnoreCase);

            try
            {
                for (var index = 0; index < archive.Entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = archive.Entries[index];
                    var destinationPath = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
                    var relativeEntryPath = NormalizeRelativeStatePath(Path.GetRelativePath(installPath, destinationPath));

                    if (!destinationPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InstallerAppException(InstallerErrorKind.InvalidArchivePath);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                        AddTrackedDirectory(extractedDirectories, installPath, destinationPath);
                    }
                    else
                    {
                        var destinationDirectory = Path.GetDirectoryName(destinationPath);

                        if (!string.IsNullOrWhiteSpace(destinationDirectory))
                        {
                            Directory.CreateDirectory(destinationDirectory);
                            AddTrackedDirectory(extractedDirectories, installPath, destinationDirectory);
                        }

                        BackupOriginalLauncherIfNeeded(installPath, relativeEntryPath, destinationPath, trackedBackups, backedUpFiles);
                        entry.ExtractToFile(destinationPath, overwrite: true);
                        extractedFiles.Add(destinationPath);
                    }

                    var percent = 80d + ((index + 1) / (double)totalEntries * 20d);
                    progress.Report(new InstallProgress(percent, false, TextId.StatusExtractingFormat, index + 1, totalEntries));
                }

                return new ExtractedContent(extractedFiles, extractedDirectories.ToArray(), backedUpFiles);
            }
            catch
            {
                foreach (var backedUpFile in backedUpFiles)
                {
                    TryRestoreBackedUpFile(installRoot, backedUpFile);
                }

                throw;
            }
        }, cancellationToken);
    }

    private static Task RemoveInstalledContentAsync(string installPath, InstallState state, IProgress<InstallProgress> progress, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var installRoot = EnsureTrailingSeparator(Path.GetFullPath(installPath));
            var filePaths = state.InstalledFiles
                .Select(relativePath => ResolveTrackedPath(installRoot, relativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var directoryPaths = CollectDirectoryPaths(state, installRoot);
            var backedUpFiles = state.BackedUpFiles ?? [];
            var totalSteps = Math.Max(filePaths.Length + backedUpFiles.Count + directoryPaths.Count, 1);
            var completedSteps = 0;

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDeleteFile(filePath);
                completedSteps++;

                progress.Report(new InstallProgress(
                    completedSteps / (double)totalSteps * 90d,
                    false,
                    TextId.StatusRemovingInstalledFilesFormat,
                    completedSteps,
                    totalSteps));
            }

            foreach (var backedUpFile in backedUpFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RestoreBackedUpFile(installRoot, backedUpFile);
                completedSteps++;

                progress.Report(new InstallProgress(
                    completedSteps / (double)totalSteps * 90d,
                    false,
                    TextId.StatusRemovingInstalledFilesFormat,
                    completedSteps,
                    totalSteps));
            }

            foreach (var directoryPath in directoryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(directoryPath) &&
                    !Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath, recursive: false);
                }

                completedSteps++;

                progress.Report(new InstallProgress(
                    completedSteps / (double)totalSteps * 90d,
                    false,
                    TextId.StatusRemovingInstalledFilesFormat,
                    completedSteps,
                    totalSteps));
            }
        }, cancellationToken);
    }

    private void PopulateLanguageList()
    {
        _isApplyingLanguage = true;
        _languageComboBox.Items.AddRange(LocalizationCatalog.SupportedLanguages.ToArray());
        _languageComboBox.SelectedItem = LocalizationCatalog.SupportedLanguages.First(language => language.Code == _currentLanguage.Code);
        _isApplyingLanguage = false;
    }

    private void LanguageComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isApplyingLanguage || _languageComboBox.SelectedItem is not InstallerLanguage language)
        {
            return;
        }

        ApplyLanguage(language);
    }

    private void ApplyLanguage(InstallerLanguage language)
    {
        _currentLanguage = language;
        _currentCulture = LocalizationCatalog.GetCulture(language);

        _isApplyingLanguage = true;
        _languageComboBox.SelectedItem = LocalizationCatalog.SupportedLanguages.First(item => item.Code == language.Code);
        _isApplyingLanguage = false;

        Text = GetText(TextId.AppTitle);
        _titleLabel.Text = GetText(TextId.AppTitle);
        _introLabel.Text = GetText(TextId.Intro);
        _languageLabel.Text = GetText(TextId.LanguageLabel);
        _releaseGroup.Text = GetText(TextId.ReleaseGroupTitle);
        _modeGroup.Text = GetText(TextId.InstallTypeGroupTitle);
        _fullRadioButton.Text = GetText(TextId.FullInstall);
        _patchRadioButton.Text = GetText(TextId.PatchUpdate);
        _destinationGroup.Text = GetText(TextId.DestinationGroupTitle);
        _destinationLabel.Text = GetText(TextId.DestinationLabel);
        _browseButton.Text = GetText(TextId.Browse);
        _refreshButton.Text = GetText(TextId.Refresh);
        _installButton.Text = GetText(TextId.Install);
        _uninstallButton.Text = GetText(TextId.Uninstall);
        _exitButton.Text = GetText(TextId.Exit);

        UpdateModeDescription();
        UpdateReleaseText();
        _statusLabel.Text = Format(_statusTextId, _statusArgs);
    }

    private void UpdateModeDescription()
    {
        _modeLabel.Text = SelectedMode == InstallMode.Full
            ? GetText(TextId.FullDescription)
            : GetText(TextId.PatchDescription);
    }

    private void UpdateReleaseText()
    {
        switch (_releaseUiState)
        {
            case ReleaseUiState.Ready when _currentRelease is not null:
                _releaseLabel.Text = Format(TextId.ReleaseLatestFormat, _currentRelease.TagName);
                _releaseDetailsLabel.Text = Format(
                    TextId.ReleaseDetailsFormat,
                    _currentRelease.Title,
                    _currentRelease.PublishedAt.LocalDateTime,
                    FormatSize(_currentRelease.FullAsset.SizeBytes),
                    FormatSize(_currentRelease.PatchAsset.SizeBytes));
                break;

            case ReleaseUiState.Failed:
                _releaseLabel.Text = GetText(TextId.ReleaseLoadFailed);
                _releaseDetailsLabel.Text = GetText(TextId.ReleaseRetry);
                break;

            default:
                _releaseLabel.Text = GetText(TextId.ReleaseChecking);
                _releaseDetailsLabel.Text = GetText(TextId.ReleaseWait);
                break;
        }
    }

    private string GetErrorMessage(Exception exception)
    {
        return exception switch
        {
            InstallerAppException { Kind: InstallerErrorKind.InvalidArchivePath } => GetText(TextId.InvalidArchivePath),
            InstallerAppException { Kind: InstallerErrorKind.GitHubEmptyPayload } => GetText(TextId.GitHubEmptyPayload),
            InstallerAppException { Kind: InstallerErrorKind.GitHubMissingAssets } => GetText(TextId.GitHubMissingAssets),
            InstallerAppException { Kind: InstallerErrorKind.InvalidInstallState } => GetText(TextId.InvalidInstallState),
            InstallerAppException { Kind: InstallerErrorKind.InvalidInstallStatePath } => GetText(TextId.InvalidInstallStatePath),
            _ => exception.Message
        };
    }

    private string GetText(TextId id)
    {
        return LocalizationCatalog.Get(_currentLanguage, id);
    }

    private string Format(TextId id, params object[] args)
    {
        return string.Format(_currentCulture, GetText(id), args);
    }

    private static InstallState BuildInstallState(
        string installPath,
        InstallState? existingState,
        string releaseTag,
        ExtractedContent extractedContent,
        IReadOnlyList<ManagedExclusion> exclusions)
    {
        var installedFiles = new HashSet<string>(existingState?.InstalledFiles ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in extractedContent.Files)
        {
            installedFiles.Add(NormalizeRelativeStatePath(Path.GetRelativePath(installPath, filePath)));
        }

        var installedDirectories = new HashSet<string>(existingState?.InstalledDirectories ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var directoryPath in extractedContent.Directories)
        {
            var relativePath = NormalizeRelativeStatePath(Path.GetRelativePath(installPath, directoryPath));

            if (!string.IsNullOrEmpty(relativePath))
            {
                installedDirectories.Add(relativePath);
            }
        }

        foreach (var relativeFilePath in installedFiles)
        {
            var parentDirectory = Path.GetDirectoryName(relativeFilePath);

            while (!string.IsNullOrWhiteSpace(parentDirectory) && parentDirectory != ".")
            {
                installedDirectories.Add(NormalizeRelativeStatePath(parentDirectory));
                parentDirectory = Path.GetDirectoryName(parentDirectory);
            }
        }

        var exclusionMap = new Dictionary<string, ManagedExclusion>(StringComparer.OrdinalIgnoreCase);

        if (existingState is not null)
        {
            foreach (var exclusion in existingState.Exclusions)
            {
                exclusionMap[exclusion.Path] = exclusion;
            }
        }

        foreach (var exclusion in exclusions)
        {
            if (exclusionMap.TryGetValue(exclusion.Path, out var existingExclusion) && existingExclusion.AddedByInstaller)
            {
                exclusionMap[exclusion.Path] = new ManagedExclusion(exclusion.Path, true);
            }
            else
            {
                exclusionMap[exclusion.Path] = exclusion;
            }
        }

        var backupMap = new Dictionary<string, BackedUpFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var backedUpFile in existingState?.BackedUpFiles ?? [])
        {
            backupMap[NormalizeRelativeStatePath(backedUpFile.OriginalRelativePath)] = new BackedUpFile(
                NormalizeRelativeStatePath(backedUpFile.OriginalRelativePath),
                NormalizeRelativeStatePath(backedUpFile.BackupRelativePath));
        }

        foreach (var backedUpFile in extractedContent.BackedUpFiles)
        {
            backupMap[NormalizeRelativeStatePath(backedUpFile.OriginalRelativePath)] = new BackedUpFile(
                NormalizeRelativeStatePath(backedUpFile.OriginalRelativePath),
                NormalizeRelativeStatePath(backedUpFile.BackupRelativePath));
        }

        return new InstallState(
            releaseTag,
            DateTimeOffset.UtcNow,
            installedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            installedDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            exclusionMap.Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            backupMap.Values.OrderBy(entry => entry.OriginalRelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<string> CollectDirectoryPaths(InstallState state, string installRoot)
    {
        var directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativeDirectoryPath in state.InstalledDirectories)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectoryPath))
            {
                continue;
            }

            directoryPaths.Add(ResolveTrackedPath(installRoot, relativeDirectoryPath));
        }

        foreach (var relativeFilePath in state.InstalledFiles)
        {
            var relativeDirectory = Path.GetDirectoryName(relativeFilePath);

            while (!string.IsNullOrWhiteSpace(relativeDirectory) && relativeDirectory != ".")
            {
                directoryPaths.Add(ResolveTrackedPath(installRoot, relativeDirectory));
                relativeDirectory = Path.GetDirectoryName(relativeDirectory);
            }
        }

        return directoryPaths
            .OrderByDescending(path => path.Length)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveTrackedPath(string installRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(installRoot, relativePath));

        if (!fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallerAppException(InstallerErrorKind.InvalidInstallStatePath);
        }

        return fullPath;
    }

    private static string NormalizeRelativeStatePath(string relativePath)
    {
        if (relativePath == ".")
        {
            return string.Empty;
        }

        return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static void AddTrackedDirectory(ISet<string> directories, string installPath, string directoryPath)
    {
        var relativePath = NormalizeRelativeStatePath(Path.GetRelativePath(installPath, directoryPath));

        if (!string.IsNullOrEmpty(relativePath))
        {
            directories.Add(Path.GetFullPath(directoryPath));
        }
    }

    private static void BackupOriginalLauncherIfNeeded(
        string installPath,
        string relativeEntryPath,
        string destinationPath,
        ISet<string> trackedBackups,
        ICollection<BackedUpFile> backedUpFiles)
    {
        if (!string.Equals(relativeEntryPath, "csgo.exe", StringComparison.OrdinalIgnoreCase) ||
            trackedBackups.Contains(relativeEntryPath))
        {
            return;
        }

        var backupPath = Path.Combine(installPath, "csgo.exe.old");

        if (!File.Exists(destinationPath))
        {
            return;
        }

        File.Move(destinationPath, backupPath);
        trackedBackups.Add(relativeEntryPath);
        backedUpFiles.Add(new BackedUpFile(relativeEntryPath, "csgo.exe.old"));
    }

    private static void RestoreBackedUpFile(string installRoot, BackedUpFile backedUpFile)
    {
        var originalPath = ResolveTrackedPath(installRoot, backedUpFile.OriginalRelativePath);
        var backupPath = ResolveTrackedPath(installRoot, backedUpFile.BackupRelativePath);

        if (!File.Exists(backupPath))
        {
            return;
        }

        var originalDirectory = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrWhiteSpace(originalDirectory))
        {
            Directory.CreateDirectory(originalDirectory);
        }

        if (File.Exists(originalPath))
        {
            TryDeleteFile(originalPath);
        }

        File.Move(backupPath, originalPath);
    }

    private static void TryRestoreBackedUpFile(string installRoot, BackedUpFile backedUpFile)
    {
        try
        {
            RestoreBackedUpFile(installRoot, backedUpFile);
        }
        catch
        {
        }
    }

    private async Task TryRollbackExclusionsAsync(IEnumerable<string> paths)
    {
        try
        {
            await _defenderExclusionService.RemoveExclusionsAsync(paths);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
        }

        File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private string FormatSize(long sizeBytes)
    {
        var sizeInMegabytes = sizeBytes / 1024d / 1024d;
        return string.Format(_currentCulture, "{0:0.#} MB", sizeInMegabytes);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BetterOffensiveInstaller/1.0");
        return client;
    }

    private enum ReleaseUiState
    {
        Loading,
        Ready,
        Failed
    }

    private sealed record ExtractedContent(IReadOnlyList<string> Files, IReadOnlyList<string> Directories, IReadOnlyList<BackedUpFile> BackedUpFiles);
    private readonly record struct InstallProgress(double Percent, bool IsIndeterminate, TextId StatusTextId, params object[] Arguments);
}
