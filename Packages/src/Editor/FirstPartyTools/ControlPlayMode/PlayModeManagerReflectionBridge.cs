using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Drives Unity's internal PlayModeManager through reflection so control-play-mode starts and
    /// stops the active Play Mode configuration the same way the Editor's Play button does.
    /// </summary>
    internal sealed class PlayModeManagerReflectionBridge : IPlayModeScenarioBridge
    {
        private const string ManagerTypeName = "Unity.PlayMode.Editor.PlayModeManager";
        private const string DefaultConfigurationTypeName = "Unity.PlayMode.Editor.DefaultPlayModeConfiguration";
        private const string RunningStateName = "Running";
        private const string NoActiveConfigurationMessage = "No non-default Play Mode configuration is active.";

        // Resolved once per domain. Unity versions without PlayModeManager (or with renamed
        // members) resolve to an unavailable set so control-play-mode keeps its legacy path.
        private static readonly Lazy<ResolvedMembers> Members = new(ResolveMembers);

        public bool IsNonDefaultScenarioActive => GetActiveNonDefaultConfiguration() != null;

        public bool IsScenarioRunning
        {
            get
            {
                object manager = GetManager();
                if (manager == null)
                {
                    return false;
                }

                // PlayModeState is an internal enum, so it can only be compared by name.
                object state = InvokeUnwrapped(() => Members.Value.CurrentStateProperty.GetValue(manager));
                return state != null && state.ToString() == RunningStateName;
            }
        }

        public string ActiveScenarioName
        {
            get
            {
                object configuration = GetActiveNonDefaultConfiguration();
                if (configuration == null)
                {
                    return null;
                }

                return ((UnityEngine.Object)configuration).name;
            }
        }

        public void Start()
        {
            object configuration = GetActiveNonDefaultConfiguration();
            if (configuration == null)
            {
                throw new InvalidOperationException(NoActiveConfigurationMessage);
            }

            // Unity's Start() only logs and returns when the configuration is invalid; surface it
            // instead so the CLI does not wait for a Play Mode that never comes.
            ThrowIfConfigurationInvalid(configuration);
            object manager = GetManager();
            InvokeUnwrapped(() => Members.Value.StartMethod.Invoke(manager, null));
        }

        public void Stop()
        {
            if (GetActiveNonDefaultConfiguration() == null)
            {
                throw new InvalidOperationException(NoActiveConfigurationMessage);
            }

            object manager = GetManager();
            InvokeUnwrapped(() => Members.Value.StopMethod.Invoke(manager, null));
        }

        // Reflection wraps every exception thrown by the target (a method body or a property getter)
        // in TargetInvocationException, whose message says nothing; rethrow Unity's own exception
        // so the tool error carries its reason.
        internal static object InvokeUnwrapped(Func<object> invocation)
        {
            try
            {
                return invocation();
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static void ThrowIfConfigurationInvalid(object configuration)
        {
            MethodInfo isValidMethod = configuration.GetType().GetMethod(
                "IsConfigurationValid",
                BindingFlags.Instance | BindingFlags.Public);
            if (isValidMethod == null)
            {
                return;
            }

            object[] arguments = { null };
            bool isValid = (bool)InvokeUnwrapped(() => isValidMethod.Invoke(configuration, arguments));
            if (isValid)
            {
                return;
            }

            string name = ((UnityEngine.Object)configuration).name;
            string reason = arguments[0] as string ?? "unknown reason";
            throw new InvalidOperationException(
                $"Play Mode configuration '{name}' cannot start: {reason}");
        }

        private static object GetManager()
        {
            ResolvedMembers members = Members.Value;
            if (!members.IsAvailable)
            {
                return null;
            }

            return InvokeUnwrapped(() => members.InstanceProperty.GetValue(null));
        }

        // Returns the active configuration object, or null when unavailable or default.
        private static object GetActiveNonDefaultConfiguration()
        {
            object manager = GetManager();
            if (manager == null)
            {
                return null;
            }

            object configuration = InvokeUnwrapped(() => Members.Value.ActiveConfigProperty.GetValue(manager));
            if (configuration == null)
            {
                return null;
            }

            // DefaultConfig is not serialized, so a domain reload recreates it and a reference
            // comparison against ActivePlayModeConfig is unreliable; the type name is stable.
            if (configuration.GetType().FullName == DefaultConfigurationTypeName)
            {
                return null;
            }

            return configuration;
        }

        private static ResolvedMembers ResolveMembers()
        {
            Type managerType = FindManagerType();
            if (managerType == null)
            {
                return new ResolvedMembers(null, null, null, null, null);
            }

            // instance is declared on ScriptableSingleton<T>, not on PlayModeManager itself.
            PropertyInfo instanceProperty = managerType.BaseType?.GetProperty(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            PropertyInfo activeConfigProperty = managerType.GetProperty(
                "ActivePlayModeConfig",
                BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo currentStateProperty = managerType.GetProperty(
                "CurrentState",
                BindingFlags.Instance | BindingFlags.Public);
            // Type.EmptyTypes keeps the lookup unambiguous if Unity ever adds overloads.
            MethodInfo startMethod = managerType.GetMethod(
                "Start",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo stopMethod = managerType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            return new ResolvedMembers(
                instanceProperty,
                activeConfigProperty,
                currentStateProperty,
                startMethod,
                stopMethod);
        }

        private static Type FindManagerType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type managerType = assembly.GetType(ManagerTypeName, false);
                if (managerType != null)
                {
                    return managerType;
                }
            }

            return null;
        }

        /// <summary>
        /// Holds the PlayModeManager members resolved for the current domain.
        /// </summary>
        private sealed class ResolvedMembers
        {
            public ResolvedMembers(
                PropertyInfo instanceProperty,
                PropertyInfo activeConfigProperty,
                PropertyInfo currentStateProperty,
                MethodInfo startMethod,
                MethodInfo stopMethod)
            {
                InstanceProperty = instanceProperty;
                ActiveConfigProperty = activeConfigProperty;
                CurrentStateProperty = currentStateProperty;
                StartMethod = startMethod;
                StopMethod = stopMethod;
            }

            public PropertyInfo InstanceProperty { get; }
            public PropertyInfo ActiveConfigProperty { get; }
            public PropertyInfo CurrentStateProperty { get; }
            public MethodInfo StartMethod { get; }
            public MethodInfo StopMethod { get; }

            // A partial match means an unknown Unity API shape, which is treated like no manager at all.
            public bool IsAvailable =>
                InstanceProperty != null
                && ActiveConfigProperty != null
                && CurrentStateProperty != null
                && StartMethod != null
                && StopMethod != null;
        }
    }
}
