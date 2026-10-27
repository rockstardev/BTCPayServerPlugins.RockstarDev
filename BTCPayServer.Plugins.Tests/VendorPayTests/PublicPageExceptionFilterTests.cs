using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// BTCPay disables a plugin and restarts on any unhandled exception from plugin code. The public VendorPay pages are
// reachable without an admin, so an error there must end as a handled 500, never as an escaped exception.
public class PublicPageExceptionFilterTests
{
    private static ExceptionContext ContextFor(Exception exception, HttpContext httpContext = null)
    {
        var actionContext = new ActionContext(httpContext ?? new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
    }

    [Fact]
    public void AnEscapingExceptionBecomesAHandled500()
    {
        var context = ContextFor(new InvalidOperationException("boom"));
        new PublicPageExceptionFilter(NullLogger<PublicPageExceptionFilter>.Instance).OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
    }

    [Fact]
    public void AnAbortedRequestIsStillHandled()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var httpContext = new DefaultHttpContext { RequestAborted = aborted.Token };
        var context = ContextFor(new OperationCanceledException(), httpContext);
        new PublicPageExceptionFilter(NullLogger<PublicPageExceptionFilter>.Instance).OnException(context);

        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void ThePublicControllerCarriesTheFilter()
    {
        var filters = typeof(PublicController).GetCustomAttributes(typeof(TypeFilterAttribute), true)
            .Cast<TypeFilterAttribute>();
        Assert.Contains(filters, f => f.ImplementationType == typeof(PublicPageExceptionFilter));
    }
}
