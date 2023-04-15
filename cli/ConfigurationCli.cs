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

        public static ConfigurationCli Create() => Configuration.Create<ConfigurationCli>();

        public TerminalSetting Terminal { get; set; } = new TerminalSetting();

        [DataContract]
        public class TerminalSetting : ConfigurationBase
        {
            [Reactive, DataMember] public string EditorPath { get; set; }
        }

        protected override void SetDefault<T>(T value)
        {
            ConfigurationCli config = value as ConfigurationCli;
            
            config.Terminal.EditorPath = "";
            
            base.SetDefault(value);
        }
    }
}