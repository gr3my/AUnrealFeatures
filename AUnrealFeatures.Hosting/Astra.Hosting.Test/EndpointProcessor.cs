using AUnrealFeatures.Hosting.Http.Attributes;
using AUnrealFeatures.Hosting.Http.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AUnrealFeatures.Hosting.Test
{
    public sealed class EndpointProcessor : HttpProcessorAttribute
    {
        public override async Task<bool> Validate(IHttpContext httpContext)
        {
            if (httpContext == null) return false;
            return httpContext.Request.Queries.ContainsKey("t");
        }
    }
}
