using System.Security.Claims;

namespace BuildWise.Api.Controllers;

internal static class AuthenticatedActor
{
    public static bool TryGetUserId(this ClaimsPrincipal user, out int userId)
    {
        userId = 0;
        return user.Identity?.IsAuthenticated == true
            && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId)
            && userId > 0;
    }
}
