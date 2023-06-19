using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using PackageReference = NuGetMonitor.Models.PackageReference;

namespace NuGetMonitor.Services
{
    public static class NuGetService
    {
        public static async Task<IEnumerable<PackageReference>> CheckPackageReferences(IReadOnlyCollection<PackageIdentity> packageIdentities)
        {
            using var sourceCacheContext = new SourceCacheContext();

            var identitiesById = packageIdentities.GroupBy(item => item.Id);

            var result = await Task
                .WhenAll(identitiesById.Select(identities => CheckPackageReference(identities, sourceCacheContext)))
                .ConfigureAwait(false);

            return result;
        }

        private static async Task<PackageReference> CheckPackageReference(IGrouping<string, PackageIdentity> packageIdentities, SourceCacheContext sourceCacheContext)
        {
            // TODO: read source repositories from nuget.config in solution directory and check all repos
            //var packageSourceProvider = new PackageSourceProvider(new Settings(_solutionDirectory));
            //var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
            //var repositories = sourceRepositoryProvider.GetRepositories().ToArray();

            var packageMetadataResource = await Repository.Factory
                .GetCoreV3("https://api.nuget.org/v3/index.json")
                .GetResourceAsync<PackageMetadataResource>()
                .ConfigureAwait(false);

            // use the oldest reference with the smallest version
            var identity = packageIdentities.OrderBy(item => item.Version.Version).First();

            var metadata = await packageMetadataResource
                .GetMetadataAsync(identity, sourceCacheContext, NullLogger.Instance, CancellationToken.None)
                .ConfigureAwait(false);

            if (metadata == null)
            {
                return new PackageReference(identity);
            }

            return new PackageReference(identity)
            {
                IsVulnerable = metadata.Vulnerabilities != null,
                IsDeprecated = await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false) != null,
                IsOutdated = await IsOutdated(identity, sourceCacheContext).ConfigureAwait(false),
            };
        }

        private static async Task<bool> IsOutdated(PackageIdentity packageIdentity, SourceCacheContext sourceCacheContext)
        {
            var packageResource = await Repository.Factory
                .GetCoreV3("https://api.nuget.org/v3/index.json")
                .GetResourceAsync<FindPackageByIdResource>()
                .ConfigureAwait(false);

            var versions = await packageResource
                .GetAllVersionsAsync(packageIdentity.Id, sourceCacheContext, NullLogger.Instance, CancellationToken.None)
                .ConfigureAwait(false);

            var latestVersion = versions.Last(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

            return latestVersion > packageIdentity.Version;
        }
    }
}
