namespace BulkRename.Core;

public sealed class RenameOptions
{
    public string RegexPattern { get; init; } = string.Empty;
    public string RegexReplacement { get; init; } = string.Empty;
    public bool RegexIncludeExtension { get; init; }
    public bool RegexIgnoreCase { get; init; }
    public string ReplaceText { get; init; } = string.Empty;
    public string ReplaceWith { get; init; } = string.Empty;
    public bool ReplaceIgnoreCase { get; init; }
    public bool ReplaceFirstOnly { get; init; }
    public int RemoveFirst { get; init; }
    public int RemoveLast { get; init; }
    public int RemoveFrom { get; init; }
    public int RemoveTo { get; init; }
    public string RemoveChars { get; init; } = string.Empty;
    public string RemoveWords { get; init; } = string.Empty;
    public string CropMode { get; init; } = "Before";
    public string CropText { get; init; } = string.Empty;
    public bool RemoveDigits { get; init; }
    public bool RemoveHigh { get; init; }
    public bool RemoveDoubleSpaces { get; init; }
    public bool RemoveAccents { get; init; }
    public bool RemoveSymbols { get; init; }
    public bool TrimSpaces { get; init; }
    public bool TrimChars { get; init; }
    public string LeadDots { get; init; } = "None";
    public string CaseMode { get; init; } = "Same";
    public string NameMode { get; init; } = "Keep";
    public string NameValue { get; init; } = string.Empty;
    public string InsertText { get; init; } = string.Empty;
    public int InsertPosition { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public string FolderMode { get; init; } = "None";
    public string FolderSeparator { get; init; } = string.Empty;
    public int FolderLevels { get; init; } = 1;
    public string NumberMode { get; init; } = "None";
    public int NumberAt { get; init; }
    public int NumberStart { get; init; } = 1;
    public int NumberIncrement { get; init; } = 1;
    public int NumberPadding { get; init; }
    public string NumberSeparator { get; init; } = string.Empty;
    public int NumberBreak { get; init; }
    public bool NumberFolder { get; init; }
    public string NumberType { get; init; } = "Base 10 (Decimal)";
    public string NumberCase { get; init; } = "Same";
    public string ExtensionText { get; init; } = string.Empty;
    public string ExtensionMode { get; init; } = "Same";
}
