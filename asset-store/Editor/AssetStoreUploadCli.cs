using AssetStoreTools.Api;
using AssetStoreTools.Api.Models;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Exporter;
using AssetStoreTools.Utility;
using AssetStoreTools.Validator;
using AssetStoreTools.Validator.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Cli
{
    internal static class AssetStoreUploadCli
    {
        private const string SessionTokenKey = "kharma.sessionid";

        private static readonly JObject Result = new JObject();
        private static string _resultPath;

        public static void Run()
        {
            RunAsync();
        }

        private static async void RunAsync()
        {
            var code = 1;
            try
            {
                code = await Main();
            }
            catch (Exception e)
            {
                Fail(e.ToString());
            }
            finally
            {
                WriteResult();
                EditorApplication.Exit(code);
            }
        }

        private static async Task<int> Main()
        {
            _resultPath = Arg("-asuResult");
            var root = Arg("-asuRoot");
            var packageId = Arg("-asuPackageId");
            var output = Arg("-asuOutput");
            var pristine = Arg("-asuPristine");
            var deps = SplitList(Arg("-asuDeps"));
            var slots = SplitList(Arg("-asuSlots"));
            var list = Flag("-asuList");
            var upload = Flag("-asuUpload");
            var validate = Flag("-asuValidate");

            Result["unity"] = Application.unityVersion;
            Result["uploaded"] = false;

            AssetStoreClient client = null;
            IAssetStoreApi api = null;
            Package package = null;

            if (list || upload || !Flag("-asuOffline"))
            {
                client = new AssetStoreClient();
                api = new AssetStoreApi(client);
                if (!await Authenticate(api))
                    return Fail("Could not sign in to the Asset Store. Sign in once in the Asset Store Uploader window, " +
                                "or set ASSET_STORE_USER / ASSET_STORE_PASS for one run.");

                var packages = await api.GetPackages();
                if (!packages.Success)
                    return Fail($"Could not fetch publisher packages: {packages.Exception}");

                if (list)
                {
                    var all = new JArray();
                    foreach (var p in packages.Packages)
                    {
                        all.Add(new JObject { ["id"] = p.PackageId, ["name"] = p.Name, ["status"] = p.Status, ["versionId"] = p.VersionId });
                        Log($"package {p.PackageId} '{p.Name}' status={p.Status}");
                    }
                    Result["packages"] = all;
                    return 0;
                }

                package = packages.Packages.FirstOrDefault(p => p.PackageId == packageId);
                if (package == null)
                    return Fail($"No package with id '{packageId}' on this publisher account.");

                Log($"package {package.PackageId} '{package.Name}' status={package.Status}");
                Result["packageId"] = package.PackageId;
                Result["packageName"] = package.Name;
                Result["packageStatus"] = package.Status;
                Result["versionId"] = package.VersionId;

                var uploads = await GetUploads(client, package);
                Result["versionUploads"] = uploads;
                Log($"{package.Status} version {package.VersionId} has uploads from: {Describe(uploads)}");

                if (upload)
                {
                    if (package.Status != "draft")
                        return Fail($"'{package.Name}' has no open draft (status: {package.Status}). " +
                                    "Create one at https://publisher.unity.com/packages, then run this again.");

                    var stale = uploads.Properties().Select(x => x.Name).Where(x => !slots.Contains(x)).ToList();
                    if (stale.Count > 0)
                        return Fail($"The draft holds uploads from {string.Join(", ", stale)} that this release does not replace, " +
                                    "so those Unity versions would keep getting the old build. Remove them in the Publisher Portal " +
                                    "(Package upload > Remove) or add them to asset-store/products.json.");
                }
            }

            if (!Directory.Exists(root))
                return Fail($"Export root '{root}' does not exist.");

            var rootGuid = AssetDatabase.AssetPathToGUID(root);
            if (string.IsNullOrEmpty(rootGuid))
                return Fail($"'{root}' is not a known asset.");
            Result["rootGuid"] = rootGuid;

            if (validate)
                Validate(root, package != null ? package.Category : string.Empty);

            if (!string.IsNullOrEmpty(pristine) && !RestoreSource(root, pristine))
                return Fail("The export root no longer matches the source ref (files added, removed, or GUIDs changed on import); see 'sourceProblems'.");

            var registry = PackageUtility.GetAllRegistryPackages();
            var resolved = new JObject();
            foreach (var dep in deps)
            {
                var info = registry.FirstOrDefault(x => x.name == dep);
                if (info == null)
                    return Fail($"Dependency '{dep}' is not a registry package in this project.");
                resolved[dep] = info.version;
                Log($"dependency {dep} {info.version}");
            }
            Result["dependencies"] = resolved;

            var exporter = new DefaultPackageExporter(new DefaultExporterSettings
            {
                ExportPaths = new[] { root },
                OutputFilename = output,
                Dependencies = deps.ToArray(),
                PreviewGenerator = null
            });

            var exported = await ((PackageExporterBase)exporter).Export();
            if (!exported.Success)
                return Fail($"Export failed: {exported.Exception}");

            Result["package"] = Path.GetFullPath(output);
            Result["packageBytes"] = new FileInfo(output).Length;
            Log($"exported {output} ({new FileInfo(output).Length} bytes)");

            if (!upload)
                return 0;

            Log($"uploading to draft {package.VersionId}");
            await Upload(client, package, output, root, rootGuid);
            Result["uploaded"] = true;

            var after = await GetUploads(client, package);
            Result["versionUploadsAfter"] = after;
            Log($"draft now has uploads from: {Describe(after)}");
            if (after.Property(Application.unityVersion) == null)
                return Fail($"The server accepted the upload, but the draft does not list an upload from {Application.unityVersion}.");

            return 0;
        }

        private static async Task Upload(IAssetStoreClient client, Package package, string output, string root, string rootGuid)
        {
            var uri = ApiUtility.CreateUri(Constants.Api.UploadUnityPackageUrl(package.VersionId), new Dictionary<string, string>
            {
                { "root_guid", rootGuid },
                { "root_path", root },
                { "project_path", root }
            }, true);

            using (var stream = new FileStream(output, FileMode.Open, FileAccess.Read))
            {
                var response = await client.Put(uri, new StreamContent(stream, 32768));
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Upload failed with HTTP {(int)response.StatusCode}: {body}");

                var parsed = new PackageUploadResponse(body);
                if (!parsed.Success)
                    throw new Exception($"Upload rejected: {parsed.Exception?.Message}");
            }
        }

        private static async Task<JObject> GetUploads(IAssetStoreClient client, Package package)
        {
            var uri = ApiUtility.CreateUri(Constants.Api.GetPackageUploadedVersionsUrl(package.PackageId, package.VersionId), true);
            var response = await client.Get(uri);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            return json["content"]?["uploads"] as JObject ?? new JObject();
        }

        private static string Describe(JObject uploads)
        {
            return uploads.Count == 0 ? "none" : string.Join(", ", uploads.Properties().Select(x => x.Name));
        }

        private static async Task<bool> Authenticate(IAssetStoreApi api)
        {
            var saved = EditorPrefs.GetString(SessionTokenKey, string.Empty);
            if (string.IsNullOrEmpty(saved))
                Log("no saved Asset Store session");
            else if (await TryAuthenticate(api, new SessionAuthentication(saved), "saved session"))
                return true;

            var user = Environment.GetEnvironmentVariable("ASSET_STORE_USER");
            var pass = Environment.GetEnvironmentVariable("ASSET_STORE_PASS");
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                Log("ASSET_STORE_USER / ASSET_STORE_PASS not set");
                return false;
            }

            return await TryAuthenticate(api, new CredentialsAuthentication(user, pass), "credentials");
        }

        private static async Task<bool> TryAuthenticate(IAssetStoreApi api, IAuthenticationType type, string label)
        {
            var response = await api.Authenticate(type);
            if (!response.Success)
            {
                Log($"sign-in via {label} failed: {response.Exception?.Message}");
                return false;
            }

            if (!response.User.IsPublisher)
            {
                Log($"sign-in via {label} worked, but the account is not a publisher");
                return false;
            }

            EditorPrefs.SetString(SessionTokenKey, response.User.SessionId);
            Result["auth"] = label;
            Result["publisherId"] = response.User.PublisherId;
            Log($"signed in via {label} as '{response.User.Name}'");
            return true;
        }

        private static bool RestoreSource(string root, string pristine)
        {
            var problems = new JArray();
            var restored = new JArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var sources = Directory.GetFiles(Path.Combine(pristine, root), "*", SearchOption.AllDirectories).ToList();
            sources.Add(Path.Combine(pristine, root + ".meta"));

            foreach (var source in sources)
            {
                var path = source.Substring(pristine.Length).TrimStart('\\', '/').Replace('\\', '/');
                seen.Add(path);

                if (!File.Exists(path))
                {
                    problems.Add($"removed {path}");
                    continue;
                }

                var expected = File.ReadAllBytes(source);
                if (expected.SequenceEqual(File.ReadAllBytes(path)))
                    continue;

                if (path.EndsWith(".meta") && MetaGuid(source) != MetaGuid(path))
                {
                    problems.Add($"guid changed {path}");
                    continue;
                }

                File.WriteAllBytes(path, expected);
                restored.Add(path);
            }

            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToList();
            if (File.Exists(root + ".meta"))
                files.Add(root + ".meta");

            foreach (var file in files)
            {
                var path = file.Replace('\\', '/');
                if (!seen.Contains(path))
                    problems.Add($"added {path}");
            }

            Result["restoredFiles"] = restored;
            Result["sourceProblems"] = problems;
            Log($"restored {restored.Count} file(s) this editor rewrote on import");
            foreach (var entry in problems)
                Log($"source problem: {entry}");

            return problems.Count == 0;
        }

        private static string MetaGuid(string metaPath)
        {
            return File.ReadLines(metaPath).FirstOrDefault(line => line.StartsWith("guid:"));
        }

        private static void Validate(string root, string category)
        {
            var validator = new CurrentProjectValidator(new CurrentProjectValidationSettings
            {
                Category = category,
                ValidationPaths = new List<string> { root },
                ValidationType = ValidationType.UnityPackage
            });

            var result = validator.Validate();
            var issues = new JArray();

            foreach (var test in result.Tests)
            {
                var status = test.Result.Status;
                if (status == TestResultStatus.Pass || status == TestResultStatus.Undefined)
                    continue;

                var messages = new JArray();
                for (var i = 0; i < test.Result.MessageCount; i++)
                    messages.Add(test.Result.GetMessage(i).GetText());

                issues.Add(new JObject { ["test"] = test.Title, ["status"] = status.ToString(), ["messages"] = messages });
                Log($"validator {status}: {test.Title}");
            }

            Result["validationStatus"] = result.Status.ToString();
            Result["validationIssues"] = issues;
        }

        private static List<string> SplitList(string value)
        {
            return (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static bool Flag(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        }

        private static void Log(string message)
        {
            Debug.Log($"[asset-store-upload] {message}");
        }

        private static int Fail(string message)
        {
            Result["error"] = message;
            Debug.LogError($"[asset-store-upload] {message}");
            return 1;
        }

        private static void WriteResult()
        {
            if (!string.IsNullOrEmpty(_resultPath))
                File.WriteAllText(_resultPath, Result.ToString());
        }
    }
}
