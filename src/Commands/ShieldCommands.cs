﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bytehide.CLI.Helpers;
using Bytehide.CLI.Repos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Shield.Client.Extensions;
using Shield.Client.Models;
using Shield.Client.Models.API;
using Shield.Client.Models.API.Application;
using Shield.Client.Models.API.Project;
using Spectre.Console;
using static Bytehide.CLI.Helpers.AuthHelper;

namespace Bytehide.CLI.Commands
{
    public class ShieldCommands : ICloneable
    {
        public ShieldCommands(ClientManager clientManager, DependenciesResolver dependenciesResolver)
        {
            ClientManager = clientManager;
            DependenciesResolver = dependenciesResolver;
        }

        /// <summary>
        /// Clone the current instance and disables the console outputs.
        /// </summary>
        /// <returns></returns>
        public ShieldCommands AsMute()
        {
            var cloned = Clone() as ShieldCommands;

            cloned.DoNotDisturb = true;

            return cloned;
        }

        /// <summary>
        /// Disable any console log output.
        /// </summary>
        internal bool DoNotDisturb { get; set; } = false;

        private ClientManager ClientManager { get; }
        private DependenciesResolver DependenciesResolver { get; }

        internal void MarkupLine(string text, params object[] args)
        {
            if (DoNotDisturb) 
                return;
            AnsiConsole.MarkupLine(text, args);
        }

        internal void MarkupLine(string text)
        {
            if (DoNotDisturb)
                return;
            AnsiConsole.MarkupLine(text);
        }

        internal void WriteLine(string text = null)
        {
            if (DoNotDisturb)
                return;

            if (text is null)
            {
                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.WriteLine(text);
            }
        }

        /// <summary>
        ///     Open Bytehide web to register a new user
        /// </summary>
        public void AuthRegister()

            => UsefulHelpers.OpenBrowser("https://cloud.bytehide.com/register");


        /// <summary>
        ///     Log in the current user whit an apiKey.
        /// </summary>
        /// <param name="apiKey">Dotnetsafer Personal Api Token (required to use the CLI)</param>
        /// <see cref="https://docs.bytehide.com/platforms/dotnet/products/shield/cli-authentication"/>
        public bool AuthLogin(string apiKey)
        {
            // Generates state and PKCE values.
            string state = randomDataBase64url(32);
            string code_verifier = randomDataBase64url(32);
            string code_challenge = base64urlencodeNoPadding(sha256(code_verifier));
            const string code_challenge_method = "SHA256";

            // Creates a redirect URI using an available port on the loopback address.
            string redirectURI = string.Format("http://{0}:{1}/", IPAddress.Loopback, GetRandomUnusedPort());

            // Creates an HttpListener to listen for requests on that redirect URI.
            var http = new HttpListener();
            http.Prefixes.Add(redirectURI);

            http.Start();

            // Creates the OAuth 2.0 authorization request.
            string authorizationRequest = string.Format("{0}?redirect_uri={1}&client_id={2}&state={3}&code_challenge={4}&code_challenge_method={5}",
                AuthHelper.authorizationEndpoint,
                Uri.EscapeDataString(redirectURI),
                clientID,
                state,
                code_challenge,
                code_challenge_method);

            // Opens request in the browser.
            try
            {
                Process browser = UsefulHelpers.OpenBrowser(authorizationRequest);
            }
            catch
            {
                MarkupLine(AnsiConsole.Profile.Capabilities.Links
               ? $"[red] Failed to open the browser, open the given url in your browser:[/] [link={authorizationRequest}]{authorizationRequest}[/]"
               : $"[red] Failed to open the browser, open the given url in your browser:[/] {authorizationRequest}");
                WriteLine("");
            }

            
            WriteLine("Waiting for the request to be accepted...");

            // Waits for the OAuth authorization response.
            var context =  http.GetContextAsync().Result;


            // Sends an HTTP response to the browser.
            var response = context.Response;
            string responseString = string.Format("<html><head><meta http-equiv='refresh' content='10;url=https://bytehide.com'></head><body>Please return to the ByteHide CLI.</body></html>");
            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            var responseOutput = response.OutputStream;
            Task responseTask = responseOutput.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
            {
                responseOutput.Close();
                http.Stop();
                MarkupLine(
                    "[lime]Response received.[/]");
                WriteLine("");
            });

