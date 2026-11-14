#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace PurrNet.Recording
{
    static class WebRecording
    {
        public const int StatBytes = 0, StatCompressMs = 1, StatDeflate = 3;

        [DllImport("__Internal")]
        static extern int PurrNetRec_Open(string name, double maxLocalBytes);

        [DllImport("__Internal")]
        static extern unsafe void PurrNetRec_Append(int handle, int kind, int compress, byte* data, int length);

        [DllImport("__Internal")]
        static extern void PurrNetRec_Link(int handle, string json);

        [DllImport("__Internal")]
        static extern void PurrNetRec_Close(int handle);

        [DllImport("__Internal")]
        static extern double PurrNetRec_Stat(int handle, int which);

        [DllImport("__Internal")]
        static extern void PurrNetRec_Uploads(string endpoint, string projectKey, double maxLocalBytes);

        [DllImport("__Internal")]
        static extern void PurrNetRec_SetIdle(int idle);

        public static int Open(string name, long maxLocalBytes) => PurrNetRec_Open(name, maxLocalBytes);

        public static unsafe void Append(int handle, int kind, bool compress, byte[] data, int length)
        {
            fixed (byte* p = data)
                PurrNetRec_Append(handle, kind, compress ? 1 : 0, p, length);
        }

        public static void Link(int handle, string json) => PurrNetRec_Link(handle, json);

        public static void Close(int handle) => PurrNetRec_Close(handle);

        public static double Stat(int handle, int which) => handle == 0 ? 0 : PurrNetRec_Stat(handle, which);

        public static void Uploads(string endpoint, string projectKey, long maxLocalBytes) =>
            PurrNetRec_Uploads(endpoint, projectKey, maxLocalBytes);

        public static void SetIdle(bool idle) => PurrNetRec_SetIdle(idle ? 1 : 0);
    }
}
#endif
