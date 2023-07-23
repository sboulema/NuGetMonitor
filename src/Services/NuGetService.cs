using NuGet.Common;
using NuGet.Protocol.Core.Types;
using NuGet.Packaging.Core;
using PackageReference = NuGetMonitor.Models.PackageReference;
using NuGet.Configuration;
using Community.VisualStudio.Toolkit;
using Settings = NuGet.Configuration.Settings;
using NuGetMonitor.Models;

namespace NuGetMonitor.Services;

public static class NuGetService
{
    public static async Task<IEnumerable<PackageReference>> CheckPackageReferences(IReadOnlyCollection<PackageReferenceEntry> references)
    {
        using var sourceCacheContext = new SourceCacheContext();

        var identitiesById = references
            .Select(item => item.Identity)
            .GroupBy(item => item.Id);

        var sourceRepositories = await GetSourceRepositories().ConfigureAwait(false);

        var result = await Task
            .WhenAll(identitiesById.Select(identities => CheckPackageReference(identities, sourceCacheContext, sourceRepositories)))
            .ConfigureAwait(false);

        return result;
    }

    private static async Task<PackageReference> CheckPackageReference(
        IGrouping<string, PackageIdentity> packageIdentities,
        SourceCacheContext sourceCacheContext,
        IEnumerable<SourceRepository> sourceRepositories)
    {
        // use the oldest reference with the smallest version
        var identity = packageIdentities.OrderBy(item => item.Version.Version).First();

        foreach (var sourceRepository in sourceRepositories)
        {
            var packageMetadataResource = await sourceRepository
                .GetResourceAsync<PackageMetadataResource>()
                .ConfigureAwait(false);

            var metadata = await packageMetadataResource
                .GetMetadataAsync(identity, sourceCacheContext, NullLogger.Instance, CancellationToken.None)
                .ConfigureAwait(false);

            if (metadata == null)
            {
                continue;
            }

            return new PackageReference(identity)
            {
                IsVulnerable = metadata.Vulnerabilities != null,
                IsDeprecated = await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false) != null,
                IsOutdated = await IsOutdated(identity, sourceCacheContext, sourceRepository).ConfigureAwait(false),
            };
        }

        return new PackageReference(identity);
    }

    private static async Task<bool> IsOutdated(
        PackageIdentity packageIdentity,
        SourceCacheContext sourceCacheContext,
        SourceRepository sourceRepository)
    {
        var packageResource = await sourceRepository
            .GetResourceAsync<FindPackageByIdResource>()
            .ConfigureAwait(false);

        var versions = await packageResource
            .GetAllVersionsAsync(packageIdentity.Id, sourceCacheContext, NullLogger.Instance, CancellationToken.None)
            .ConfigureAwait(false);

        var latestVersion = versions.Last(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

        return latestVersion > packageIdentity.Version;
    }

    private static async Task<IEnumerable<SourceRepository>> GetSourceRepositories()
    {
        var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(false);
        var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);

        var packageSourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(solutionDirectory));
        var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
        var sourceRepositories = sourceRepositoryProvider.GetRepositories();

        return sourceRepositories;
    }
}