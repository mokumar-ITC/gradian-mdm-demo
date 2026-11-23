using Gradian.Mdm.Services.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Gradian.Mdm.Web.Controllers
{
    /// <summary>Access-denied and unexpected-error pages. Never shows exception details to the browser.</summary>
    [AllowAnonymous]
    public class ErrorController : Controller
    {
        private readonly ILogger<ErrorController> _logger;

        public ErrorController(ILogger<ErrorController> logger) => _logger = logger;

        /// <summary>Shown when the user's level is below what the page requires.</summary>
        [Route("Error/AccessDenied")]
        public IActionResult AccessDenied(int? required)
        {
            var message = required is int level && AccessLevel.Names.ContainsKey(level)
                ? $"This page requires {AccessLevel.Name(level)} access (level {level}) or higher."
                : "You do not have permission to open this page.";
            return View((object)message);
        }

        /// <summary>Target of UseExceptionHandler outside Development. Logs the error with the request id the user sees.</summary>
        [Route("Error/Handle")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Handle()
        {
            var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (feature != null)
                _logger.LogError(feature.Error, "Unhandled error on {Path} (request {RequestId})", feature.Path, HttpContext.TraceIdentifier);

            ViewBag.RequestId = HttpContext.TraceIdentifier;
            return View("Error");
        }
    }
}
