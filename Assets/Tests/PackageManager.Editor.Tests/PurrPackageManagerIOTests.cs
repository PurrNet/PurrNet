using System;
using System.IO;
using NUnit.Framework;

namespace PurrNet.Editor.Tests
{
    public class PurrPackageManagerIOTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "PurrNetIOTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(_testRoot))
                return;

            foreach (var file in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_testRoot, true);
        }

        [Test]
        public void GetContainedPath_AllowsNestedChild()
        {
            var result = PurrPackageManagerIO.GetContainedPath(_testRoot, "Runtime/Package.cs");

            Assert.That(result, Is.EqualTo(Path.GetFullPath(Path.Combine(_testRoot, "Runtime", "Package.cs"))));
        }

        [TestCase("../outside.txt")]
        [TestCase("nested/../../outside.txt")]
        public void GetContainedPath_RejectsTraversal(string path)
        {
            Assert.Throws<InvalidDataException>(() => PurrPackageManagerIO.GetContainedPath(_testRoot, path));
        }

        [Test]
        public void GetContainedPath_RejectsRootedPath()
        {
            var rooted = Path.Combine(Path.GetPathRoot(_testRoot) ?? Path.DirectorySeparatorChar.ToString(), "outside.txt");
            Assert.Throws<InvalidDataException>(() => PurrPackageManagerIO.GetContainedPath(_testRoot, rooted));
        }

        [Test]
        public void GetSafeFileName_RemovesDirectories()
        {
            var unsafeName = Path.Combine("..", "downloads", "package.unitypackage");
            Assert.That(PurrPackageManagerIO.GetSafeFileName(unsafeName, "fallback.unitypackage"),
                Is.EqualTo("package.unitypackage"));
            Assert.That(PurrPackageManagerIO.GetSafeFileName(@"..\downloads\package.unitypackage", "fallback.unitypackage"),
                Is.EqualTo("package.unitypackage"));
        }

        [Test]
        public void WriteAllTextAtomic_ReadOnlyDestinationPreservesOriginal()
        {
            var path = Path.Combine(_testRoot, "manifest.json");
            File.WriteAllText(path, "old");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            Assert.Throws<UnauthorizedAccessException>(() => PurrPackageManagerIO.WriteAllTextAtomic(path, "new"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("old"));
        }

        [Test]
        public void SyncDirectoryTransactional_UpdatesAndRemovesFiles()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "kept.txt"), "new");
            File.WriteAllText(Path.Combine(source, "added.txt"), "added");
            File.WriteAllText(Path.Combine(destination, "kept.txt"), "old");
            File.WriteAllText(Path.Combine(destination, "removed.txt"), "removed");

            PurrPackageManagerIO.SyncDirectoryTransactional(source, destination);

            Assert.That(File.ReadAllText(Path.Combine(destination, "kept.txt")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "added.txt")), Is.EqualTo("added"));
            Assert.That(File.Exists(Path.Combine(destination, "removed.txt")), Is.False);
        }

        [Test]
        public void SyncDirectoryTransactional_ReadOnlyFileLeavesDestinationUntouched()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var blockedPath = Path.Combine(destination, "blocked.txt");
            File.WriteAllText(Path.Combine(source, "blocked.txt"), "new-blocked");
            File.WriteAllText(Path.Combine(source, "other.txt"), "new-other");
            File.WriteAllText(blockedPath, "old-blocked");
            File.WriteAllText(Path.Combine(destination, "other.txt"), "old-other");
            File.SetAttributes(blockedPath, File.GetAttributes(blockedPath) | FileAttributes.ReadOnly);

            Assert.Throws<IOException>(() => PurrPackageManagerIO.SyncDirectoryTransactional(source, destination));
            Assert.That(File.ReadAllText(blockedPath), Is.EqualTo("old-blocked"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "other.txt")), Is.EqualTo("old-other"));
        }

        [Test]
        public void SyncDirectoryTransactional_OpenHandleLeavesDestinationUntouched()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var blockedPath = Path.Combine(destination, "blocked.dll");
            File.WriteAllText(Path.Combine(source, "blocked.dll"), "new-blocked");
            File.WriteAllText(Path.Combine(source, "other.txt"), "new-other");
            File.WriteAllText(blockedPath, "old-blocked");
            File.WriteAllText(Path.Combine(destination, "other.txt"), "old-other");

            using (new FileStream(blockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => PurrPackageManagerIO.SyncDirectoryTransactional(source, destination));
                Assert.That(File.ReadAllText(blockedPath), Is.EqualTo("old-blocked"));
                Assert.That(File.ReadAllText(Path.Combine(destination, "other.txt")), Is.EqualTo("old-other"));
            }
        }
    }
}
