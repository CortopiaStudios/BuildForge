using System;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    internal enum EnvVarOverride
    {
        Inherit = 0,
        Custom = 1,
    }

    [Serializable]
    internal class AndroidKeystoreEnvGlobalConfig
    {
        // Stored only when the user typed a name; the getters supply the
        // defaults, so ForgeSettings.asset shows empty fields until then.
        [SerializeField] string keystorePathEnvVar;
        [SerializeField] string keystorePassEnvVar;
        [SerializeField] string keyAliasEnvVar;
        [SerializeField] string keyAliasPassEnvVar;

        public string KeystorePathEnvVar
        {
            get => string.IsNullOrEmpty(keystorePathEnvVar) ? "ANDROID_KEYSTORE_PATH" : keystorePathEnvVar;
            set => keystorePathEnvVar = value;
        }

        public string KeystorePassEnvVar
        {
            get => string.IsNullOrEmpty(keystorePassEnvVar) ? "ANDROID_KEYSTORE_PASSWORD" : keystorePassEnvVar;
            set => keystorePassEnvVar = value;
        }

        public string KeyAliasEnvVar
        {
            get => string.IsNullOrEmpty(keyAliasEnvVar) ? "ANDROID_KEY_ALIAS" : keyAliasEnvVar;
            set => keyAliasEnvVar = value;
        }

        public string KeyAliasPassEnvVar
        {
            get => string.IsNullOrEmpty(keyAliasPassEnvVar) ? "ANDROID_KEY_PASSWORD" : keyAliasPassEnvVar;
            set => keyAliasPassEnvVar = value;
        }
    }

    [Serializable]
    internal class AndroidKeystoreEnvProfileConfig
    {
        [SerializeField] bool enabled;

        [SerializeField] EnvVarOverride keystorePathMode;
        [SerializeField] string keystorePathEnvVar;

        [SerializeField] EnvVarOverride keystorePassMode;
        [SerializeField] string keystorePassEnvVar;

        [SerializeField] EnvVarOverride keyAliasMode;
        [SerializeField] string keyAliasEnvVar;

        [SerializeField] EnvVarOverride keyAliasPassMode;
        [SerializeField] string keyAliasPassEnvVar;

        public bool Enabled { get => enabled; set => enabled = value; }

        public EnvVarOverride KeystorePathMode { get => keystorePathMode; set => keystorePathMode = value; }
        public string KeystorePathEnvVar { get => keystorePathEnvVar; set => keystorePathEnvVar = value; }

        public EnvVarOverride KeystorePassMode { get => keystorePassMode; set => keystorePassMode = value; }
        public string KeystorePassEnvVar { get => keystorePassEnvVar; set => keystorePassEnvVar = value; }

        public EnvVarOverride KeyAliasMode { get => keyAliasMode; set => keyAliasMode = value; }
        public string KeyAliasEnvVar { get => keyAliasEnvVar; set => keyAliasEnvVar = value; }

        public EnvVarOverride KeyAliasPassMode { get => keyAliasPassMode; set => keyAliasPassMode = value; }
        public string KeyAliasPassEnvVar { get => keyAliasPassEnvVar; set => keyAliasPassEnvVar = value; }

        /// <summary>
        /// Returns the resolved env var name.
        /// </summary>
        public string ResolveEnvVar(EnvVarOverride mode, string profileValue, string globalValue)
        {
            return mode == EnvVarOverride.Custom ? profileValue : globalValue;
        }
    }
}
