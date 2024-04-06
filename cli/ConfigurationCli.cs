using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace MemoriaNote.Cli
{
    /// <summary>
    /// Represents the configuration specific to the CLI application.
    /// </summary>
    [DataContract]
    public class ConfigurationCli : Configuration
    {
#pragma warning disable 108
        public static ConfigurationCli Instance
        {
            get => (ConfigurationCli)Configuration.Instance;
            set => Configuration.Instance = value;
        }
#pragma warning restore 108
        /// <summary>
        /// Gets or sets the instance of ConfigurationCli.
        /// </summary>
        public static ConfigurationCli Create() => Configuration.Create<ConfigurationCli>();
        /// <summary>
        /// Represents terminal settings for the CLI.
        /// </summary>
        [Reactive, DataMember] public TerminalSetting Terminal { get; set; } = new TerminalSetting();

        [DataContract]
        public class TerminalSetting : ConfigurationBase
        {
            /// <summary>
            /// Gets the environment variable name for the editor.
            /// </summary>
            public static string EditorEnvName => "EDITOR";
            [Reactive, DataMember] public bool EditorEnv { get; set; } = true;

            [Reactive, DataMember] public string EditorPath { get; set; }

            [Reactive, DataMember] public CompletionType Completion { get; set; } = CompletionType.Word;
        }

        /// <summary>
        /// Represents state settings for the CLI.
        /// </summary>
        [Reactive, DataMember] public StateSetting State { get; set; } = new StateSetting();

        /// <summary>
        /// Represents state setting configurations.
        /// </summary>
        [DataContract]
        public class StateSetting : ConfigurationBase
        {
            [Reactive, DataMember] public SearchRangeType SearchRange { get; set; }
            [Reactive, DataMember] public SearchMethodType SearchMethod { get; set; }
        }

        /// <summary>
        /// Sets default values for ConfigurationCli properties.
        /// </summary>
        protected override void SetDefault<T>(T value)
        {
            ConfigurationCli config = value as ConfigurationCli;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                config.Terminal.EditorPath = @"C:\Program Files\Git\usr\bin\nano.exe";
            else
                config.Terminal.EditorPath = "nano";

            base.SetDefault(value);
        }
    }

    /// <summary>
    /// Represents completion types for the CLI.
    /// </summary>
    public enum CompletionType
    {
        None,
        Word
    }
}