using System;
using System.Runtime.InteropServices;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Starts and ends the macOS process activity (NSProcessInfo beginActivityWithOptions:reason:) that
    /// keeps App Nap from throttling the Editor while it is in the background.
    /// The declarations resolve only when called, so other platforms can load this type without harm.
    /// </summary>
    internal sealed class MacProcessActivityApi : IProcessActivityApi
    {
        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

        // NSActivityUserInitiatedAllowingIdleSystemSleep in NSProcessInfo.h. Why this option: it was the
        // smallest one measured to lift the throttle (NSActivityBackground and NSActivityLatencyCritical
        // did not), and unlike NSActivityUserInitiated it leaves idle system sleep alone.
        private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00EFFFFFUL;

        public IntPtr Begin(string reason)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty");

            // Why a pool of our own: callers run on thread-pool threads, which have no autorelease pool, so
            // the autoreleased token and reason string would never be freed. Draining this pool frees both,
            // leaving only the reference retained below.
            IntPtr pool = PushAutoreleasePool();
            try
            {
                IntPtr processInfo = Send(GetClass("NSProcessInfo"), RegisterSelector("processInfo"));
                IntPtr reasonString = SendUtf8String(
                    GetClass("NSString"),
                    RegisterSelector("stringWithUTF8String:"),
                    reason);
                IntPtr token = SendBeginActivity(
                    processInfo,
                    RegisterSelector("beginActivityWithOptions:reason:"),
                    UserInitiatedAllowingIdleSystemSleep,
                    reasonString);
                // Why retain before the pool drains: the token is autoreleased, and draining the pool would
                // free it and end the activity at once, while every call here still appears to succeed.
                if (token != IntPtr.Zero)
                {
                    Send(token, RegisterSelector("retain"));
                }

                return token;
            }
            finally
            {
                PopAutoreleasePool(pool);
            }
        }

        public void End(IntPtr token)
        {
            if (token == IntPtr.Zero)
            {
                throw new ArgumentException("token must be a non-zero value returned by Begin.", nameof(token));
            }

            IntPtr processInfo = Send(GetClass("NSProcessInfo"), RegisterSelector("processInfo"));
            SendEndActivity(processInfo, RegisterSelector("endActivity:"), token);
            Send(token, RegisterSelector("release"));
        }

        // objc_msgSend takes a different argument list per selector, so each shape gets its own name.
        [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
        private static extern IntPtr GetClass(string name);

        [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
        private static extern IntPtr RegisterSelector(string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendUtf8String(IntPtr receiver, IntPtr selector, string value);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendBeginActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void SendEndActivity(IntPtr receiver, IntPtr selector, IntPtr token);

        [DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPush")]
        private static extern IntPtr PushAutoreleasePool();

        [DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPop")]
        private static extern void PopAutoreleasePool(IntPtr pool);
    }
}
