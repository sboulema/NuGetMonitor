using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PackageReference = NuGetMonitor.Models.PackageReference;

namespace NuGetMonitor.Services
{
    public static class NuGetService
    {
        public static async Task<IEnumerable<PackageReference>> CheckPackageReferences(IEnumerable<PackageReference> packageReferences)
        {
            var result = await Task.WhenAll(
                packageReferences.Select(packageReference => CheckPackageReference(packageReference))
            );

            return result;
        }

        private static async Task<PackageReference> CheckPackageReference(PackageReference packageReference)
        {
            var packageMetadataResource = await Repository.Factory
                .GetCoreV3("https://api.nuget.org/v3/index.json")
                .GetResourceAsync<PackageMetadataResource>();

            var metadata = await packageMetadataResource.GetMetadataAsync(
                packageReference.PackageIdentity,
                new SourceCacheContext(),
                NullLogger.Instance,
                CancellationToken.None);

            packageReference.IsVulnerable = metadata.Vulnerabilities != null;
            packageReference.IsDeprecated = await metadata.GetDeprecationMetadataAsync() != null;
            packageReference.IsOutdated = await IsOutdated(packageReference);

            return packageReference;
        }

        private static async Task<bool> IsOutdated(PackageReference packageReference)
        {
            var packageResource = await Repository.Factory
                .GetCoreV3("https://api.nuget.org/v3/index.json")
                .GetResourceAsync<FindPackageByIdResource>();

            var versions = await packageResource.GetAllVersionsAsync(
                packageReference.PackageIdentity.Id,
                new SourceCacheContext(),
                NullLogger.Instance,
                CancellationToken.None);

            return versions.Last() > packageReference.PackageIdentity.Version;
        }
    }
}
