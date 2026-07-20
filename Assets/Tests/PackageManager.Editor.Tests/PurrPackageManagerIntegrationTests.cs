using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace PurrNet.Editor.Tests
{
    public class PurrPackageManagerIntegrationTests
    {
        private const string PackageName = "com.purrnet.package-manager-integration-test";

        [Test]
        [Explicit("Mutates the project manifest temporarily and invokes the real Unity Package Manager.")]
        [Timeout(180000)]
        public async Task LocalPackage_CanInstallUpdateAndRemoveThroughUnityPackageManager()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            var lockPath = Path.Combine(projectRoot, "Packages", "packages-lock.json");
            var fixtureRoot = Path.Combine(projectRoot, "PurrPackageManagerIntegrationFixtures", Guid.NewGuid().ToString("N"));
            var manifestBackup = File.ReadAllText(manifestPath);
            var lockExisted = File.Exists(lockPath);
            var lockBackup = lockExisted ? File.ReadAllText(lockPath) : null;
            var package = JsonConvert.DeserializeObject<PackageInfo>(
                $"{{\"id\":\"integration-test\",\"display_name\":\"Package Manager Integration Test\",\"upm_package_name\":\"{PackageName}\",\"is_external\":true}}");

            Directory.CreateDirectory(fixtureRoot);
            try
            {
                WritePackageManifest(fixtureRoot, "1.0.0");
                var localReference = "file:../PurrPackageManagerIntegrationFixtures/" + Path.GetFileName(fixtureRoot);

                var install = await PurrPackageManagerInstaller.InstallExternal(package, localReference);
                Assert.That(install.Success, Is.True, install.Error);
                Assert.That(GetRegisteredVersion(), Is.EqualTo("1.0.0"));

                var reinstall = await PurrPackageManagerInstaller.InstallExternal(package, localReference);
                Assert.That(reinstall.Success, Is.True, reinstall.Error);
                Assert.That(GetRegisteredVersion(), Is.EqualTo("1.0.0"));

                WritePackageManifest(fixtureRoot, "1.1.0");
                var update = await PurrPackageManagerInstaller.InstallExternal(package, localReference);
                Assert.That(update.Success, Is.True, update.Error);
                Assert.That(GetRegisteredVersion(), Is.EqualTo("1.1.0"));

                var remove = await PurrPackageManagerInstaller.Remove(package, false);
                Assert.That(remove.Success, Is.True, remove.Error);
                Assert.That(GetRegisteredVersion(), Is.Null);
            }
            finally
            {
                // Always restore byte-for-byte project state, even if an assertion or UPM operation fails.
                try
                {
                    if (GetRegisteredVersion() != null)
                        await PurrPackageManagerInstaller.Remove(package, false);
                }
                catch
                {
                    // The exact manifest/lock snapshots below are the final recovery path.
                }

                PurrPackageManagerIO.WriteAllTextAtomic(manifestPath, manifestBackup);
                if (lockExisted)
                    PurrPackageManagerIO.WriteAllTextAtomic(lockPath, lockBackup);
                else if (File.Exists(lockPath))
                    File.Delete(lockPath);

                PurrPackageManagerIO.DeleteDirectoryBestEffort(fixtureRoot);
                var fixtureParent = Path.GetDirectoryName(fixtureRoot);
                if (!string.IsNullOrEmpty(fixtureParent) && Directory.Exists(fixtureParent)
                    && Directory.GetFileSystemEntries(fixtureParent).Length == 0)
                    Directory.Delete(fixtureParent);
            }
        }

        private static void WritePackageManifest(string root, string version)
        {
            var json = $"{{\n  \"name\": \"{PackageName}\",\n  \"version\": \"{version}\",\n  \"displayName\": \"PurrNet Package Manager Integration Test\"\n}}";
            PurrPackageManagerIO.WriteAllTextAtomic(Path.Combine(root, "package.json"), json);
        }

        private static string GetRegisteredVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => string.Equals(package.name, PackageName, StringComparison.OrdinalIgnoreCase))
                ?.version;
        }
    }
}
