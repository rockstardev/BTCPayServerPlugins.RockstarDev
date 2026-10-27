using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

/// <summary>
/// BTCPay disables a plugin and restarts the server on any unhandled exception thrown from the plugin's code
/// (PluginExceptionHandler). The public VendorPay pages are reachable by vendors and by anyone holding an accountless
/// upload link, so an error there must stay one failed request: it is logged and answered with BTCPay's generic error
/// page, and never reaches that handler. Known failures (a save on the upload pages) are handled closer to the form.
/// </summary>
public class PublicPageExceptionFilter(ILogger<PublicPageExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!context.HttpContext.RequestAborted.IsCancellationRequested)
            logger.LogError(context.Exception, "VendorPay: unhandled error on public page {Path}", context.HttpContext.Request.Path);
        context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
        context.ExceptionHandled = true;
    }
}
