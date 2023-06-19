using NuGet.Common;
using NuGet.Protocol.Core.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using PackageReference = NuGetMonitor.Models.PackageReference;
using NuGet.Configuration;
using System.IO;
using System;
using Community.VisualStudio.Toolkit;
using Settings = NuGet.Configuration.Settings;

namespace NuGetMonitor.Services
{
    public static class NuGetService
    {
        public static async Task<IEnumerable<PackageReference>> CheckPackageReferences(
            IReadOnlyCollection<PackageIdentity> packageIdentities)
        {
            using var sourceCacheContext = new SourceCacheContext();

            var identitiesById = packageIdentities.GroupBy(item => item.Id);

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

        // https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior#config-file-locations-and-uses
        private static async Task<IEnumerable<SourceRepository>> GetSourceRepositories()
        {
            var sourceRepositories = new List<SourceRepository>();

            var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(false);
            var solutionNuGetConfigPath = Path.GetDirectoryName(solution.FullPath);

            sourceRepositories.AddRange(GetSourceRepositories(solutionNuGetConfigPath));

            var userNuGetConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NuGet");

            sourceRepositories.AddRange(GetSourceRepositories(userNuGetConfigPath));

            var computerNuGetConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "NuGet", "Config");

            sourceRepositories.AddRange(
                Directory
                    .EnumerateFiles(computerNuGetConfigPath, "*.Config")
                    .SelectMany(path => GetSourceRepositories(computerNuGetConfigPath, Path.GetFileName(path), true)));

            return sourceRepositories
                .GroupBy(repository => repository.PackageSource.SourceUri)
                .Select(group => group.First());
        }

        private static IEnumerable<SourceRepository> GetSourceRepositories(
            string path, string fileName = "NuGet.Config", bool isMachineWide = false)
        {
            if (!File.Exists(Path.Combine(path, fileName)))
            {
                return Enumerable.Empty<SourceRepository>();
            }

            var packageSourceProvider = new PackageSourceProvider(new Settings(path, fileName, isMachineWide));
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
            var repositories = sourceRepositoryProvider.GetRepositories();
            return repositories;
        }
    }
}
