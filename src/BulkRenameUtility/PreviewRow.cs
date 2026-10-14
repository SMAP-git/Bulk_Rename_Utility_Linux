using System.ComponentModel;
using System.Runtime.CompilerServices;
using BulkRename.Core;

namespace BulkRenameUtility;

public sealed class PreviewRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _newName = string.Empty;
    private string _status = "Ready";
    private string _statusKey = "ready";
    private string _folderTypeLabel = "Folder";
    private string _fileTypeLabel = "file";

    public PreviewRow(string path, bool isDirectory, long size, DateTime modified, FileDateTimes? dates = null)
    {
        Path = path;
        IsDirectory = isDirectory;
        Size = size;
        Modified = modified;
        Dates = dates;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsDirectory { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public FileDateTimes? Dates { get; }
    public string SizeDisplay => IsDirectory ? "—" : FormatSize(Size);
    public string TypeDisplay => IsDirectory
        ? _folderTypeLabel
        : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() + " " + _fileTypeLabel;
    public string NewName
    {
        get => _newName;
        set
        {
            if (_newName == value)
            {
                return;
            }

            _newName = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    public string StatusKey => _statusKey;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetStatus(string key, string text)
    {
        _statusKey = key;
        Status = text;
    }

    public void LocalizeLabels(string folderTypeLabel, string fileTypeLabel, Func<string, string> translate)
    {
        _folderTypeLabel = folderTypeLabel;
        _fileTypeLabel = fileTypeLabel;
        OnPropertyChanged(nameof(TypeDisplay));
        Status = translate(_statusKey);
    }

    private static string FormatSize(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024d:0.#} KB",
        < 1024L * 1024 * 1024 => $"{size / (1024d * 1024):0.#} MB",
        _ => $"{size / (1024d * 1024 * 1024):0.#} GB"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
