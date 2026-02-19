using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerAPI
    {
        private const string BaseUrl = "https://purrnet.dev/api";

        public static async Task<Result<PackagesResponse>> GetPackages(string apiKey)
        {
            return await SendRequest<PackagesResponse>($"{BaseUrl}/packages", apiKey);
        }

        public static async Task<Result<EntitlementsResponse>> GetEntitlements(string apiKey)
        {
            return await SendRequest<EntitlementsResponse>($"{BaseUrl}/entitlements", apiKey);
        }

        public static async Task<Result<DownloadResponse>> GetDownloadUrl(string apiKey, string packageId, string versionId)
        {
            var url = $"{BaseUrl}/packages/{packageId}/download?versionId={versionId}";
            return await SendRequest<DownloadResponse>(url, apiKey);
        }

        public static async Task<Result<string>> DownloadFile(string url, string destPath)
        {
            try
            {
                var request = UnityWebRequest.Get(url);
                request.downloadHandler = new DownloadHandlerFile(destPath);
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    return Result<string>.Fail(request.error);

                return Result<string>.Ok(destPath);
            }
            catch (Exception e)
            {
                return Result<string>.Fail(e.Message);
            }
        }

        private static async Task<Result<T>> SendRequest<T>(string url, string apiKey)
        {
            try
            {
                var request = UnityWebRequest.Get(url);

                if (!string.IsNullOrEmpty(apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var apiError = JsonConvert.DeserializeObject<ApiError>(request.downloadHandler.text);
                        if (apiError != null && !string.IsNullOrEmpty(apiError.Error))
                            errorMsg = apiError.Error;
                    }
                    catch
                    {
                        // use the original error
                    }

                    return Result<T>.Fail(errorMsg);
                }

                var result = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                return Result<T>.Ok(result);
            }
            catch (Exception e)
            {
                return Result<T>.Fail(e.Message);
            }
        }
    }
}
