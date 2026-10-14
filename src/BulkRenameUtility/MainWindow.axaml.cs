using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BulkRename.Core;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace BulkRenameUtility;

public partial class MainWindow : Window
{
    private enum SortColumn
    {
        Name,
        NewName,
        Size,
        Modified,
        Type,
        Status
    }

    private readonly ObservableCollection<PreviewRow> _rows = [];
    private readonly ObservableCollection<FolderEntry> _folders = [];
    private readonly Dictionary<AutoCompleteBox, ObservableCollection<string>> _inputHistory = [];
    private readonly Dictionary<string, Dictionary<string, string>> _catalogues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PreviewRow> _allRows = [];
    private readonly HashSet<string> _typeFilter = new(StringComparer.OrdinalIgnoreCase);
    private string _languageCode = "en";
    private bool _ready;
    private bool _changingFolderList;
    private string _currentPath = string.Empty;
    private int _folderLoadVersion;
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortDescending;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Dispatcher.UIThread.Post(NarrowSpinnerButtons, DispatcherPriority.Loaded);
        FilesList.ItemsSource = _rows;
        FolderList.ItemsSource = _folders;
        InitializeInputHistory();
        LoadLanguageCatalogues();
        BuildLanguageMenu();
        ActualThemeVariantChanged += (_, _) => ApplyGap();
        var settings = LoadSettings();
        ApplyTheme(settings.Theme, save: false);
        ApplyLanguage("en");
        if (!string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase) && _catalogues.ContainsKey(settings.Language))
        {
            ApplyLanguage(settings.Language, save: false);
        }
        var initialPath = Environment.GetEnvironmentVariable("BULKRENAME_DEFAULT_PATH");
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        PathBox.Text = initialPath;
        _ready = true;
        UpdateSortHeaders();
        Opened += async (_, _) => await LoadFolderAsync(initialPath);
    }

    private async Task LoadFolderAsync(string path)
    {
        var loadVersion = ++_folderLoadVersion;
        var showHidden = ShowHiddenCheck.IsChecked == true;

        try
        {
            var result = await Task.Run<(string FullPath, List<PreviewRow> Entries)?>(() =>
            {
                if (!Directory.Exists(path))
                {
                    return null;
                }

                var fullPath = System.IO.Path.GetFullPath(path);
                var entries = Directory.EnumerateFileSystemEntries(fullPath)
                    .Where(fullEntryPath => showHidden
                        || !System.IO.Path.GetFileName(fullEntryPath).StartsWith('.'))
                    .Select(fullEntryPath =>
                    {
                        var isDirectory = Directory.Exists(fullEntryPath);
                        var info = isDirectory
                            ? (FileSystemInfo)new DirectoryInfo(fullEntryPath)
                            : new FileInfo(fullEntryPath);
                        return new PreviewRow(
                            fullEntryPath,
                            isDirectory,
                            isDirectory ? 0 : ((FileInfo)info).Length,
                            info.LastWriteTime,
                            new FileDateTimes(info.CreationTime, info.LastWriteTime, info.LastAccessTime));
                    })
                    .ToList();

                return (fullPath, entries);
            });

            if (loadVersion != _folderLoadVersion)
            {
                return;
            }

            if (result is null)
            {
                ShowMessage(string.Format(Tr("folderMissing"), path));
                return;
            }

            _currentPath = result.Value.FullPath;
            PathBox.Text = _currentPath;
            _allRows.Clear();
            foreach (var entry in result.Value.Entries)
            {
                entry.LocalizeLabels(Tr("folderType"), Tr("fileType"), key => Tr(key));
                _allRows.Add(entry);
            }

            BuildTypeFilter();
            ApplyTypeFilter();
            RefreshFolderList();
            RefreshPreview(this, new RoutedEventArgs());
            SetStatus(string.Format(Tr("folderLoaded"), _rows.Count, _currentPath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (loadVersion == _folderLoadVersion)
            {
                ShowMessage(string.Format(Tr("folderReadError"), error.Message));
            }
        }
    }

    private async void ShowHiddenChanged(object? sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            await LoadFolderAsync(_currentPath);
        }
    }

    private const string FolderTypeKey = "<folder>";
    private const string NoExtensionKey = "<none>";

    private static string TypeKey(PreviewRow row)
    {
        if (row.IsDirectory)
        {
            return FolderTypeKey;
        }

        var extension = System.IO.Path.GetExtension(row.Path).TrimStart('.');
        return extension.Length == 0 ? NoExtensionKey : extension.ToLowerInvariant();
    }

    private string TypeLabel(string key) => key switch
    {
        FolderTypeKey => Tr("folderType"),
        NoExtensionKey => Tr("noExtension"),
        _ => "." + key
    };

    private void BuildTypeFilter()
    {
        var present = _allRows.Select(TypeKey).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key == FolderTypeKey ? 0 : 1).ThenBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        _typeFilter.IntersectWith(present);
        TypeFilterPanel.Children.Clear();
        foreach (var key in present)
        {
            var box = new CheckBox { Content = TypeLabel(key), Tag = key, IsChecked = _typeFilter.Contains(key) };
            box.IsCheckedChanged += TypeFilterChanged;
            TypeFilterPanel.Children.Add(box);
        }

        UpdateTypeFilterText();
    }

    private void TypeFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string key } box)
        {
            return;
        }

        if (box.IsChecked == true)
        {
            _typeFilter.Add(key);
        }
        else
        {
            _typeFilter.Remove(key);
        }

        UpdateTypeFilterText();
        ApplyTypeFilter();
        RefreshPreview(this, new RoutedEventArgs());
    }

    private void TypeFilterClear(object? sender, RoutedEventArgs e)
    {
        _typeFilter.Clear();
        foreach (var box in TypeFilterPanel.Children.OfType<CheckBox>())
        {
            box.IsCheckedChanged -= TypeFilterChanged;
            box.IsChecked = false;
            box.IsCheckedChanged += TypeFilterChanged;
        }

        UpdateTypeFilterText();
        ApplyTypeFilter();
        RefreshPreview(this, new RoutedEventArgs());
    }

    private void UpdateTypeFilterText()
    {
        TypeAllButton.Content = Tr("allTypes");
        TypeFilterButton.Content = _typeFilter.Count == 0
            ? Tr("typesAll")
            : string.Format(Tr("typesSelected"), _typeFilter.Count);
    }

    private void ApplyTypeFilter()
    {
        _rows.Clear();
        var visible = _typeFilter.Count == 0 ? _allRows : _allRows.Where(row => _typeFilter.Contains(TypeKey(row)));
        foreach (var row in SortRows(visible))
        {
            _rows.Add(row);
        }
    }

    private void RefreshFolderList()
    {
        _changingFolderList = true;
        try
        {
            _folders.Clear();
            var parent = Directory.GetParent(_currentPath);
            if (parent is not null)
            {
                _folders.Add(new FolderEntry("..", parent.FullName));
            }

            foreach (var directory in Directory.EnumerateDirectories(_currentPath)
                         .Where(d => ShowHiddenCheck.IsChecked == true || !Path.GetFileName(d).StartsWith('.'))
                         .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                _folders.Add(new FolderEntry(Path.GetFileName(directory), directory));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowMessage(string.Format(Tr("folderListError"), error.Message));
        }
        finally
        {
            _changingFolderList = false;
        }
    }

    private RenameOptions ReadOptions()
    {
        return new RenameOptions
        {
            RegexPattern = RegexMatchBox.Text ?? string.Empty,
            RegexReplacement = RegexReplaceBox.Text ?? string.Empty,
            RegexIncludeExtension = RegexExtensionCheck.IsChecked == true,
            RegexIgnoreCase = RegexIgnoreCaseCheck.IsChecked == true,
            ReplaceText = ReplaceBox.Text ?? string.Empty,
            ReplaceWith = WithBox.Text ?? string.Empty,
            ReplaceIgnoreCase = ReplaceIgnoreCaseCheck.IsChecked != true,
            ReplaceFirstOnly = ReplaceFirstCheck.IsChecked == true,
            RemoveFirst = (int)(RemoveFirstBox.Value ?? 0),
            RemoveLast = (int)(RemoveLastBox.Value ?? 0),
            RemoveFrom = (int)(RemoveFromBox.Value ?? 0),
            RemoveTo = (int)(RemoveToBox.Value ?? 0),
            RemoveChars = RemoveCharsBox.Text ?? string.Empty,
            RemoveWords = RemoveWordsBox.Text ?? string.Empty,
            CropMode = SelectedComboValue(CropModeBox),
            CropText = CropTextBox.Text ?? string.Empty,
            RemoveDigits = RemoveDigitsCheck.IsChecked == true,
            RemoveHigh = RemoveHighCheck.IsChecked == true,
            RemoveDoubleSpaces = RemoveDoubleSpacesCheck.IsChecked == true,
            RemoveAccents = RemoveAccentsCheck.IsChecked == true,
            RemoveSymbols = RemoveSymbolsCheck.IsChecked == true,
            TrimSpaces = TrimSpacesCheck.IsChecked == true,
            TrimChars = TrimCharsCheck.IsChecked == true,
            LeadDots = SelectedComboValue(LeadDotsBox),
            CaseMode = SelectedComboValue(CaseModeBox) switch
            {
                "UPPERCASE" => "UPPERCASE",
                "lowercase" => "lowercase",
                "Title Case" => "Title Case",
                _ => "Same"
            },
            NameMode = SelectedComboValue(NameModeBox),
            NameValue = NameValueBox.Text ?? string.Empty,
            Prefix = PrefixBox.Text ?? string.Empty,
            Suffix = SuffixBox.Text ?? string.Empty,
            InsertText = InsertBox.Text ?? string.Empty,
            InsertPosition = (int)(InsertPosBox.Value ?? 0),
            FolderMode = SelectedComboValue(FolderModeBox),
            FolderSeparator = FolderSeparatorBox.Text ?? string.Empty,
            FolderLevels = (int)(FolderLevelsBox.Value ?? 1),
            NumberMode = SelectedComboValue(NumberModeBox),
            NumberAt = (int)(NumberAtBox.Value ?? 0),
            NumberStart = (int)(NumberStartBox.Value ?? 1),
            NumberIncrement = (int)(NumberIncrementBox.Value ?? 1),
            NumberPadding = (int)(NumberPaddingBox.Value ?? 0),
            NumberSeparator = NumberSeparatorBox.Text ?? string.Empty,
            NumberBreak = (int)(NumberBreakBox.Value ?? 0),
            NumberFolder = NumberFolderCheck.IsChecked == true,
            NumberType = SelectedComboValue(NumberTypeBox),
            NumberCase = SelectedComboValue(NumberCaseBox),
            ExtensionMode = SelectedComboValue(ExtensionModeBox) switch
            {
                "UPPERCASE" => "UPPERCASE",
                "lowercase" => "lowercase",
                var mode when mode is "Title" or "Fixed" or "Extra" or "Remove" => mode,
                _ => "Same"
            },
            ExtensionText = ExtensionTextBox.Text ?? string.Empty,
            DateMode = SelectedComboValue(DateModeBox),
            DateType = SelectedComboValue(DateTypeBox),
            DateFormat = SelectedComboValue(DateFormatBox),
            DateSeparator = DateSeparatorBox.Text ?? string.Empty,
            DateSegmentSeparator = DateSegmentBox.Text ?? string.Empty,
            DateCustomFormat = DateCustomBox.Text ?? string.Empty,
            DateCentury = DateCenturyCheck.IsChecked == true,
            DateOffsetDays = (int)(DateOffsetBox.Value ?? 0),
            DateInsertPosition = (int)(DateAtBox.Value ?? 0)
        };
    }

    private static string SelectedComboValue(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
        ?? (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
        ?? "None";

    private void RefreshPreview(object? sender, RoutedEventArgs e)
    {
        if (!_ready && sender is not MainWindow)
        {
            return;
        }

        try
        {
            var options = ReadOptions();
            var currentDate = DateTime.Now;
            var sequence = 0;
            string? lastDirectory = null;
            foreach (var row in _rows)
            {
                if (!row.IsSelected)
                {
                    row.NewName = string.Empty;
                    row.SetStatus("notSelected", Tr("notSelected"));
                    continue;
                }

                var dir = Path.GetDirectoryName(row.Path);
                if (options.NumberFolder && dir != lastDirectory)
                {
                    sequence = 0;
                    lastDirectory = dir;
                }

                sequence++;
                row.NewName = RenameEngine.Preview(row.Name, options, sequence, dir, row.Dates, currentDate);
                row.SetStatus(row.NewName == row.Name ? "unchanged" : "ready",
                    Tr(row.NewName == row.Name ? "unchanged" : "ready"));
            }

            ValidatePreviewDestinations();
            if (_sortColumn is SortColumn.NewName or SortColumn.Status)
            {
                ReorderRows();
            }

            SetStatus(string.Format(
                Tr("previewStatus"),
                _rows.Count,
                _rows.Count(row => row.IsSelected),
                Tr(SortColumnKey()),
                Tr(_sortDescending ? "descending" : "ascending")));
        }
        catch (Exception error) when (error is ArgumentException or FormatException or RegexMatchTimeoutException)
        {
            foreach (var row in _rows)
            {
                row.NewName = string.Empty;
                row.SetStatus("invalidOptions", Tr("invalidOptions"));
                row.LocalizeLabels(Tr("folderType"), Tr("fileType"), Tr);
            }

            if (_sortColumn is SortColumn.NewName or SortColumn.Status)
            {
                ReorderRows();
            }

            SetStatus(string.Format(Tr("previewError"), error.Message));
        }
    }

    private void OptionsChanged(object? sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            RefreshPreview(sender, e);
        }
    }

    private void ResetOptions(Action reset)
    {
        var wasReady = _ready;
        _ready = false;
        try
        {
            reset();
        }
        finally
        {
            _ready = wasReady;
        }

        OptionsChanged(this, new RoutedEventArgs());
    }

    private void ResetRegex(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        RegexMatchBox.Text = string.Empty;
        RegexReplaceBox.Text = string.Empty;
        RegexIgnoreCaseCheck.IsChecked = false;
        RegexExtensionCheck.IsChecked = false;
    });

    private void ResetNameCase(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        NameModeBox.SelectedIndex = 0;
        CaseModeBox.SelectedIndex = 0;
        NameValueBox.Text = string.Empty;
    });

    private void ResetExtension(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        ExtensionModeBox.SelectedIndex = 0;
        ExtensionTextBox.Text = string.Empty;
    });

    private void ResetReplace(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        ReplaceBox.Text = string.Empty;
        WithBox.Text = string.Empty;
        ReplaceIgnoreCaseCheck.IsChecked = false;
        ReplaceFirstCheck.IsChecked = false;
    });

    private void ResetAdd(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        PrefixBox.Text = string.Empty;
        InsertBox.Text = string.Empty;
        InsertPosBox.Value = 0;
        SuffixBox.Text = string.Empty;
    });

    private void ResetNumbering(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        NumberModeBox.SelectedIndex = 0;
        NumberAtBox.Value = 0;
        NumberStartBox.Value = 1;
        NumberIncrementBox.Value = 1;
        NumberPaddingBox.Value = 0;
        NumberSeparatorBox.Text = string.Empty;
        NumberBreakBox.Value = 0;
        NumberFolderCheck.IsChecked = false;
        NumberTypeBox.SelectedIndex = 8;
        NumberCaseBox.SelectedIndex = 0;
    });

    private void ResetFolder(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        FolderModeBox.SelectedIndex = 0;
        FolderSeparatorBox.Text = string.Empty;
        FolderLevelsBox.Value = 1;
    });

    private void ResetRemove(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        RemoveFirstBox.Value = 0;
        RemoveLastBox.Value = 0;
        RemoveFromBox.Value = 0;
        RemoveToBox.Value = 0;
        RemoveCharsBox.Text = string.Empty;
        RemoveWordsBox.Text = string.Empty;
        CropModeBox.SelectedIndex = 0;
        CropTextBox.Text = string.Empty;
        RemoveDigitsCheck.IsChecked = false;
        RemoveHighCheck.IsChecked = false;
        RemoveDoubleSpacesCheck.IsChecked = false;
        RemoveAccentsCheck.IsChecked = false;
        RemoveSymbolsCheck.IsChecked = false;
        TrimSpacesCheck.IsChecked = false;
        TrimCharsCheck.IsChecked = false;
        LeadDotsBox.SelectedIndex = 0;
    });

    private void ResetDate(object? sender, RoutedEventArgs e) => ResetOptions(() =>
    {
        DateModeBox.SelectedIndex = 0;
        DateTypeBox.SelectedIndex = 0;
        DateFormatBox.SelectedIndex = 0;
        DateAtBox.Value = 0;
        DateSeparatorBox.Text = string.Empty;
        DateSegmentBox.Text = string.Empty;
        DateCustomBox.Text = string.Empty;
        DateCenturyCheck.IsChecked = false;
        DateOffsetBox.Value = 0;
    });

    private void LoadLanguageCatalogues()
    {
        var languagesPath = Path.Combine(AppContext.BaseDirectory, "Languages");
        if (!Directory.Exists(languagesPath))
        {
            throw new DirectoryNotFoundException($"Language catalogues were not found: {languagesPath}");
        }

        foreach (var file in Directory.EnumerateFiles(languagesPath, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            if (translations is null || !translations.ContainsKey("language"))
            {
                throw new InvalidDataException($"Language catalogue is invalid: {file}");
            }

            _catalogues.Add(code, translations);
        }

        if (!_catalogues.ContainsKey("en"))
        {
            throw new FileNotFoundException(
                "The English language catalogue is required.",
                Path.Combine(languagesPath, "en.json"));
        }
    }

    private void BuildLanguageMenu()
    {
        foreach (var (code, translations) in _catalogues.OrderBy(
                     entry => entry.Value["language"],
                     StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem { Header = translations["language"], Tag = code };
            item.Click += (_, _) => ApplyLanguage(code, save: true);
            LanguageMenu.Items.Add(item);
        }
    }

    private sealed class AppSettings
    {
        public string Language { get; set; } = "en";
        public string Theme { get; set; } = "system";
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "bulk-rename-utility",
        "settings.json");

    private static AppSettings LoadSettings()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(
                SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(new AppSettings { Language = _languageCode, Theme = _themeMode }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowMessage(error.Message);
        }
    }

    private string _themeMode = "system";

    private void ThemeSystem(object? sender, RoutedEventArgs e) => ApplyTheme("system", save: true);

    private void ThemeLight(object? sender, RoutedEventArgs e) => ApplyTheme("light", save: true);

    private void ThemeDark(object? sender, RoutedEventArgs e) => ApplyTheme("dark", save: true);

    private void ApplyGap()
    {
        var gap = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? 4 : 6;
        ToolbarGrid.Margin = new Thickness(gap);
        PanelsGrid.Margin = new Thickness(gap, 0);
        PanelsGrid.ColumnDefinitions[1].Width = new GridLength(gap);
        ColumnSplitter.Width = gap;
        RootGrid.RowDefinitions[3].Height = new GridLength(gap);
        RowSplitter.Height = gap;
        RowSplitter.Margin = new Thickness(gap, 0);
        OptionsScroll.Margin = new Thickness(gap, 0, gap, gap);
        OptionsGrid.ColumnSpacing = gap;
        OptionsGrid.RowSpacing = gap;
    }

    private void ApplyTheme(string mode, bool save)
    {
        _themeMode = mode is "light" or "dark" ? mode : "system";
        RequestedThemeVariant = _themeMode switch
        {
            "light" => Avalonia.Styling.ThemeVariant.Light,
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
        ApplyGap();
        if (save)
        {
            SaveSettings();
        }
    }

    private static string AppVersion =>
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;

    private void ApplyLanguage(string code, bool save = false)
    {
        if (!_catalogues.ContainsKey(code))
        {
            ShowMessage($"Language catalogue is unavailable: {code}");
            return;
        }

        _languageCode = code;
        if (save)
        {
            SaveSettings();
        }

        Title = Tr("appTitle");
        ThemeMenu.Header = Tr("themeMenu");
        ToolTip.SetTip(SettingsButton, Tr("settings"));
        ThemeSystemItem.Header = Tr("themeSystem");
        ThemeLightItem.Header = Tr("themeLight");
        ThemeDarkItem.Header = Tr("themeDark");
        HelpMenu.Header = Tr("helpMenu");
        LanguageMenu.Header = Tr("languageMenu");
        AboutMenuItem.Header = Tr("about");
        AppTitleText.Text = Tr("appTitle");
        AppSubtitleText.Text = Tr("subtitle");
        SafePreviewText.Text = $"v{AppVersion}";
        ParentButton.Content = Tr("parent");
        BrowseButton.Content = Tr("browse");
        RefreshButton.Content = Tr("refresh");
        ShowHiddenCheck.Content = Tr("showHidden");
        if (_allRows.Count > 0)
        {
            BuildTypeFilter();
        }
        else
        {
            UpdateTypeFilterText();
        }
        FoldersTitle.Text = Tr("folders");
        RegexTitle.Text = $"1  {Tr("regex")}";
        ReplaceTitle.Text = $"4  {Tr("replaceText")}";
        NameCaseTitle.Text = $"2  {Tr("nameCase")}";
        AddRemoveTitle.Text = $"8  {Tr("removeSection")}";
        FirstNLabel.Text = Tr("firstN");
        LastNLabel.Text = Tr("lastN");
        FromLabel.Text = Tr("removeFrom");
        ToLabel.Text = Tr("removeTo");
        NumberingTitle.Text = $"6  {Tr("numbering")}";
        NumberModeLabel.Text = Tr("mode");
        NumberAtLabel.Text = Tr("at");
        NumberStartLabel.Text = Tr("start");
        NumberIncrLabel.Text = Tr("incr");
        NumberPadLabel.Text = Tr("pad");
        NumberSepLabel.Text = Tr("sep");
        NumberBreakLabel.Text = Tr("break");
        NumberFolderLabel.Text = Tr("folder");
        NumberTypeLabel.Text = Tr("typeLabel");
        NumberCaseLabel.Text = Tr("caseLabel");
        ExtensionTitle.Text = $"3  {Tr("extension")}";
        FolderTitle.Text = $"7  {Tr("appendFolder")}";
        FolderNameLabel.Text = Tr("name");
        FolderSepLabel.Text = Tr("folderSep");
        FolderLevelsLabel.Text = Tr("folderLevels");
        LocalizeCombo(FolderModeBox, ["none", "prefix", "suffix"]);
        AddTitle.Text = $"5  {Tr("add")}";
        PrefixLabel.Text = Tr("prefix");
        InsertLabel.Text = Tr("insert");
        InsertPosLabel.Text = Tr("atPos");
        SuffixLabel.Text = Tr("suffix");
        AutoDateTitle.Text = $"9  {Tr("autoDate")}";
        DateModeLabel.Text = Tr("mode");
        DateTypeLabel.Text = Tr("typeLabel");
        DateFormatLabel.Text = Tr("dateFormat");
        DateAtLabel.Text = Tr("atPos");
        DateSeparatorLabel.Text = Tr("sep");
        DateSegmentLabel.Text = Tr("dateSegment");
        DateCustomLabel.Text = Tr("dateCustom");
        DateCenturyLabel.Text = Tr("dateCentury");
        Avalonia.Automation.AutomationProperties.SetName(DateCenturyCheck, Tr("dateCentury"));
        DateOffsetLabel.Text = Tr("dateOffset");
        ApplyOptionTooltips();
        ToolTip.SetTip(DateCustomBox, Tr("dateCustomTip"));
        ToolTip.SetTip(DateSeparatorBox, Tr("dateSeparatorTip"));
        ToolTip.SetTip(DateSegmentBox, Tr("dateSegmentTip"));
        ToolTip.SetTip(DateCenturyCheck, Tr("dateCenturyTip"));
        ToolTip.SetTip(DateCenturyLabel, Tr("dateCenturyTip"));
        ToolTip.SetTip(DateTypeBox, Tr("dateTypeTip"));
        LocalizeCombo(DateModeBox, ["none", "prefix", "suffix", "replaceText"]);
        LocalizeCombo(DateTypeBox, ["dateCreated", "dateModified", "dateAccessed", "dateCurrent"]);
        if (DateFormatBox.Items[11] is ComboBoxItem customDateItem)
        {
            customDateItem.Content = Tr("dateCustom");
        }

        Button[] resetButtons =
        [
            ResetRegexButton, ResetNameCaseButton, ResetExtensionButton, ResetReplaceButton,
            ResetAddButton, ResetNumberingButton, ResetFolderButton, ResetRemoveButton, ResetDateButton
        ];
        TextBlock[] sectionTitles =
        [
            RegexTitle, NameCaseTitle, ExtensionTitle, ReplaceTitle,
            AddTitle, NumberingTitle, FolderTitle, AddRemoveTitle, AutoDateTitle
        ];
        for (var index = 0; index < resetButtons.Length; index++)
        {
            var description = string.Format(Tr("resetSection"), sectionTitles[index].Text);
            ToolTip.SetTip(resetButtons[index], description);
            Avalonia.Automation.AutomationProperties.SetName(resetButtons[index], description);
        }

        PreviewSafetyTitle.Text = Tr("previewSafety");
        PreviewSafetyText.Text = Tr("onlyChecked");
        PreviewTipText.Text = Tr("safetyTip");
        SelectAllButton.Content = Tr("selectAll");
        ClearSelectionButton.Content = Tr("clearSelection");
        RenameButton.Content = Tr("renameSelected");

        PathBox.Watermark = Tr("folderPath");
        RegexMatchBox.Watermark = Tr("matchPattern");
        RegexReplaceBox.Watermark = Tr("replaceWith");
        RegexIgnoreCaseCheck.Content = Tr("ignoreCase");
        ReplaceBox.Watermark = Tr("find");
        WithBox.Watermark = Tr("replaceWith");
        ReplaceIgnoreCaseCheck.Content = Tr("matchCase");
        ReplaceFirstCheck.Content = Tr("firstOnly");
        NameValueBox.Watermark = Tr("nameValue");
        ExtensionTextBox.Watermark = Tr("extensionText");
        RemoveCharsBox.Watermark = Tr("removeChars");
        RemoveWordsBox.Watermark = Tr("removeWords");
        CropLabel.Text = Tr("crop");
        LeadDotsLabel.Text = Tr("leadDots");
        RemoveDigitsCheck.Content = Tr("digits");
        RemoveHighCheck.Content = Tr("high");
        RemoveDoubleSpacesCheck.Content = Tr("doubleSpaces");
        RemoveAccentsCheck.Content = Tr("accents");
        RemoveSymbolsCheck.Content = Tr("symbols");
        TrimSpacesCheck.Content = Tr("trim");
        TrimCharsCheck.Content = Tr("trimChars");
        LocalizeCombo(CropModeBox, ["cropBefore", "cropAfter"]);
        LocalizeCombo(LeadDotsBox, ["dotsNone", "dotsOne", "dotsAll"]);
        NumberSeparatorBox.Watermark = Tr("separator");
        RegexExtensionCheck.Content = Tr("includeExtension");

        LocalizeCombo(NameModeBox, ["keep", "fixed", "prefix", "suffix"]);
        LocalizeCombo(CaseModeBox, ["sameCase", "uppercase", "lowercase", "titleCase"]);
        LocalizeCombo(NumberModeBox, ["none", "prefix", "suffix", "preSuffix", "insert"]);
        LocalizeNumberTypeCombo();
        LocalizeCombo(NumberCaseBox, ["same", "lower", "upper"]);
        LocalizeCombo(ExtensionModeBox, ["same", "lower", "upper", "title", "fixed", "extra", "remove"]);
        UpdateSortHeaders();
        foreach (var row in _rows)
        {
            row.LocalizeLabels(Tr("folderType"), Tr("fileType"), Tr);
        }

        RefreshPreview(this, new RoutedEventArgs());
    }

    private void ApplyOptionTooltips()
    {
        (Control Control, string Key)[] tooltips =
        [
            (RegexMatchBox, "regexMatchTip"), (RegexReplaceBox, "regexReplaceTip"),
            (RegexIgnoreCaseCheck, "regexIgnoreCaseTip"), (RegexExtensionCheck, "regexExtensionTip"),
            (NameModeBox, "nameModeTip"), (CaseModeBox, "caseModeTip"), (NameValueBox, "nameValueTip"),
            (ExtensionModeBox, "extensionModeTip"), (ExtensionTextBox, "extensionTextTip"),
            (ReplaceBox, "replaceFindTip"), (WithBox, "replaceWithTip"),
            (ReplaceIgnoreCaseCheck, "replaceMatchCaseTip"), (ReplaceFirstCheck, "replaceFirstTip"),
            (PrefixBox, "prefixTip"), (InsertBox, "insertTip"), (InsertPosBox, "insertPositionTip"),
            (SuffixBox, "suffixTip"), (NumberModeBox, "numberModeTip"), (NumberAtBox, "numberAtTip"),
            (NumberStartBox, "numberStartTip"), (NumberIncrementBox, "numberIncrementTip"),
            (NumberPaddingBox, "numberPaddingTip"), (NumberSeparatorBox, "numberSeparatorTip"),
            (NumberBreakBox, "numberBreakTip"), (NumberFolderCheck, "numberFolderTip"),
            (NumberTypeBox, "numberTypeTip"), (NumberCaseBox, "numberCaseTip"),
            (FolderModeBox, "folderModeTip"), (FolderSeparatorBox, "folderSeparatorTip"),
            (FolderLevelsBox, "folderLevelsTip"), (RemoveFirstBox, "removeFirstTip"),
            (RemoveLastBox, "removeLastTip"), (RemoveFromBox, "removeFromTip"),
            (RemoveToBox, "removeToTip"), (RemoveCharsBox, "removeCharsTip"),
            (RemoveWordsBox, "removeWordsTip"), (CropModeBox, "cropModeTip"),
            (CropTextBox, "cropTextTip"), (RemoveDigitsCheck, "removeDigitsTip"),
            (RemoveHighCheck, "removeHighTip"), (RemoveDoubleSpacesCheck, "removeDoubleSpacesTip"),
            (RemoveAccentsCheck, "removeAccentsTip"), (RemoveSymbolsCheck, "removeSymbolsTip"),
            (TrimSpacesCheck, "trimSpacesTip"), (TrimCharsCheck, "trimCharsTip"),
            (LeadDotsBox, "leadDotsTip"), (DateModeBox, "dateModeTip"),
            (DateFormatBox, "dateFormatTip"), (DateAtBox, "dateAtTip"),
            (DateOffsetBox, "dateOffsetTip")
        ];
        foreach (var (control, key) in tooltips)
        {
            ToolTip.SetTip(control, Tr(key));
        }
    }

    private string Tr(string key)
    {
        if (_catalogues.TryGetValue(_languageCode, out var selected) &&
            selected.TryGetValue(key, out var value))
        {
            return value;
        }

        return _catalogues.TryGetValue("en", out var english) && english.TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private void LocalizeNumberTypeCombo()
    {
        var keys = new Dictionary<string, string>
        {
            ["Base 2 (Binary)"] = "numberBase2",
            ["Base 8 (Octal)"] = "numberBase8",
            ["Base 10 (Decimal)"] = "numberBase10",
            ["Base 16 (Hex)"] = "numberBase16",
            ["Roman"] = "numberRoman",
            ["Hebrew"] = "numberHebrew"
        };

        var wasReady = _ready;
        _ready = false;
        var selected = Math.Max(NumberTypeBox.SelectedIndex, 0);
        NumberTypeBox.SelectedIndex = -1;
        foreach (var item in NumberTypeBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && keys.TryGetValue(tag, out var key))
            {
                item.Content = Tr(key);
            }
        }

        NumberTypeBox.SelectedIndex = selected;
        _ready = wasReady;
    }

    private void LocalizeCombo(ComboBox combo, IReadOnlyList<string> translationKeys)
    {
        var wasReady = _ready;
        _ready = false;
        var selected = Math.Max(combo.SelectedIndex, 0);
        combo.SelectedIndex = -1;
        for (var index = 0; index < combo.Items.Count && index < translationKeys.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem item)
            {
                item.Content = Tr(translationKeys[index]);
            }
        }

        combo.SelectedIndex = selected;
        _ready = wasReady;
    }

    private void InitializeInputHistory()
    {
        AutoCompleteBox[] historyFields =
        [
            RegexMatchBox,
            RegexReplaceBox,
            ReplaceBox,
            WithBox,
            NameValueBox,
            RemoveCharsBox,
            RemoveWordsBox,
            CropTextBox,
            PrefixBox,
            InsertBox,
            SuffixBox
        ];

        foreach (var field in historyFields)
        {
            var history = new ObservableCollection<string>();
            _inputHistory.Add(field, history);
            field.ItemsSource = history;
            field.FilterMode = AutoCompleteFilterMode.None;
            field.MinimumPrefixLength = 0;
            field.IsTextCompletionEnabled = false;
        }
    }

    private void RememberHistory(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox field ||
            !_inputHistory.TryGetValue(field, out var history) ||
            string.IsNullOrWhiteSpace(field.Text))
        {
            return;
        }

        var value = field.Text.Trim();
        var existingIndex = history.IndexOf(value);
        if (existingIndex >= 0)
        {
            history.RemoveAt(existingIndex);
        }

        history.Insert(0, value);
    }

    private void HistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is AutoCompleteBox field)
        {
            RememberHistory(field, new RoutedEventArgs());
            field.IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private void OpenHistory(AutoCompleteBox field)
    {
        if (_inputHistory.TryGetValue(field, out var history) && history.Count > 0)
        {
            field.IsDropDownOpen = true;
        }
    }

    private void OpenRegexMatchHistory(object? sender, RoutedEventArgs e) => OpenHistory(RegexMatchBox);

    private void OpenRegexReplaceHistory(object? sender, RoutedEventArgs e) => OpenHistory(RegexReplaceBox);

    private void OpenReplaceHistory(object? sender, RoutedEventArgs e) => OpenHistory(ReplaceBox);

    private void OpenWithHistory(object? sender, RoutedEventArgs e) => OpenHistory(WithBox);

    private void OpenNameValueHistory(object? sender, RoutedEventArgs e) => OpenHistory(NameValueBox);

    // The Fluent template hard-codes MinWidth=34 on the spinner buttons, so it must be overridden per instance.
    private void NarrowSpinnerButtons()
    {
        foreach (var button in this.GetVisualDescendants().OfType<RepeatButton>())
        {
            if (button.Name is "PART_IncreaseButton" or "PART_DecreaseButton")
            {
                button.MinWidth = 26;
                button.Width = 26;
            }
        }
    }

    private void NumberOptionChanged(object? sender, NumericUpDownValueChangedEventArgs e) => OptionsChanged(sender, e);

    
    private void OpenRemoveCharsHistory(object? sender, RoutedEventArgs e) => OpenHistory(RemoveCharsBox);

    private void OpenPrefixHistory(object? sender, RoutedEventArgs e) => OpenHistory(PrefixBox);

    private void OpenInsertHistory(object? sender, RoutedEventArgs e) => OpenHistory(InsertBox);

    private void OpenSuffixHistory(object? sender, RoutedEventArgs e) => OpenHistory(SuffixBox);

    private void OpenCropHistory(object? sender, RoutedEventArgs e) => OpenHistory(CropTextBox);

    private void OpenRemoveWordsHistory(object? sender, RoutedEventArgs e) => OpenHistory(RemoveWordsBox);

    private async void ApplyRenames(object? sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            ShowMessage(Tr("selectItemsToRename"));
            return;
        }

        try
        {
            var plans = selected.Select(row => new RenamePlan(row.Path, row.NewName, row.IsDirectory)).ToArray();
            RenameTransaction.Execute(plans);
            await LoadFolderAsync(_currentPath);
            ShowMessage(string.Format(Tr("renameComplete"), plans.Count(plan => Path.GetFileName(plan.SourcePath) != plan.NewName)));
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or AggregateException)
        {
            ShowMessage(string.Format(Tr("renameFailed"), error.Message));
        }
    }

    private void SelectAll(object? sender, RoutedEventArgs e)
    {
        SetSelection(true);
    }

    private void ClearSelection(object? sender, RoutedEventArgs e)
    {
        SetSelection(false);
    }

    private void ToggleAllSelection(object? sender, RoutedEventArgs e)
    {
        SetSelection(SelectAllCheck.IsChecked == true);
    }

    private void SetSelection(bool selected)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = selected;
        }

        SelectAllCheck.IsChecked = selected;
        RefreshPreview(this, new RoutedEventArgs());
    }

    private void SortByName(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.Name);

    private void SortByNewName(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.NewName);

    private void SortBySize(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.Size);

    private void SortByModified(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.Modified);

    private void SortByType(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.Type);

    private void SortByStatus(object? sender, RoutedEventArgs e) => ApplySort(SortColumn.Status);

    private void ApplySort(SortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        ReorderRows();

        UpdateSortHeaders();
        RefreshPreview(this, new RoutedEventArgs());
    }

    private void ReorderRows()
    {
        var sortedRows = SortRows(_rows).ToArray();
        _rows.Clear();
        foreach (var row in sortedRows)
        {
            _rows.Add(row);
        }
    }

    private IEnumerable<PreviewRow> SortRows(IEnumerable<PreviewRow> rows)
    {
        var folders = rows.Where(row => row.IsDirectory);
        var files = rows.Where(row => !row.IsDirectory);

        IOrderedEnumerable<PreviewRow> orderedFiles = (_sortColumn, _sortDescending) switch
        {
            (SortColumn.Name, false) => files.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Name, true) => files.OrderByDescending(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.NewName, false) => files.OrderBy(row => row.NewName, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.NewName, true) => files.OrderByDescending(row => row.NewName, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Size, false) => files.OrderBy(row => row.Size),
            (SortColumn.Size, true) => files.OrderByDescending(row => row.Size),
            (SortColumn.Modified, false) => files.OrderBy(row => row.Modified),
            (SortColumn.Modified, true) => files.OrderByDescending(row => row.Modified),
            (SortColumn.Type, false) => files.OrderBy(row => row.TypeDisplay, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Type, true) => files.OrderByDescending(row => row.TypeDisplay, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Status, false) => files.OrderBy(row => row.Status, StringComparer.CurrentCultureIgnoreCase),
            (SortColumn.Status, true) => files.OrderByDescending(row => row.Status, StringComparer.CurrentCultureIgnoreCase),
            _ => files.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return folders
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .Concat(orderedFiles.ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    private void UpdateSortHeaders()
    {
        NameHeader.Content = HeaderText(Tr("name"), SortColumn.Name);
        NewNameHeader.Content = HeaderText(Tr("newName"), SortColumn.NewName);
        SizeHeader.Content = HeaderText(Tr("size"), SortColumn.Size);
        ModifiedHeader.Content = HeaderText(Tr("modified"), SortColumn.Modified);
        TypeHeader.Content = HeaderText(Tr("type"), SortColumn.Type);
        StatusHeader.Content = HeaderText(Tr("status"), SortColumn.Status);
    }

    private string HeaderText(string label, SortColumn column)
    {
        if (_sortColumn != column)
        {
            return label;
        }

        return $"{label} {(_sortDescending ? "↓" : "↑")}";
    }

    private string SortColumnKey() => _sortColumn switch
    {
        SortColumn.Size => "sortSize",
        SortColumn.Modified => "sortModified",
        SortColumn.Type => "sortType",
        SortColumn.NewName => "sortNewName",
        SortColumn.Status => "sortStatus",
        _ => "sortName"
    };

    private PreviewRow? _selectionAnchor;

    private void RowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PreviewRow row } control
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<CheckBox>(true) is not null)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var wasReady = _ready;
        _ready = false;

        if (shift && _selectionAnchor is not null && _rows.Contains(_selectionAnchor))
        {
            var from = _rows.IndexOf(_selectionAnchor);
            var to = _rows.IndexOf(row);
            var (start, end) = from <= to ? (from, to) : (to, from);
            for (var i = 0; i < _rows.Count; i++)
            {
                var inRange = i >= start && i <= end;
                _rows[i].IsSelected = inRange || (ctrl && _rows[i].IsSelected);
            }
        }
        else if (ctrl)
        {
            row.IsSelected = !row.IsSelected;
            _selectionAnchor = row;
        }
        else
        {
            foreach (var other in _rows)
            {
                other.IsSelected = ReferenceEquals(other, row);
            }

            _selectionAnchor = row;
        }

        SelectAllCheck.IsChecked = _rows.Count > 0 && _rows.All(r => r.IsSelected);
        _ready = wasReady;
        e.Handled = true;
        RefreshPreview(this, new RoutedEventArgs());
    }

    private void RowSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            RefreshPreview(sender, e);
        }
    }

    private void ValidatePreviewDestinations()
    {
        var selected = _rows.Where(row => row.IsSelected && row.NewName != row.Name).ToArray();
        foreach (var row in selected)
        {
            if (string.IsNullOrWhiteSpace(row.NewName) ||
                row.NewName is "." or ".." ||
                row.NewName.IndexOfAny(['/', '\\', '\0']) >= 0)
            {
                row.SetStatus("invalid", Tr("invalid"));
                continue;
            }

            var destination = Path.Combine(_currentPath, row.NewName);
            var willMoveFromDestination = selected.Any(other =>
                string.Equals(other.Path, destination, StringComparison.Ordinal));
            if ((File.Exists(destination) || Directory.Exists(destination)) && !willMoveFromDestination)
            {
                row.SetStatus("destinationExists", Tr("destinationExists"));
            }
        }

        foreach (var group in selected
                     .Where(row => row.StatusKey != "invalid" && row.StatusKey != "destinationExists")
                     .GroupBy(row => row.NewName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var row in group)
            {
                row.SetStatus("duplicate", Tr("duplicate"));
            }
        }
    }

    private async void RefreshFolder(object? sender, RoutedEventArgs e)
    {
        await LoadFolderAsync(PathBox.Text ?? _currentPath);
    }

    private async void NavigateParent(object? sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            await LoadFolderAsync(parent.FullName);
        }
    }

    private async void BrowseFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to rename",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            await LoadFolderAsync(folders[0].Path.LocalPath);
        }
    }

    private async void FolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_changingFolderList && FolderList.SelectedItem is FolderEntry folder)
        {
            var path = folder.Path;
            FolderList.SelectedItem = null;
            await LoadFolderAsync(path);
        }
    }

    private async void PathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadFolderAsync(PathBox.Text ?? string.Empty);
        }
    }

    private void ShowAbout(object? sender, RoutedEventArgs e) =>
        ShowMessage($"{Tr("aboutDescription")}\n{Tr("aboutDeveloper")}\nv{AppVersion}", Tr("about"));

    private void SetStatus(string message)
    {
    }

    private void ShowMessage(string message, string? title = null)
    {
        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var dialog = new Window
        {
            Title = title ?? Title ?? "Bulk Rename Utility",
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 340,
            MaxWidth = 560,
            CanResize = false,
            RequestedThemeVariant = ActualThemeVariant,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    okButton
                }
            }
        };
        dialog.Bind(BackgroundProperty, this.GetResourceObservable("AppBg"));
        dialog.Bind(ForegroundProperty, this.GetResourceObservable("TitleFg"));
        okButton.Click += (_, _) => dialog.Close();
        _ = dialog.ShowDialog(this);
    }
}
