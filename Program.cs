using CommandLine;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Toggl_CLI.Toggl;

namespace Toggl_CLI
{
    class Program
    {
        public class Options
        {
            [Option('c', "config", Default = "config.json", HelpText = "Specify a configuration file to use.")]
            public string Config { get; set; }
        }

        [Verb("projects", HelpText = "List all available projects.")]
        public class ProjectsOptions : Options
        {
        }

        [Verb("start", HelpText = "Start a new timer running.")]
        public class StartOptions : Options
        {
            [Option('p', "project", Required = true, HelpText = "Specify a project for the timer, either name or ID.")]
            public string Project { get; set; }

            [Option('d', "description", HelpText = "Provide a description for the timer.")]
            public string Description { get; set; }
        }

        [Verb("stop", HelpText = "Stop the current timer.")]
        public class StopOptions : Options
        {
        }

        static void Main(string[] args)
        {
            try
            {
                Parser.Default.ParseArguments<ProjectsOptions, StartOptions, StopOptions>(args)
                    .WithParsed<ProjectsOptions>(options => Projects(options).Wait())
                    .WithParsed<StartOptions>(options => Start(options).Wait())
                    .WithParsed<StopOptions>(options => Stop(options).Wait());
            }
            catch (AggregateException error) when (error.InnerExceptions.Count == 1 && error.InnerException is ApplicationException)
            {
                Console.Error.WriteLine(error.InnerException.Message);
            }
        }

        static Query GetQuery(Options options)
        {
            var configFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, options.Config));
            var config = new ConfigurationBuilder()
                .AddJsonFile(configFile.FullName)
                .Build();
            return new Query(config.GetSection("Toggl")["ApiToken"]);
        }

        static async Task Projects(ProjectsOptions options)
        {
            var query = GetQuery(options);
            foreach (var workspace in await query.GetWorkspaces())
            {
                Console.WriteLine($"* {workspace.name} ({workspace.id})");
                foreach (var project in await query.GetProjects(workspace))
                {
                    Console.WriteLine($"  * {project.name} ({project.id})");
                }
            }
        }
        static async Task Start(StartOptions options)
        {
            var query = GetQuery(options);
            var projects = await query.GetProjects();
            var project = projects.FirstOrDefault(project => project.id.Equals(options.Project) || project.name.Equals(options.Project, StringComparison.CurrentCultureIgnoreCase));
            if (project == null)
            {
                throw new ApplicationException($"No project matching '{options.Project}' was found");
            }
            await query.StartTimer(project, options.Description);
            Console.WriteLine($"Started timer in {project.name} for '{options.Description}'");
        }

        static async Task Stop(StopOptions options)
        {
            var query = GetQuery(options);
            if (await query.StopTimer())
                Console.WriteLine("Stopped timer");
            else
                Console.WriteLine("No timer running");
        }
    }
}
