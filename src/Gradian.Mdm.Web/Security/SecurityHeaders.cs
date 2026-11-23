namespace Gradian.Mdm.Web.Security
{
    /// <summary>Adds browser security headers to every response.</summary>
    public static class SecurityHeaders
    {
        // All scripts, styles and fonts are served from wwwroot, so nothing needs a third-party origin.
        // 'unsafe-inline' remains for the inline <script> blocks and onclick handlers in the Razor views.
        private const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        /// <summary>Registers the middleware. Call it early so error pages get the headers too.</summary>
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                await next();
            });
    }
}