            var status = context.Request.QueryString.Get("status");

            // Checks for errors.
            if (status != null && status == "revoked")
            {
                MarkupLine(
                    "[red]The request was revoked. Cancelling.[/]");
                WriteLine("");
                return false;
            }

            if (context.Request.QueryString.Get("auth") == null
                || context.Request.QueryString.Get("state") == null
                 || context.Request.QueryString.Get("endpoint") == null)
            {
                MarkupLine(
                   "[red]Malformed authorization response.[/]");
                WriteLine("");
                return false;
            }

            // extracts the code
            var auth = context.Request.QueryString.Get("auth");
            var incoming_state = context.Request.QueryString.Get("state");

            // Compares the receieved state to the expected value, to ensure that
            // this app made the request which resulted in authorization.
            if (incoming_state != state)
            {
                MarkupLine(
                   $"[red]Received request with invalid state ({incoming_state})[/]");
                WriteLine("");
                return false;
            }

            HttpClient httpClient = new();
            HttpResponseMessage endpointResponse = httpClient.GetAsync(string.Format("{0}&code={1}&scope[]={2}", context.Request.QueryString.Get("endpoint"), code_verifier, "shield")).Result;

            string responseBody =  endpointResponse.Content.ReadAsStringAsync().Result;

            if (!endpointResponse.IsSuccessStatusCode)
            {
                MarkupLine(
                   $"[red]This application could not be authorized: ({responseBody})[/]");
                WriteLine("");
                return false;
            }

            var appResponse = JsonConvert.DeserializeObject<ApplicationResponse>(responseBody);

            var token = appResponse.token;

            if (ClientManager.IsValidKey(token))
            {
                ClientManager.UpdateKey(token);
                var user = "";
                try
                {
                    var info = ClientManager.Client.GetSession();
                    user = info.Email;
                }
                catch { }
               
                MarkupLine(
                    $"[lime]Logged in correctly under the account {user}. Your session has been saved, to delete you credentials use [dim]clear[/][/]");
                WriteLine("");
                return true;
            }

            apiKey ??= AnsiConsole.Ask<string>("[blue]Insert your API Key[/]");

            if (ClientManager.IsValidKey(apiKey))
            {
                ClientManager.UpdateKey(apiKey);
                MarkupLine(
                    "[lime]Logged in correctly. Your session has been saved, to delete you credentials use [dim]clear[/][/]");
                WriteLine("");
                return true;
            }

            MarkupLine("[red]NOT logged in. Please review the API Key.[/]");
            MarkupLine(AnsiConsole.Profile.Capabilities.Links
                ? "[green] Read about CLI authentication at:[/] [link=https://docs.bytehide.com/platforms/dotnet/products/shield/cli-authentication]https://docs.bytehide.com/platforms/dotnet/products/shield/cli-authentication[/]"
                : "[green] Read about CLI authentication at:[/] https://docs.bytehide.com/platforms/dotnet/products/shield/cli-authentication");
            WriteLine("");
            return false;
        }

        /// <summary>
        ///     Checks if user is logged in.
        /// </summary>
        public bool AuthHasCredentials(bool throwException = true)
        {
            const string exMessage = "User is not logged into Bytehide.";

            if (ClientManager.HasValidClient()) return true;

            MarkupLine("[red]You are NOT logged in. \nYou must be logged in to use Shield CLI.[/]");
            WriteLine("");

            if (!AnsiConsole.Confirm("[blue]Do you want to logged in now? [/]"))
                return !throwException ? false : throw new AuthenticationException(exMessage);

            WriteLine("");
            var login = AuthLogin(null);
            return login ? true : (throwException ? throw new AuthenticationException(exMessage) : false);
        }

