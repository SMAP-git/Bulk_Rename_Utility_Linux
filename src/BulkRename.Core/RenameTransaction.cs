namespace BulkRename.Core;

public static class RenameTransaction
{
    public static void Execute(IReadOnlyCollection<RenamePlan> requestedPlans)
    {
        ArgumentNullException.ThrowIfNull(requestedPlans);
        var plans = requestedPlans
            .Where(plan => !string.Equals(Path.GetFileName(plan.SourcePath), plan.NewName, StringComparison.Ordinal))
            .ToArray();
        if (plans.Length == 0)
        {
            return;
        }

        ValidatePlans(plans);
        var staged = new List<(RenamePlan Plan, string TemporaryPath)>();
        var completed = new List<(RenamePlan Plan, string TemporaryPath)>();

        try
        {
            foreach (var plan in plans)
            {
                var temporaryPath = Path.Combine(
                    Path.GetDirectoryName(plan.SourcePath)!,
                    $".bulkrename-{Guid.NewGuid():N}.tmp");
                Move(plan.SourcePath, temporaryPath, plan.IsDirectory);
                staged.Add((plan, temporaryPath));
            }

            foreach (var item in staged)
            {
                Move(item.TemporaryPath, item.Plan.DestinationPath, item.Plan.IsDirectory);
                completed.Add(item);
            }
        }
        catch (Exception operationError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var item in completed.AsEnumerable().Reverse())
            {
                TryMove(item.Plan.DestinationPath, item.TemporaryPath, item.Plan.IsDirectory, rollbackErrors);
            }

            foreach (var item in staged.AsEnumerable().Reverse())
            {
                TryMove(item.TemporaryPath, item.Plan.SourcePath, item.Plan.IsDirectory, rollbackErrors);
            }

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    "The rename failed and one or more files could not be restored.",
                    new[] { operationError }.Concat(rollbackErrors));
            }

            throw new IOException("The rename failed. Original names were restored.", operationError);
        }
    }

    private static void ValidatePlans(IReadOnlyCollection<RenamePlan> plans)
    {
        var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
        var destinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(plan.NewName) ||
                plan.NewName is "." or ".." ||
                plan.NewName.IndexOfAny(['/', '\\', '\0']) >= 0 ||
                Path.GetFileName(plan.NewName) != plan.NewName)
            {
                throw new ArgumentException($"Invalid destination name: '{plan.NewName}'.");
            }

            if (!File.Exists(plan.SourcePath) && !Directory.Exists(plan.SourcePath))
            {
                throw new FileNotFoundException("A source item no longer exists.", plan.SourcePath);
            }

            if (!sourcePaths.Add(Path.GetFullPath(plan.SourcePath)))
            {
                throw new ArgumentException($"The same source item appears more than once: '{plan.SourcePath}'.");
            }

            if (!destinationPaths.Add(Path.GetFullPath(plan.DestinationPath)))
            {
                throw new ArgumentException($"Multiple items would be named '{plan.NewName}'.");
            }

            if (plan.IsDirectory != Directory.Exists(plan.SourcePath))
            {
                throw new ArgumentException($"Item type changed while preparing rename: '{plan.SourcePath}'.");
            }
        }

        foreach (var plan in plans)
        {
            if ((File.Exists(plan.DestinationPath) || Directory.Exists(plan.DestinationPath)) &&
                !sourcePaths.Contains(Path.GetFullPath(plan.DestinationPath)))
            {
                throw new IOException($"A destination already exists: '{plan.NewName}'.");
            }
        }
    }

    private static void TryMove(string source, string destination, bool isDirectory, ICollection<Exception> errors)
    {
        try
        {
            Move(source, destination, isDirectory);
        }
        catch (Exception error)
        {
            errors.Add(new IOException($"Could not restore '{source}' to '{destination}'.", error));
        }
    }

    private static void Move(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }
}
