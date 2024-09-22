using System;
using System.Collections.Generic;

namespace Toggl_CLI.Toggl
{
    public class TimeEntry
    {
        public uint id;
        public uint workspace_id;
        public uint? project_id;
        public DateTimeOffset start;
        public DateTimeOffset? stop;
        public int duration;
        public string description;
        public IReadOnlyList<string> tags;
    }
}
