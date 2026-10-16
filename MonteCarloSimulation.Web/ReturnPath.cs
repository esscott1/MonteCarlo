namespace MonteCarloSimulation.Web
{
    // A page on this site that the access page may send the visitor back to (return=): a local path only. An absolute
    // URL, "//host" or "/\host" would make the link an open redirect to another site, and the access flow itself
    // (/access.html, /billing/...) would send the visitor in a circle. access.js checks the same on its side.
    public static class ReturnPath
    {
        public const int MaxLength = 2048;

        public static bool IsLocal(string? path) =>
            !string.IsNullOrEmpty(path)
            && path.Length <= MaxLength
            && path[0] == '/'
            && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'))
            && !path.Any(char.IsControl)
            && !path.StartsWith("/access.html", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/billing/", StringComparison.OrdinalIgnoreCase);
    }
}
