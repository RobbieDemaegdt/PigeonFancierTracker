using System.Text.RegularExpressions;

namespace PigeonFancierTracker.Infrastructure.PigeonFancierApi;

public static class PigeonFancierApiAllowlist
{
    private static readonly Regex[] AllowedGetPaths =
    [
        PathPattern(@"^/api/translation/[^/]+$"),
        PathPattern(@"^/api/season$"),
        PathPattern(@"^/api/user$"),
        PathPattern(@"^/api/fancier(?:/\d+)?$"),
        PathPattern(@"^/api/fancier/(selected|tutorial|online|items|reports)$"),
        PathPattern(@"^/api/weather$"),
        PathPattern(@"^/api/flight(?:/\d+)?(?:/(events|positions|results|subscriptions))?$"),
        PathPattern(@"^/api/flight/(live|tick|locations)$"),
        PathPattern(@"^/api/flight/subscription$"),
        PathPattern(@"^/api/pigeon(?:/\d+)?(?:/(energy|offspring|pedigree|certificate|treatments|results))?$"),
        PathPattern(@"^/api/fancier/\d+/pigeons$"),
        PathPattern(@"^/api/couple(?:/\d+)?(?:/offspring)?$"),
        PathPattern(@"^/api/group$"),
        PathPattern(@"^/api/transfer$"),
        PathPattern(@"^/api/ranking(?:/achievements)?$"),
    ];

    public static bool IsAllowedGetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            return false;
        }

        var pathOnly = path.Split('?', 2)[0];
        return !pathOnly.Contains("..", StringComparison.Ordinal)
            && AllowedGetPaths.Any(pattern => pattern.IsMatch(pathOnly));
    }

    private static Regex PathPattern(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}