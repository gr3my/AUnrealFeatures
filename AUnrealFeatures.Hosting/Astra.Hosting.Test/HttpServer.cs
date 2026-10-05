using AUnrealFeatures.Hosting.Http;
using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AUnrealFeatures.Hosting.Test
{
    public sealed class HttpServer : AstraHttpServer
    {
        public HttpServer() : base("localhost", 80)
        {
        }

        [HttpGet("/"), EndpointProcessor]
        public async Task<IHttpActionResult> Index(IHttpContext httpContext)
        {
            return Results.Ok("Test t=" + httpContext.Request.GetQueryParameter("t"));
        }
    }
}
