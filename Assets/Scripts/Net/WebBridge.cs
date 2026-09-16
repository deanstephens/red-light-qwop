using System.Runtime.InteropServices;
using UnityEngine;

namespace RedLightQwop
{
    /// <summary>Tiny bridge to the hosting web page. Everything is a no-op outside browser builds.</summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int RLQ_IsDocumentHidden();
        [DllImport("__Internal")] static extern int RLQ_CopyText(string text);

        public static bool IsDocumentHidden => RLQ_IsDocumentHidden() != 0;
        public static bool CopyText(string text) => RLQ_CopyText(text) != 0;
#else
        public static bool IsDocumentHidden => false;
        public static bool CopyText(string text)
        {
            GUIUtility.systemCopyBuffer = text;
            return true;
        }
#endif
    }
}
