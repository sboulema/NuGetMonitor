using Microsoft.Build.Evaluation;

namespace NuGetMonitor.Model;

public static class ExtensionMethods
{
    public static string? CountedDescription<T>(this IEnumerable<T> items, string singular, Func<T, bool>? selector = null)
    {
        selector ??= _ => true;

        var count = items.Count(selector);

        switch (count)
        {
            case <= 0:
                return null;
            case 1:
                return $"1 {singular}";
            default:
            {
                var plural = singular.EndsWith("y", StringComparison.CurrentCulture) ? singular.Substring(0, singular.Length - 1) + "ies" : singular + "s";

                return $"{count} {plural}";
            }
        }
    }

    public static bool GetIsPinned(this ProjectItem projectItem)
    {
        var metadataValue = projectItem.GetMetadataValue("IsPinned");

         return bool.TryParse(metadataValue, out var value) && value;
    }

    public static string GetJustification(this ProjectItem projectItem)
    {
        return projectItem.GetMetadataValue("Justification");
    }

    public static bool IsGlobalPackageReference(this ProjectItem projectItem)
    {
        return projectItem.ItemType.Equals("GlobalPackageReference", StringComparison.OrdinalIgnoreCase);
    }
}