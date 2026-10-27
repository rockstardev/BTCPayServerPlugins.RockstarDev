using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

/// <summary>
/// BTCPay disables a plugin and restarts the server on any unhandled exception thrown from the plugin's code
/// (PluginExceptionHandler). The public VendorPay pages are reachable by vendors and by anyone holding an accountless
/// upload link, so an error there must stay one failed request and never reach that handler. Two stages:
/// <list type="bullet">
/// <item>model binding and the action (exception filter): logged, answered with BTCPay's generic error page;</item>
/// <item>rendering the result, e.g. a view (result filter, which an exception filter does not see): logged and marked
/// handled, with a 500 if nothing was sent yet.</item>
/// </list>
/// Known failures (a save on the upload pages) are handled closer to the form, with a better message.
/// </summary>
public class PublicPageExceptionFilter(ILogger<PublicPageExceptionFilter> logger) : IExceptionFilter, IResultFilter
{
    public void OnException(ExceptionContext context)
    {
        Log(context.HttpContext, context.Exception);
        context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
        context.ExceptionHandled = true;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        if (context.Exception == null || context.ExceptionHandled)
            return;
        Log(context.HttpContext, context.Exception);
        if (!context.HttpContext.Response.HasStarted)
            context.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.ExceptionHandled = true;
    }

    private void Log(HttpContext httpContext, System.Exception exception)
    {
        if (!httpContext.RequestAborted.IsCancellationRequested)
            logger.LogError(exception, "VendorPay: unhandled error on public page {Path}", httpContext.Request.Path);
    }
}
