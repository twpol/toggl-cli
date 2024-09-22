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

        [Verb("recent", HelpText = "Shows details of recent timers.")]
        public class RecentOptions : Options
        {
        }

        [Verb("current", HelpText = "Shows details of the current timer.")]
        public class CurrentOptions : Options
        {
        }

        [Verb("set", HelpText = "Sets properties of the currently running timer.")]
        public class SetOptions : Options
        {
            [Option('p', "project", HelpText = "A project for the timer, either name or ID.")]
            public string Project { get; set; }

            [Option('d', "description", HelpText = "A description for the timer.")]
            public string Description { get; set; }

            [Option('t', "tags", HelpText = "Some tags for the timer (use '~' for empty).")]
            public IReadOnlyList<string> Tags { get; set; }
        }

        [Verb("start", HelpText = "Start a new timer running.")]
        public class StartOptions : SetOptions
        {
        }

        [Verb("stop", HelpText = "Stop the current timer.")]
        public class StopOptions : Options
        {
        }

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                await Parser.Default.ParseArguments<ProjectsOptions, RecentOptions, CurrentOptions, SetOptions, StartOptions, StopOptions>(args)
                    .WithParsedAsync(Run);
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
            if (projectNameOrId == null)
            {
                return null;
            }
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

        static async Task<string> FormatTimer(Query query, TimeEntry timer)
        {
            var project = timer.project_id.HasValue ? await query.GetProject(timer.workspace_id, timer.project_id.Value) : null;
            var duration = DateTimeOffset.Now - timer.start;
            var timeRange = timer.stop == null ?
                $"{timer.start.ToString("yyyy-MM-dd HH:mm")}-now   ({duration.ToString("hh\\:mm")})" :
                $"{timer.start.ToString("yyyy-MM-dd HH:mm")}-{timer.stop.Value.TimeOfDay.ToString("hh\\:mm")} ({(timer.stop.Value - timer.start).ToString("hh\\:mm")})";
            return $"{timeRange} - {project?.name ?? "(none)"} - {timer.description} [{(timer.tags == null ? "" : string.Join(", ", timer.tags))}]";
        }

        static async Task Run(object options)
        {
            switch (options)
            {
                case ProjectsOptions po:
                    await Projects(po);
                    break;
                case RecentOptions ro:
                    await Recent(ro);
                    break;
                case CurrentOptions co:
                    await Current(co);
                    break;
                case StartOptions so:
                    await Start(so);
                    break;
                case SetOptions so:
                    await Set(so);
                    break;
                case StopOptions so:
                    await Stop(so);
                    break;
            }
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

        static async Task Recent(Options options)
        {
            var query = GetQuery(options);
            foreach (var timer in await query.GetRecentTimers())
            {
                Console.WriteLine(await FormatTimer(query, timer));
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
                Console.WriteLine($"\u25B6\uFE0F {await FormatTimer(query, timer)}");
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
            if (options.Tags.Count > 0)
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
            var timer = await query.StartTimer(await GetMatchingProject(query, options.Project), options.Description, options.Tags);
            Console.WriteLine($"\u25B6\uFE0F {await FormatTimer(query, timer)}");
        }

        static async Task Stop(StopOptions options)
        {
            var query = GetQuery(options);
            await query.StopTimer();
            await Current(options);
        }
    }
}
