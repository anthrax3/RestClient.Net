﻿using RestClientDotNet.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RestClientDotNet
{
    public sealed class RestClient : IRestClient
    {
        #region Public Properties
        public IHttpClientFactory HttpClientFactory { get; }
        public IZip Zip { get; }
        public IRestHeaders DefaultRequestHeaders => HttpClientFactory.DefaultRequestHeaders;
        public TimeSpan Timeout { get => HttpClientFactory.Timeout; set => HttpClientFactory.Timeout = value; }
        public ISerializationAdapter SerializationAdapter { get; }
        public ITracer Tracer { get; }
        public bool ThrowExceptionOnFailure { get; set; } = true;
        public string DefaultContentType { get; set; } = "application/json";
        public Uri BaseUri => HttpClientFactory.BaseUri;
        #endregion

        #region Constructors
        public RestClient(
            ISerializationAdapter serializationAdapter)
        : this(
            serializationAdapter,
            default(Uri))
        {
        }

        public RestClient(
            ISerializationAdapter serializationAdapter,
            Uri baseUri)
        : this(
            serializationAdapter,
            baseUri,
            null)
        {
        }

        public RestClient(
            ISerializationAdapter serializationAdapter,
            Uri baseUri,
            TimeSpan timeout)
        : this(
            serializationAdapter,
            new SingletonHttpClientFactory(timeout, baseUri))
        {
        }

        public RestClient(
            ISerializationAdapter serializationAdapter,
            Uri baseUri,
            ITracer tracer)
        : this(
          serializationAdapter,
          new SingletonHttpClientFactory(default, baseUri),
          tracer)
        {
        }

        public RestClient(
            ISerializationAdapter serializationAdapter,
            IHttpClientFactory httpClientFactory)
        : this(
          serializationAdapter,
          httpClientFactory,
          null)
        {
        }

        public RestClient(
       ISerializationAdapter serializationAdapter,
       IHttpClientFactory httpClientFactory,
       ITracer tracer)
        {
            SerializationAdapter = serializationAdapter;
            HttpClientFactory = httpClientFactory;
            Tracer = tracer;
        }

        #endregion

        #region Implementation
        async Task<RestResponseBase<TResponseBody>> IRestClient.SendAsync<TResponseBody, TRequestBody>(RestRequest<TRequestBody> restRequest)
        {
            var httpClient = HttpClientFactory.CreateHttpClient();

            var httpMethod = HttpMethod.Get;
            switch (restRequest.HttpVerb)
            {
                case HttpVerb.Get:
                    httpMethod = HttpMethod.Get;
                    break;
                case HttpVerb.Post:
                    httpMethod = HttpMethod.Post;
                    break;
                case HttpVerb.Put:
                    httpMethod = HttpMethod.Put;
                    break;
                case HttpVerb.Delete:
                    httpMethod = HttpMethod.Delete;
                    break;
                case HttpVerb.Patch:
                    httpMethod = new HttpMethod("PATCH");
                    break;
                default:
                    throw new NotImplementedException();
            }

            //TODO
#pragma warning disable CA2000 // Dispose objects before losing scope
            var httpRequestMessage = new HttpRequestMessage
            {
                Method = httpMethod,
                RequestUri = restRequest.Resource
            };
#pragma warning restore CA2000 // Dispose objects before losing scope

            byte[] requestBodyData = null;
            if (new List<HttpVerb> { HttpVerb.Put, HttpVerb.Post, HttpVerb.Patch }.Contains(restRequest.HttpVerb))
            {
                requestBodyData = await SerializationAdapter.SerializeAsync(restRequest.Body);
                var httpContent = new ByteArrayContent(requestBodyData);
                //Why do we have to set the content type only in cases where there is a request restRequest.Body, and headers?
                httpContent.Headers.Add("Content-Type", restRequest.ContentType);
                httpRequestMessage.Content = httpContent;
            }

            foreach (var headerName in restRequest.Headers.Names)
            {
                httpRequestMessage.Headers.Add(headerName, restRequest.Headers[headerName]);
            }

            Tracer?.Trace(restRequest.HttpVerb, httpClient.BaseAddress, restRequest.Resource, requestBodyData, TraceType.Request, null, restRequest.Headers);

            var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, restRequest.CancellationToken);

            byte[] responseData = null;

            if (Zip != null)
            {
                //This is for cases where an unzipping utility needs to be used to unzip the content. This is actually a bug in UWP
                var gzipHeader = httpResponseMessage.Content.Headers.ContentEncoding.FirstOrDefault(h =>
                    !string.IsNullOrEmpty(h) && h.Equals("gzip", StringComparison.OrdinalIgnoreCase));
                if (gzipHeader != null)
                {
                    var bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync();
                    responseData = Zip.Unzip(bytes);
                }
            }

            if (responseData == null)
            {
                responseData = await httpResponseMessage.Content.ReadAsByteArrayAsync();
            }

            var responseBody = await SerializationAdapter.DeserializeAsync<TResponseBody>(responseData);

            var restHeadersCollection = new RestResponseHeaders(httpResponseMessage.Headers);

            var restResponse = new RestResponse<TResponseBody>
            (
                restHeadersCollection,
                (int)httpResponseMessage.StatusCode,
                HttpClientFactory.BaseUri,
                restRequest.Resource,
                restRequest.HttpVerb,
                responseData,
                responseBody,
                httpResponseMessage
            );

            Tracer?.Trace(
                restRequest.HttpVerb,
                httpClient.BaseAddress,
                restRequest.Resource,
                responseData,
                TraceType.Response,
                (int)httpResponseMessage.StatusCode,
                restHeadersCollection);

            if (restResponse.IsSuccess || !ThrowExceptionOnFailure)
            {
                return restResponse;
            }

            throw new HttpStatusException($"{restResponse.StatusCode}.\r\nrestRequest.Resource: {restRequest.Resource}", restResponse, this);
        }
        #endregion
    }
}
