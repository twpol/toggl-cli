using System;
using System.Collections.Generic;

namespace Toggl_CLI.Toggl
{
    public class TimeEntry
    {
        public int id;
        public int wid;
        public int pid;
        public DateTimeOffset start;
        public DateTimeOffset stop;
        public int duration;
        public string description;
        public IReadOnlyList<string> tags;
    }
}
