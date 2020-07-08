using Attribute = Terminal.Gui.Attribute;
using CommandLine;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui;
using Toggl_CLI.Toggl;

namespace Toggl_CLI
{
    class Program
    {
        public class Options
        {
            [Option('c', "config", Default = "config.json", HelpText = "Specify a configuration file to use.")]
            public string Config { get; set; } = "";
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
            [Option('i', "interactive", HelpText = "Show an interactive text-based interface for filling in the timer")]
            public bool Interactive { get; set; }

            [Option('p', "project", HelpText = "A project for the timer, either name or ID.")]
            public string Project { get; set; } = "";

            [Option('d', "description", HelpText = "A description for the timer.")]
            public string Description { get; set; } = "";

            [Option('t', "tags", HelpText = "Some tags for the timer (use '~' for empty).")]
            public IReadOnlyList<string> Tags { get; set; } = new string[0];
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

        const string FormatTimerDateTime = "yyyy-MM-dd HH:mm";
        const string FormatTimerTime = "hh\\:mm";

        static async Task<string> FormatTimer(Query query, TimeEntry timer)
        {
            var project = await query.GetProject(timer.pid);
            var duration = TimeSpan.FromSeconds(DateTimeOffset.Now.ToUnixTimeSeconds() + timer.duration);
            var timeRange = timer.stop.Year == 1 ?
                $"{timer.start.ToString(FormatTimerDateTime)}-now   ({duration.ToString(FormatTimerTime)})" :
                $"{timer.start.ToString(FormatTimerDateTime)}-{timer.stop.TimeOfDay.ToString(FormatTimerTime)} ({(timer.stop - timer.start).ToString(FormatTimerTime)})";
            return $"{timeRange} - {project?.name ?? "(none)"} - {timer.description} [{(timer.tags == null ? "" : string.Join(", ", timer.tags))}]";
        }

        static Color GetColor(ConsoleColor color)
        {
            return (Color)Enum.Parse(typeof(Color), color.ToString());
        }