        /// <summary>
        ///     Log out and clear credentials or current user.
        /// </summary>
        public void AuthClearCredentials()
        {
            if (!AnsiConsole.Confirm("[red]This action will DELETE your credentials. Are you sure? [/]")) return;
            ClientManager.ClearClient();
            Console.WriteLine("");
            MarkupLine("[red]Credentials deleted. You must to login again to use ShieldCLI [/]");
        }



        public static string CreateFullPath(string dirPath, string name)
        {


            var separator = Path.DirectorySeparatorChar;

            var configPath = Path.EndsInDirectorySeparator(dirPath) ? dirPath : $"{dirPath}{separator}";
            var fullFilePath =
                Path.Combine(
                    Path.GetDirectoryName(configPath) ??
                    throw new InvalidOperationException("The provided directory path doesn't exists."), name);


            return fullFilePath;
        }

        public ProtectionConfigurationDTO GetFilesConfig(string path)
        {
            var configs = ClientManager.Client.Configuration.FindConfigurations(path);

            if (configs is null || configs.Count == 0)
            {
                MarkupLine($"[darkorange]There is no config files in this path[/]");
                return null;
            }

            var allConfigFiles = configs.Select(config => (string.IsNullOrWhiteSpace(config.Name) || string.IsNullOrEmpty(config.Name)) ? $"Empty name [{config.ConfigurationType.ToString().ToUpperFirst()}]" : $"{config.Name} [{config.ConfigurationType.ToString().ToUpperFirst()}]" ).ToList();

            string fullConfigName = allConfigFiles.First();

            var file = AnsiConsole.Prompt(
                       new MultiSelectionPrompt<string>()
                      .Title("Choose the configuration file:")
                      .PageSize(12)
                      .AddChoices(allConfigFiles));

            var fileIndex = allConfigFiles.IndexOf(file.First());

            return configs[fileIndex];
        }

        public ProtectionConfigurationDTO GetFileConfig(string path, string name)
        {
            var config = ClientManager.Client.Configuration.FindConfiguration(path, name);

            if (config is null)
            {
                MarkupLine($"[darkorange]There is no config files in this path with the name '{name}'[/]");
                return null;
            }

            return config;
        }

        public void PrintConfigFiles(string name, string preset, string type, Dictionary<string, ProtectionRules> protections)
        {
            MarkupLine("[lime]Config File has the follow info:[/]");
            WriteLine();

            var root = new Tree(name).Style("lime").Guide(TreeGuide.DoubleLine);

            var presetBranch = root.AddNode("[darkorange]Preset[/]");

            presetBranch.AddNode(preset.ToUpperFirst());

            var typeBranch = root.AddNode("[darkorange]Type[/]");

            typeBranch.AddNode(type.ToUpperFirst());

            if (preset == "custom")
            {
                var protectionsBranch = root.AddNode("[darkorange]Protections[/]");

                if (protections is not null && protections.Count > 0)
                {
                    foreach (var protection in protections)
                    {
                        var protectionBranch = protectionsBranch.AddNode(protection.Key);

                        if (protection.Value is not null && protection.Value.Count > 0)
                        {
                            var optionsBranch = protectionBranch.AddNode("[darkorange]Options[/]");

                            foreach (var option in protection.Value)
                            {
                                optionsBranch.AddNode($"{option.Key.ToUpperFirst()} [lime]→[/] {option.Value.ToString().ToLowerInvariant()}");
                            }
                        }
                    }
                }

            }

            AnsiConsole.Render(root);
        }

        /// <summary>
        ///     Gets the configuration file of an application, or creates if <param name="create">create</param> is true.
        /// </summary>
        /// 
        /// <param name="path">Directory path of config file</param>
        /// <param name="name">Name of the application</param>
        /// <param name="create">If <value>true</value> creates the configuration file is not exists</param>
        public ApplicationConfigurationDto GetApplicationConfiguration(string fullFilePath, bool create)
        {



            ApplicationConfigurationDto applicationConfig = null;

            if (File.Exists(fullFilePath))
                applicationConfig =
                    ClientManager.Client.Configuration.LoadApplicationConfigurationFromFileOrDefault(fullFilePath);

            //else if (create)
            //    applicationConfig = MakeApplicationConfiguration(path, "balance", name, null);


            return applicationConfig;

        }

