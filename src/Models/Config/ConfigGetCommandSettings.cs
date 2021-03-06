using System.ComponentModel;
using Spectre.Console.Cli;

namespace Dotnetsafer.CLI.Models.Config
{


    internal class ConfigGetCommandSettings : Branches.ShieldSettings
    {
        [CommandArgument(1, "[NAME]"), Description("Name of the protection config file")]
        public string Name { get; set; }
        [CommandArgument(0, "<PATH>"), Description("Path of the protection config file")]
        public string Path { get; set; }

        [CommandArgument(2, "[TYPE]"), Description("Type of the protection config file.(Project default)"), DefaultValue("project")]
        public string Type { get; set; }

        [CommandOption("--create"), Description("Create a config file if not exist."), DefaultValue(false)]
        public bool Create { get; set; }
    }










    //[Name("", "or-create"), DefaultValue(false), Description("Create a default config file")]
    //public bool Create { get; set; }
}