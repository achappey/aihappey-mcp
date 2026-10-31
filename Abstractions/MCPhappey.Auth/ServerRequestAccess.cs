using MCPhappey.Common.Models;

namespace MCPhappey.Auth;

public sealed record ServerRequestAccess(ServerConfig Config, ServerAccessMode Mode, IReadOnlyDictionary<string, string> Headers);
