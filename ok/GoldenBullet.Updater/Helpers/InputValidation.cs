using System.Text.RegularExpressions;

namespace GoldenBullet.Updater.Helpers;

public static partial class InputValidation
{
    public static void ValidateRepository(string repository)
    {
        // Make sure the repository is in the right format
        if (!RepositoryRegex().IsMatch(repository))
        {
            Utils.ExitWithError("The repository must be in the format owner/repo (e.g. GoldenBullet/GoldenBullet)");
        }
    }

    [GeneratedRegex(@"^[\w-]+/[\w-]+$")]
    private static partial Regex RepositoryRegex();
}
