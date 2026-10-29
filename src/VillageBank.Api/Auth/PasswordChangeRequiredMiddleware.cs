using Microsoft.AspNetCore.Mvc;

namespace VillageBank.Api.Auth;

public class PasswordChangeRequiredMiddleware
{
    public const string ItemKey = "password-change-required";

    private static readonly PathString ChangePasswordPath = new("/api/auth/change-password");

    private readonly RequestDelegate _next;

    public PasswordChangeRequiredMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items.ContainsKey(ItemKey) && !context.Request.Path.StartsWithSegments(ChangePasswordPath))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Password change required",
                    Detail = "You must change your temporary password before using the system."
                },
                options: null,
                contentType: "application/problem+json",
                context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
