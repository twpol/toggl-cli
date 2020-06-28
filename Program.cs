using CommandLine;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        [Verb("current", HelpText = "Shows details of the current timer.")]
        public class CurrentOptions : Options
        {
        }

        [Verb("set", HelpText = "Sets properties of the currently running timer.")]
        public class SetOptions : Options
        {
            [Option('p', "project", HelpText = "A new project for the timer, either name or ID.")]
            public string Project { get; set; }

            [Option('d', "description", HelpText = "A new description for the timer.")]
            public string Description { get; set; }

            [Option('t', "tags", HelpText = "Some new tags for the timer (use '~' for empty).")]
            public IReadOnlyList<string> Tags { get; set; }
        }

        [Verb("start", HelpText = "Start a new timer running.")]
        public class StartOptions : Options
        {
            [Option('p', "project", Required = true, HelpText = "Specify a project for the timer, either name or ID.")]
            public string Project { get; set; }

            [Option('d', "description", HelpText = "Provide a description for the timer.")]
            public string Description { get; set; }

            [Option('t', "tags", HelpText = "Provide some tags for the timer.")]
            public IReadOnlyList<string> Tags { get; set; }
        }

        [Verb("stop", HelpText = "Stop the current timer.")]
        public class StopOptions : Options
        {
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                Parser.Default.ParseArguments<ProjectsOptions, CurrentOptions, SetOptions, StartOptions, StopOptions>(args)
                    .WithParsed<ProjectsOptions>(options => Projects(options).Wait())
                    .WithParsed<CurrentOptions>(options => Current(options).Wait())
                    .WithParsed<SetOptions>(options => Set(options).Wait())
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

        static async Task<Project> GetMatchingProject(Query query, string projectNameOrId)
        {
            var projects = await query.GetProjects();
            var project = projects.FirstOrDefault(project =>
                project.id.Equals(projectNameOrId) ||
                project.name.Equals(projectNameOrId, StringComparison.CurrentCultureIgnoreCase)
            );
            if (project == null)
            {
                throw new ApplicationException($"No project matching '{projectNameOrId}' was found");
            }
            return project;
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

        static async Task Current(Options options)
        {
            var query = GetQuery(options);
            var timer = await query.GetCurrentTimer();
            if (timer == null)
            {
                Console.WriteLine("\u23F9\uFE0F No timer running");
            }
            else
            {
                var project = await query.GetProject(timer.pid);
                var duration = TimeSpan.FromSeconds(DateTimeOffset.Now.ToUnixTimeSeconds() + timer.duration);
                Console.WriteLine($"\u25B6\uFE0F {duration} - {project.name} - {timer.description} [{(timer.tags == null ? "" : string.Join(", ", timer.tags))}]");
            }
        }

        static async Task Set(SetOptions options)
        {
            var query = GetQuery(options);
            if (!string.IsNullOrEmpty(options.Project))
            {
                await query.SetCurrentTimerProject(await GetMatchingProject(query, options.Project));
            }
            if (!string.IsNullOrEmpty(options.Description))
            {
                await query.SetCurrentTimerDescription(options.Description);
            }
            if (options.Tags != null)
            {
                if (options.Tags.Count == 1 && options.Tags[0] == "~")
                    await query.SetCurrentTimerTags(new string[0]);
                else
                    await query.SetCurrentTimerTags(options.Tags);
            }
            await Current(options);
        }

        static async Task Start(StartOptions options)
        {
            var query = GetQuery(options);
            var project = await GetMatchingProject(query, options.Project);
            await query.StartTimer(project, options.Description, options.Tags);
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
