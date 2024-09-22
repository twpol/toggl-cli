using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toggl_CLI.Toggl
{
    public class Query
    {
        public class TogglException : Exception
        {
            public TogglException(string message, Exception inner) : base(message, inner)
            {
            }
        }

        const string Endpoint = "https://api.track.toggl.com/api/v9/";
        const string UserAgent = "Toggl-CLI/1.0";

        readonly string Token;

        HttpClient Client = new HttpClient();
        Dictionary<uint, Project> ProjectCache = new Dictionary<uint, Project>();

        public Query(string token)
        {
            Token = token;
        }

        internal async Task<JToken> Send(HttpMethod method, string type, HttpContent content = null)
        {
            var uri = new Uri(Endpoint + type);

            while (true)
            {
                var request = new HttpRequestMessage(method, uri);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(UserAgent));
                request.Headers.Authorization = new AuthenticationHeaderValue("basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Token}:api_token")));
                request.Content = content;

                var response = await Client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync();
                try
                {
                    return JToken.Parse(text);
                }
                catch (JsonReaderException error)
                {
                    throw new TogglException(text, error);
                }
            }
        }

        internal async Task<JToken> Get(string type)
        {
            return await Send(HttpMethod.Get, type);
        }

        internal async Task<JToken> Post(string type, JObject content)
        {
            return await Send(HttpMethod.Post, type, new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json"));
        }

        internal async Task<JToken> Put(string type, JObject content)
        {
            return await Send(HttpMethod.Put, type, new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json"));
        }

        internal async Task<JToken> Patch(string type, JObject content)
        {
            return await Send(HttpMethod.Patch, type, new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, "application/json"));
        }

        internal async Task<U> GetCached<T, U>(Dictionary<T, U> cache, T key, Func<Task<U>> generator)
        {
            if (!cache.ContainsKey(key))
            {
                cache[key] = await generator();
            }
            return cache[key];
        }

        public async Task<IReadOnlyList<Workspace>> GetWorkspaces()
        {
            return (await Get("workspaces")).ToObject<List<Workspace>>();
        }

        public async Task<IReadOnlyList<Project>> GetProjects()
        {
            return (await Get($"me/projects")).ToObject<List<Project>>();
        }

        public async Task<IReadOnlyList<Project>> GetProjects(Workspace workspace)
        {
            return (await Get($"workspaces/{workspace.id}/projects")).ToObject<List<Project>>();
        }

        public async Task<Project> GetProject(uint workspaceId, uint projectId)
        {
            if (projectId == 0)
            {
                return null;
            }
            return await GetCached(ProjectCache, projectId, async () => (await Get($"workspaces/{workspaceId}/projects/{projectId}")).ToObject<Project>());
        }

        public async Task<IReadOnlyList<TimeEntry>> GetRecentTimers()
        {
            return (await Get("me/time_entries")).ToObject<IList<TimeEntry>>().Reverse().ToImmutableList();
        }

        public async Task<TimeEntry> GetCurrentTimer()
        {
            return (await Get("me/time_entries/current")).ToObject<TimeEntry>();
        }

        public async Task SetCurrentTimerProject(Project project)
        {
            var timer = await GetCurrentTimer();
            await Put($"workspaces/{timer.workspace_id}/time_entries/{timer.id}", new JObject(
                new JProperty("project_id", project.id)
            ));
        }

        public async Task SetCurrentTimerDescription(string description)
        {
            var timer = await GetCurrentTimer();
            await Put($"workspaces/{timer.workspace_id}/time_entries/{timer.id}", new JObject(
                new JProperty("description", description)
            ));
        }

        public async Task SetCurrentTimerTags(IReadOnlyList<string> tags)
        {
            var timer = await GetCurrentTimer();
            await Put($"workspaces/{timer.workspace_id}/time_entries/{timer.id}", new JObject(
                new JProperty("tags", JArray.FromObject(tags))
            ));
        }

        public async Task<TimeEntry> StartTimer(Project project, string description, IReadOnlyList<string> tags)
        {
            var workspaceId = project?.workspace_id ?? (await GetWorkspaces()).First().id;
            var startTime = DateTimeOffset.Now;
            var response = await Post($"workspaces/{workspaceId}/time_entries",
                new JObject(
                    new JProperty("workspace_id", workspaceId),
                    new JProperty("project_id", project?.id),
                    new JProperty("start", startTime),
                    new JProperty("duration", -1),
                    new JProperty("description", description),
                    new JProperty("tags", JArray.FromObject(tags)),
                    new JProperty("created_with", UserAgent)
                )
            );
            return response.ToObject<TimeEntry>();
        }

        public async Task<bool> StopTimer()
        {
            var currentTimer = await GetCurrentTimer();
            if (currentTimer != null)
            {
                await Patch($"workspaces/{currentTimer.workspace_id}/time_entries/{currentTimer.id}/stop", new JObject());
                return true;
            }
            return false;
        }
    }
}
