﻿using MatthiWare.CommandLine.Core.Attributes;

namespace ShieldCLI.Commands
{
    public class ProtectOptions
    {
        [Name("projectkey"),Required, Description("Key of the project.")]
        public string ProjectKey{ get; set; }

        [Name("appkey"),Description("Application Key that will be protected")]
        public string AppKey { get; set; }

        [Name("config"),Required, Description("Name of Config File")]
        public string Config{ get; set; }

        [Name("auto"), DefaultValue(true), Description("Name of Config File")]
        public bool Auto { get; set; }

    }
}