        static void InitColorSchemes()
        {
            var dark = Console.BackgroundColor == ConsoleColor.Black ||
                Console.BackgroundColor == ConsoleColor.DarkGray;

            if (dark)
            {
                Colors.Base.Normal = Attribute.Make(Color.Gray, Color.Black);
                Colors.Base.Focus = Attribute.Make(Color.White, Color.DarkGray);
            }
            else
            {
                Colors.Base.Normal = Attribute.Make(Color.DarkGray, Color.White);
                Colors.Base.Focus = Attribute.Make(Color.Black, Color.Gray);
            }
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
            if (options.Interactive)
            {
                SetInteractive(options);
                return;
            }

            var query = GetQuery(options);
            if (options.Project != "")
            {
                await query.SetCurrentTimerProject(await GetMatchingProject(query, options.Project));
            }
            if (options.Description != "")
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

        static void SetInteractive(SetOptions options)
        {
            var query = GetQuery(options);

            Label timerStartTime, timerProject, timerDescription, timerStopTime, timerTags;
            ListView projectRecent, descriptionRecent, tagsRecent;
            Button applyButton;

            Application.Init();
            InitColorSchemes();
            var timerRow = new View() { Height = Dim.Sized(5 /* 5 instead of 4 due to https://github.com/migueldeicaza/gui.cs/issues/522 */) };
            {
                var timerBox = new FrameView("Timer");
                {
                    timerStartTime = new Label("(start time)") { Width = Dim.Sized(18) };
                    timerProject = new Label("(project)") { X = Pos.Right(timerStartTime), Width = Dim.Percent(25) };
                    timerDescription = new Label("(description)") { X = Pos.Right(timerProject), Width = Dim.Fill() };
                    timerStopTime = new Label("(stop time)") { Y = Pos.Bottom(timerStartTime), Width = Dim.Width(timerStartTime) };
                    timerTags = new Label("(tags)") { X = Pos.Right(timerStopTime), Y = Pos.Top(timerStopTime), Width = Dim.Fill() };
                    timerBox.Add(timerStartTime, timerProject, timerDescription, timerStopTime, timerTags);
                }
                timerRow.Add(timerBox);
            }
            var inputRow = new View() { Y = Pos.Bottom(timerRow), Height = Dim.Fill() - 1 };
            {
                var projectBox = new FrameView("Project") { Width = Dim.Percent(25) };
                {
                    projectRecent = new ListView(new string[0])
                    {
                        AllowsMarking = true,
                        AllowsMultipleSelection = false,
                    };
                    projectBox.Add(projectRecent);
                }
                var descriptionBox = new FrameView("Description") { X = Pos.Right(projectBox), Width = Dim.Percent(66) };
                {
                    descriptionRecent = new ListView(new string[0])
                    {
                        AllowsMarking = true,
                        AllowsMultipleSelection = false,
                    };
                    descriptionBox.Add(descriptionRecent);
                }
                var tagsBox = new FrameView("Tags") { X = Pos.Right(descriptionBox), Width = Dim.Fill() };
                {
                    tagsRecent = new ListView(new string[0])
                    {
                        AllowsMarking = true,
                        AllowsMultipleSelection = true,
                    };
                    tagsBox.Add(tagsRecent);
                }
                inputRow.Add(projectBox, descriptionBox, tagsBox);
            }
            var actionRow = new View() { Y = Pos.Bottom(inputRow), Height = Dim.Fill() };
            {
                applyButton = new Button("Apply")
                {
                    Y = Pos.AnchorEnd() - 1,
                };
                var exitButton = new Button("Exit")
                {
                    X = Pos.Right(applyButton),
                    Y = Pos.AnchorEnd() - 1,
                    Clicked = () => Application.Top.Running = false,
                };
                actionRow.Add(applyButton, exitButton);
            }
            Application.Top.Add(timerRow, inputRow, actionRow);

            Func<Task<(TimeEntry?, Project?)>> updateTimer = async () =>
            {
                var timer = await query.GetCurrentTimer();
                var project = await query.GetProject(timer?.pid ?? 0);

                if (timer != null)
                {
                    timerStartTime.Text = timer.start.ToString(FormatTimerDateTime);
                    timerStopTime.Text = timer.stop.Year == 1 ? "now" : timer.stop.ToString(FormatTimerDateTime);
                    timerProject.Text = project?.name ?? "(none)";
                    timerDescription.Text = timer.description;
                    timerTags.Text = timer.tags == null ? "" : string.Join(", ", timer.tags);
                    Application.Refresh();
                }

                return (timer, project);
            };

            _ = Task.Run(async () =>
            {
                var (timer, project) = await updateTimer();

                _ = Task.Run(async () =>
                {
                    var recent = await query.GetRecentTimers();
                    var projects = (await query.GetProjects()).ToList();
                    var descriptions = recent.Select(v => v.description).OrderBy(v => v).Distinct().ToList();
                    var tags = recent.Where(v => v.tags != null).SelectMany(v => v.tags).OrderBy(v => v).Distinct().ToList();

                    projectRecent.SetSource(projects);
                    if (project != null) projectRecent.Source.SetMark(projects.IndexOf(project), true);
                    descriptionRecent.SetSource(descriptions);
                    if (timer != null) descriptionRecent.Source.SetMark(descriptions.IndexOf(timer.description), true);
                    tagsRecent.SetSource(tags);
                    if (timer != null) foreach (var tag in timer.tags!) tagsRecent.Source.SetMark(tags.IndexOf(tag), true);
                    Application.Refresh();

                    applyButton.Clicked = async () =>
                    {
                        await query.SetCurrentTimerProject(projects.Where((project, index) => projectRecent.Source.IsMarked(index)).FirstOrDefault());
                        await query.SetCurrentTimerDescription(descriptions.Where((description, index) => descriptionRecent.Source.IsMarked(index)).FirstOrDefault());
                        await query.SetCurrentTimerTags(tags.Where((tag, index) => tagsRecent.Source.IsMarked(index)).ToList());
                        await updateTimer();
                        Application.Top.SetFocus(projectRecent);
                    };
                });
            });

            Application.Run();
        }

        static async Task Start(StartOptions options)
        {
            var query = GetQuery(options);
            await query.StartTimer();
            await Set(options);
        }

        static async Task Stop(StopOptions options)
        {
            var query = GetQuery(options);
            await query.StopTimer();
            await Current(options);
        }
    }
}
