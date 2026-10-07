namespace BulkRename.Core;

public sealed record RenamePlan(string SourcePath, string NewName, bool IsDirectory)
{
    public string DestinationPath => Path.Combine(Path.GetDirectoryName(SourcePath)!, NewName);
}