        public ProtectionConfigurationDTO GetUniversalConfiguration(string fullFilePath)
        => ClientManager.Client.Configuration.LoadConfigurationFromFile(fullFilePath);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="name"></param>
        /// <param name="create"></param>
        /// <returns></returns>
        public ProjectConfigurationDto GetProjectConfiguration(string fullFilePath, bool create)
        {


            ProjectConfigurationDto projectConfig = null;

            if (File.Exists(fullFilePath))
            {
                projectConfig =
                    ClientManager.Client.Configuration.LoadProjectConfigurationFromFileOrDefault(fullFilePath);
            }
            //else if (create)
            //{


            //    projectConfig = MakeProjectConfiguration(path, "balance", name, null);
            //}

            return projectConfig;
            ///TODO: @Sr-l Read file and show info. 

        }

        /// <summary>
        /// Resolves an application required dependencies by his path.
        /// </summary>
        /// <param name="applicationPath">Application path</param>
        /// <returns></returns>
        internal async Task<List<(string, string)>> ResolveDependenciesAsync(string applicationPath)
        {
            var (isValid, requiredDependencies, (module, createdContext)) =
                await DependenciesResolver.GetAssemblyInfoAsync(applicationPath ?? string.Empty);

            if (!isValid)
                throw new Exception(
                    "Invalid .NET Assembly. The application is not a .NET module, remember that if it is .NET Core you must protect the compiled .dll (NOT .exe).");

            var requiredDep = requiredDependencies.ToList();

            List<string> dependencies = null;


            //AnsiConsole.Markup("[green]Resolving dependencies locally...[/]");

            AnsiConsole.Status()
                .Start("[green]Resolving dependencies locally...[/]", ctx =>
                {
                    ctx.Spinner(Spinner.Known.BoxBounce);
                    ctx.SpinnerStyle(Style.Parse("green"));

                    dependencies = DependenciesResolver.GetUnresolved(module,
                        createdContext, requiredDep).ToList();
                });

            var length = dependencies.Count;

            await AnsiConsole.Progress().Columns(
                    new ProgressColumn[]
                    {
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn()
                    }
                )
                .StartAsync(async context =>
                {
                    var resolverTask = context.AddTask("[green]Resolving dependencies with nuget...[/]");

                    resolverTask.MaxValue = length;

                    foreach (var (assembly, _) in requiredDep.Where(dep => dep.Item2 is null).ToList())
                    {
                        var info = Utils.SplitAssemblyInfo(assembly);

                        await DependenciesResolver.GetUnresolvedWithNuget(
                            module,
                            createdContext, requiredDep, info.name,
                            info.version);

                        resolverTask.Increment(1);
                    }

                    resolverTask.StopTask();
                });

            while (requiredDep.ToList().Any(dep => string.IsNullOrEmpty(dep.Item2)))
            {
                var unresolved = requiredDep.Where(dep => string.IsNullOrEmpty(dep.Item2)).ToList();

                MarkupLine(
                    $"The following dependencies [red]({unresolved.Count})[/] are required to process the application:");
                WriteLine();

                var table = new Table();

                table.AddColumn("Name").AddColumn("Version");

                table.Border(TableBorder.Rounded);

                var userPath = new List<string>();

                unresolved.ForEach(dep =>
                    table.AddRow(
                        $"[darkorange]{Utils.SplitAssemblyInfo(dep.Item1).name}[/]",
                        $"[darkorange]{Utils.SplitAssemblyInfo(dep.Item1).version}[/]"));

                AnsiConsole.Render(table);

                WriteLine("");

                unresolved.ForEach(dep =>
                    userPath.Add(AnsiConsole.Ask<string>(
                        $"[darkorange]Enter the path of the [red]{Utils.SplitAssemblyInfo(dep.Item1).name}[/] library:[/]")));

                _ = DependenciesResolver.GetUnresolved(module,
                    createdContext, requiredDep, userPath.ToArray());
            }

            MarkupLine("[lime]The dependencies have been resolved.[/]");

            DependenciesResolver.FixInvalidResolutions(requiredDep);

            return requiredDep;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="preset"></param>
        /// <returns></returns>
        public string ChooseProtectionPreset(string preset)
        {
            string[] presets = { "maximum", "balance", "custom", "optimized" };

            if (presets.All(pr => pr != preset))
                preset = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[white]Please choose the preset for the protection of protection[/]")
                        .PageSize(4)
                        .AddChoices(presets));

            return preset;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public ConfigurationType? ChooseConfigurationType(string type)
        {
            if (type != "application" && type != "project")
                type = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title(
                            "[white]Protection type must be application or propect.Please choose the type of protection[/]?")
                        .PageSize(3)
                        .AddChoice("project")
                        .AddChoice("application"));

            return type switch
            {
                "application" => (ConfigurationType?)ConfigurationType.Application,
                "project" => (ConfigurationType?)ConfigurationType.Project,
                _ => null,
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string ChooseConfigurationSource()
        {
            WriteLine();
            var value = AnsiConsole.Prompt(
                new SelectionPrompt<string>()

                    .Title("[darkorange]Choose the source of protection to use[/]")
                    .PageSize(3)
                    .AddChoice("Load from a config file")
                    .AddChoice("Use a preset")
                    .AddChoice("Make a custom")

            );

            return value;
        }

        public string[] ChooseCustomProtections(string projectKey)
        {
            var protections = ClientManager.Client.Protections.GetProtections(projectKey);

            var availableNames = protections.Where(p => p.Available).Select(p => p.Name).ToList();
            var notAvailableNames = protections.Where(p => !p.Available).Select(p => $"[[PRO]] {p.Name}").ToList();

            var choices = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Choose custom protections")
                    .PageSize(12)
                    .AddChoices(availableNames)
                    .AddChoices(notAvailableNames)
            );

            var selected = choices.ToArray();

            var available = availableNames.Where(p => selected.Contains(p));
            var notAvailable = notAvailableNames.Where(p => selected.Contains(p)).ToArray();


            if (notAvailable.Length > 0)
            {
                MarkupLine(
                    "[darkorange]Following protections selected will not be apply because they are not in your Shield Edition.[/]");
                MarkupLine("[darkorange]Please Upgrade your edition if you want to use the protections[/]");
                MarkupLine("");
                foreach (var invalid in notAvailable)
                    MarkupLine($"[red]{invalid}[/]");

            }

            var selectedIds = protections.Where(p => available.Contains(p.Name)).Select(p => p.Id).ToArray();

            return selectedIds;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="preset"></param>
        /// <param name="name"></param>
        /// <param name="protectionsId"></param>
        /// <returns></returns>
        [Obsolete("Legacy configuration file",true)]
        public ApplicationConfigurationDto MakeApplicationConfiguration(string path, string preset, string name,
            string[] protectionsId)

        {

            var applicationConfig = preset.Equals("custom")
                ? ClientManager.Client.Configuration.MakeApplicationCustomConfiguration(protectionsId)
                : ClientManager.Client.Configuration.MakeApplicationConfiguration(preset.ToPreset());

            if (File.Exists(Path.Combine(path, name)))
            {
                File.Delete(Path.Combine(path, name));
            }


            applicationConfig.SaveToFile(path, name);

            MarkupLine("[lime]Application configuration file created sucessfully.[/]");

            return applicationConfig;

        }

        public ProtectionConfigurationDTO MakeUniversalConfiguration(string path, string name,
            string preset = null, ConfigurationType type = ConfigurationType.Application, string[] protectionsId = null)

        {
            var isCustom = preset is not null && preset.Equals("custom");

            if(isCustom && protectionsId is null)
                protectionsId = Array.Empty<string>();

            var config = isCustom ? 
                ClientManager.Client.Configuration.FromProtections(protectionsId) : 
                ClientManager.Client.Configuration.Default(preset.ToPreset());

            config.ConfigurationType = type;

            if (name is not null)
                config.Rename(name);

            if (File.Exists(Path.Combine(path, name)))
                File.Delete(Path.Combine(path, name));

            config.SaveToFile(ref path, name);

            MarkupLine($"[lime]{type} configuration file created sucessfully in {Path.GetFullPath(path)}.[/]");

            return config;

        }

        public ProjectConfigurationDto MakeProjectConfiguration(string path, string preset, string name,
            string[] protectionsId)

        {


            var projectConfig = preset.Equals("custom")
                ? ClientManager.Client.Configuration.MakeProjectCustomConfiguration(protectionsId)
                : ClientManager.Client.Configuration.MakeProjectConfiguration(preset.ToPreset());

            if (File.Exists(Path.Combine(path, name)))
            {
                File.Delete(Path.Combine(path, name));
            }

            projectConfig.SaveToFile(path, name);
            MarkupLine("[lime]Project configuration file created sucessfully.[/]");

            return projectConfig;
        }

        public ProjectDto FindOrCreateProjectByName(string name)
        {
            var project = ClientManager.Client.Project.FindOrCreateExternalProject(name);

            MarkupLine("[lime]Project Found [/]");

            return project;
        }

        public ProjectDto FindOrCreateProjectById(string name, string key)
        {
            var project = ClientManager.Client.Project.FindByIdOrCreateExternalProject(name ?? "default", key);
            MarkupLine("[lime]Project Found [/]");

            return project;
        }

        public async Task<ProjectDto> FindOrCreateProjectByNameAsync(string name)
        {
            ProjectDto result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.BoxBounce2)
                .SpinnerStyle(Style.Parse("green bold"))
                .StartAsync("Loading project...",
                    async ctx =>
                    {
                        result = await ClientManager.Client.Project.FindOrCreateExternalProjectAsync(name);
                    });

            return result;
        }


        public async Task<ProjectDto> FindOrCreateProjectByIdAsync(string name, string key)
        {
            ProjectDto result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.BoxBounce2)
                .SpinnerStyle(Style.Parse("green bold"))
                .StartAsync("Looking for project...", async ctx =>
                {
                    result = await ClientManager.Client.Project.FindByIdOrCreateExternalProjectAsync(name ?? "default",
                        key);
                    AnsiConsole.Markup("[lime]Project found.[/]");
                });

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="keyProject"></param>
        /// <returns></returns>
        public async Task<DirectUploadDto> UploadApplicationAsync(string path, string keyProject)
        {

            var dependencies = await ResolveDependenciesAsync(path);

            DirectUploadDto result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.BoxBounce2)
                .SpinnerStyle(Style.Parse("green bold"))
                .StartAsync("Processing application...", async ctx =>
                {
                    result = await ClientManager.Client.Application.UploadApplicationDirectlyAsync(keyProject,
                        path, dependencies.Select(dep => dep.Item2).ToList());
                });

            Console.WriteLine("");
            MarkupLine("[lime]Application Uploaded Succesfully[/]");
            return result;

        }


