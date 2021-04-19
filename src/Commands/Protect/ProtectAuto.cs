﻿using MatthiWare.CommandLine.Abstractions.Command;
using Shield.Client.Models.API.Project;
using ShieldCLI.Models;
using ShieldCLI.Models.Protect;
using ShieldCLI.Repos;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShieldCLI.Commands.Protect
{
    public class ProtectAuto : Command<GlobalOptions, ProtectAutoOptions>
    {
        private ClientManager ClientManager { get; set; }
        public ShieldCommands ShieldCommands { get; set; }

        public ProtectAuto(ClientManager clientManager, ShieldCommands shieldCommands)
        {
            ClientManager = clientManager;
            ShieldCommands = shieldCommands;
        }

        public override void OnConfigure(ICommandConfigurationBuilder builder)
        {
            builder.Name("protect:auto").Description("Protect an application or project.");



        }

        public override async Task OnExecuteAsync(GlobalOptions option, ProtectAutoOptions options, CancellationToken cancellationToken)
        {

            ShieldCommands.AuthHasCredentials();

            string name = AnsiConsole.Ask<string>("Project Name?");

            var project = await ShieldCommands.ProjectFindOrCreateByNameAsync(name);

            ShieldCommands.ShowTable(project.Name, project.Key);



            string path = AnsiConsole.Ask<string>("Application Path?");

            var appUpload = await ShieldCommands.UploadApplicationAsync(path, project.Key);

            Console.WriteLine("");
            ShieldCommands.ShowTable(Path.GetFileName(path), appUpload.ApplicationBlob);


            string protection = ShieldCommands.ChooseProtections();
            string configname = AnsiConsole.Ask<string>("Enter a name for the config file");


            if (protection == "Load from a config file")
            {
                string apppath = AnsiConsole.Ask<string>("Config File Path?");
                ShieldCommands.ConfigApplicationGetFile(apppath, configname, false);

                AnsiConsole.Markup("[lime]Archivo Cargado[/]");
            }

            if (protection == "Use a preset")

            {
                var preset = ShieldCommands.ChoosePreset("default");
                ShieldCommands.ConfigApplicationMakeFile(Path.GetDirectoryName(path), preset, configname);

            }
            if (protection == "Make a custom")
            {

                var preset = ShieldCommands.ChoosePreset("custom");
                ShieldCommands.ConfigApplicationMakeFile(Path.GetDirectoryName(path), preset, configname);
            }
        }












        //var projectKey = options.ProjectKey;
        //var appKey = options.AppKey;
        //var config = options.Config;
        //var output = options.Output;

        //var appConfig = ClientManager.Client.Configuration.LoadApplicationConfigurationFromFile(config);




        //var connection = ClientManager.Client.Connector.CreateHubConnection();
        //var hub = ClientManager.Client.Connector.InstanceHubConnectorAsync(connection).Result;

        //hub.StartAsync().Wait();

        //var result = ClientManager.Client.Tasks.ProtectSingleFile("projectKety", "appKey", connection, appConfig);



        //result.OnSuccess(hub, (a) =>
        //{

        //    AnsiConsole.Markup($"[lime]{a.Name} application has been protected SUCESSFULLY. [/]");
        //}
        //);

        //result.OnError(hub, AnsiConsole.Write);
        //result.OnClose(hub, (s) =>
        //{
        //    AnsiConsole.Markup($"[lime]{s} [/]");
        //});
    }
}



