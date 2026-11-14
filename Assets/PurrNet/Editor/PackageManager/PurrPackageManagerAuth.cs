using System;
using PurrNet.Essentials;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerAuth
    {
        internal static int LoginAttempt => PurrAuth.loginAttempt;

        public static event Action onAuthChanged
        {
            add => PurrAuth.onAuthChanged += value;
            remove => PurrAuth.onAuthChanged -= value;
        }

        public static string GetApiKey() => PurrAuth.GetApiKey();

        public static void SetApiKey(string key) => PurrAuth.SetApiKey(key);

        public static void ClearApiKey() => PurrAuth.ClearApiKey();

        public static bool HasApiKey() => PurrAuth.HasApiKey();

        public static bool IsLoggedIn => PurrAuth.isLoggedIn;

        public static bool TryGetApiKey(out string apiKey) => PurrAuth.TryGetApiKey(out apiKey);

        public static void Login() => PurrAuth.Login();

        public static void PasteApiKey() => PurrAuth.PasteApiKey();

        public static void Logout() => PurrAuth.Logout();

        public static void DrawLoginButton() => PurrAuth.DrawLoginButton();

        public static void DrawLoginButton(float width, float height) => PurrAuth.DrawLoginButton(width, height);

        internal static void CancelLogin() => PurrAuth.CancelLogin();
    }
}
