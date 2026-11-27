using Newtonsoft.Json;

namespace PurrNet.Essentials
{
    public struct Result<T>
    {
        public bool success { get; }
        public T value { get; }
        public string error { get; }

        private Result(bool success, T value, string error)
        {
            this.success = success;
            this.value = value;
            this.error = error;
        }

        public static Result<T> Ok(T value) => new Result<T>(true, value, null);
        public static Result<T> Fail(string error) => new Result<T>(false, default, error);
    }

    public class ApiError
    {
        [JsonProperty("error")]
        public string error { get; private set; }

        [JsonProperty("message")]
        public string message { get; private set; }
    }
}
