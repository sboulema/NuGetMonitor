using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NuGetMonitor.Services
{
    public static class NuGetService
    {
        public static async Task<Version> GetLatestVersion(string id)
        {
            var packageResource = await Repository.Factory
                .GetCoreV3("https://api.nuget.org/v3/index.json")
                .GetResourceAsync<FindPackageByIdResource>();

            var versions = await packageResource.GetAllVersionsAsync(
                id,
                new SourceCacheContext(),
                NullLogger.Instance,
                CancellationToken.None);

            var latestVersion = versions.Max(version => version.Version);

            return latestVersion;
        }
    }
}
