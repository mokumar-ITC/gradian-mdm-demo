using System.Threading.RateLimiting;
using Gradian.Mdm.Infrastructure;
using Gradian.Mdm.Services.Interfaces;
using Gradian.Mdm.Services.Service;
using Gradian.Mdm.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
// Serilog reads its sinks and levels from the "Serilog" section; console output is what App Service log streaming shows.
builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// ---------------------------------------------------------------- data access and business services
// Database:Provider selects PostgreSql or FabricWarehouse; ConnectionStrings:Mdm is the connection string.
builder.Services.AddMdmDataAccess(builder.Configuration);

builder.Services.AddScoped<IUserRoleService, UserRoleService>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();
builder.Services.AddScoped<IColumnEditService, ColumnEditService>();
builder.Services.AddScoped<IProductMasterService, ProductMasterService>();
builder.Services.AddMemoryCache();   // caches each user's access level for a few minutes (see RoleRequirementAttribute)

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// ---------------------------------------------------------------- MVC, antiforgery, authentication
builder.Services.AddControllersWithViews(options =>
{
    // Every POST must carry a valid antiforgery token, even if an action forgets [ValidateAntiForgeryToken]
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // Every page requires a signed-in user unless it is marked [AllowAnonymous]
    options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
});

var cookieSecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".GradianMdm.Antiforgery";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Error/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = ".GradianMdm.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;   // Lax so the cookie survives the redirect after sign-in
    });
builder.Services.AddAuthorization();

// Brute-force protection: limited sign-in attempts per minute from one client address (default 10)
var loginAttemptsPerMinute = builder.Configuration.GetValue("Security:LoginAttemptsPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Demo:ShowDemoAccounts"))
    app.Logger.LogWarning("Demo:ShowDemoAccounts is on outside Development: the login page lists the demo accounts and their password.");

// ---------------------------------------------------------------- HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error/Handle");   // friendly page, no stack traces
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSerilogRequestLogging();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness / readiness probe for App Service and the deployment smoke test. Reports only the status, no details.
app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllerRoute(name: "default", pattern: "{controller=ProductMaster}/{action=Index}/{id?}");

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