        public void PrintProject(string name, string key)
        {
            Console.WriteLine("");
            var table = new Table();

            table.AddColumn("[darkorange]Project Name[/]");
            table.AddColumn("[darkorange]Project Key[/]");
            table.AddRow(name, key);
            AnsiConsole.Render(table);

        }

        public void PrintApplication(string name, string key, string projectKey)
        {
            Console.WriteLine("");
            var table = new Table();

            table.AddColumn("[darkorange]Application Name[/]");
            table.AddColumn("[darkorange]Application Key[/]");
            table.AddColumn("[darkorange]Project Key[/]");

            table.AddRow(name, key, projectKey);
            AnsiConsole.Render(table);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="projectKey"></param>
        /// <param name="path"></param>
        /// <param name="applicationName"></param>
        /// <returns></returns>
        public ProtectionConfigurationDTO CreateConfigurationFile(string projectKey, string path,
            string applicationName = null)
        {
            ProtectionConfigurationDTO configurationDto = null;

            var protection = ChooseConfigurationSource();

            var text = new TextPrompt<string>("[lime]Enter the config file name[/]");
            if (!string.IsNullOrEmpty(applicationName))
                text.DefaultValue(applicationName.MakeValidFileName());

            var configName =
                AnsiConsole.Prompt(text);

            WriteLine("");

            string[] protectionsId = { };

            switch (protection)
            {
                case "Load from a config file":
                    {
                        var configPath = AnsiConsole.Ask<string>("[lime]Provide the configuration file path:[/]");

                        configurationDto = ClientManager.Client.Configuration.LoadConfigurationFromFile(configPath);
                        
                        if(configurationDto is null)
                        {
                            MarkupLine("[darkorange]The configuration file is invalid or not exists, please create new one:[/]");
                            goto case "Use a preset";
                        }

                        break;
                    }
                case "Use a preset":
                    {
                        var preset = ChooseProtectionPreset("default");
                        if (preset == "custom")
                            goto case "Make a custom";
                        configurationDto = MakeUniversalConfiguration(path, configName, preset, ConfigurationType.Application, protectionsId);
                        break;
                    }
                case "Make a custom":
                    {
                        const string preset = "custom";
                        protectionsId = ChooseCustomProtections(projectKey);
                        configurationDto = MakeUniversalConfiguration(path, configName, preset, ConfigurationType.Application, protectionsId);
                        break;
                    }
            }

            return configurationDto;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="projectKey"></param>
        /// <param name="appName"></param>
        /// <param name="fileBlob"></param>
        /// <param name="config"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task ProtectApplicationAsync(string projectKey, string fileBlob,
            ProtectionConfigurationDTO config, string path)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots2).StartAsync("The application is being protected...", async ctx =>
                {
                    var connection = ClientManager.Client.Connector.CreateHubConnection();
                    var hub = await ClientManager.Client.Connector.InstanceHubConnectorWithLoggerAsync(connection);
                    await hub.StartAsync();

                    var result = await ClientManager.Client.Tasks.ProtectSingleFileAsync(projectKey, fileBlob, connection,
                        config
                       /* ClientManager.Client.Configuration.FromProtections("invalid_code", "constants_mutation", "constants_basic", "anti_ildasm")*/);

                    hub.OnLog(connection.OnLogger, (string date, string message, string level) =>
                    {
                        _ = Enum.TryParse<LogLevel>(level, out var logLevel);

                        const LogLevel minimumLevel = LogLevel.Information;

                        var color = logLevel switch
                        {
                            LogLevel.Trace => Color.Cyan3.ToString(),
                            LogLevel.Debug => Color.DarkViolet.ToString(),
                            LogLevel.Information => Color.DodgerBlue3.ToString(),
                            LogLevel.Warning => Color.DarkOrange.ToString(),
                            LogLevel.Error => Color.DarkRed.ToString(),
                            LogLevel.Critical => Color.Red.ToString(),
                            LogLevel.None => Color.Black.ToString(),
                            _ => throw new ArgumentOutOfRangeException()
                        };

                        if (logLevel < minimumLevel)
                            return;

                        MarkupLine("[" + color + "] > {0}[/]", message.EscapeMarkup());
                    });

                    result.OnSuccess(hub, async (application) =>
                        {
                            MarkupLine("");
                            MarkupLine(
                                $"[lime] > The application has been protected successfully with {application.Preset} protection.[/]");
                            MarkupLine("");
                            var downloaded =
                               ClientManager.Client.Application.DownloadApplicationAsArray(application);

                            downloaded.SaveOn(path, true);

                            MarkupLine(
                                $"[lime]Application saved successfully in [/][darkorange]{Path.GetFullPath(path)}[/]");
                        }
                    );

                    var semaphore = new Semaphore(0, 1);

                    result.OnError(hub, (error) =>
                    {
                        MarkupLine("");
                        MarkupLine("[red]An error occurred during the protection process:[/]");
                        MarkupLine("[darkorange] > {0}[/]", error.EscapeMarkup());
                        MarkupLine("[darkorange] > The process is still active but may not finish successfully.[/]");
                        MarkupLine("[blue] > The error has been reported and notified to our team, you will soon receive news about the solution.[/]");
                    });

                    result.OnClose(hub, _ =>
                    {
                        MarkupLine("");
                        MarkupLine("[lime]Protection has ended. [/]");
                        semaphore.Release();
                    });

                    semaphore.WaitOne();
                });
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}