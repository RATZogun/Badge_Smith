internal static class DotEnv
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
