﻿#if NETCOREAPP3_1

using RestClientDotNet.Abstractions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RestClientDotNet.UnitTests
{
    public class TestHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpStatusException(null,null, null);
        }
    }
}
#endif