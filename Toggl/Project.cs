namespace Toggl_CLI.Toggl
{
    public class Project
    {
        public int id;
        public int wid;
        public string name = "";

        public override string ToString()
        {
            return name;
        }

        public override bool Equals(object? obj)
        {
            return object.ReferenceEquals(this, obj) || (this.id == (obj as Project)?.id);
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }
}
