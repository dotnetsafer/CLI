﻿using System.ComponentModel;
using Spectre.Console.Cli;

namespace Bytehide.CLI.Models.Project
{
    internal class ProjectGetCommandSettings : Branches.ShieldSettings
    {
        [CommandArgument(0, "<PROJECT>"), Description("Name of the Bytehide shield project or Key if flag --key is true")]
        public string Project { get; set; }

        [CommandOption("--key|-k"), Description("Set true if <PROJECT> is the key of the project."), DefaultValue(false)]
        public bool IsProjectKey { get; set; }

    }
}