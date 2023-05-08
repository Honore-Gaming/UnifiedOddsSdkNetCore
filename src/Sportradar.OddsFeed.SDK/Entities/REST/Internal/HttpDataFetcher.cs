﻿/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using Sportradar.OddsFeed.SDK.Common.Exceptions;
using Sportradar.OddsFeed.SDK.Common.Internal;
using Sportradar.OddsFeed.SDK.Messages.REST;

namespace Sportradar.OddsFeed.SDK.Entities.REST.Internal
{
    /// <summary>
    /// A <see cref="IDataFetcher" /> which uses the HTTP requests to fetch the requested data
    /// </summary>
    /// <seealso cref="MarshalByRefObject" />
    /// <seealso cref="IDataFetcher" />
    /// <seealso cref="IDataPoster" />
    /// <seealso cref="IDataFetcher" />
    internal class HttpDataFetcher : MarshalByRefObject, IDataFetcher, IDataPoster
    {
        /// <summary>
        /// A sdk wrapper around <see cref="HttpClient"/> used to invoke HTTP requests
        /// </summary>
        private readonly ISdkHttpClient _client;

        private readonly int _connectionFailureLimit;

        private readonly int _connectionFailureTimeBetweenNewRequestsInSec;

        private int _connectionFailureCount;

        private long _timeOfLastFailure;

        private bool _blockingModeActive;

        private readonly bool _saveResponseHeaders;

        private readonly IDeserializer<response> _responseDeserializer;

        /// <summary>
        /// Gets the response headers
        /// </summary>
        /// <value>The response headers</value>
        public Dictionary<string, IEnumerable<string>> ResponseHeaders { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpDataFetcher"/> class
        /// </summary>
        /// <param name="client">A <see cref="ISdkHttpClient"/> used to invoke HTTP requests</param>
        /// <param name="responseDeserializer">The deserializer for unexpected response</param>
        /// <param name="connectionFailureLimit">Indicates the limit of consecutive request failures, after which it goes in "blocking mode"</param>
        /// <param name="connectionFailureTimeout">indicates the timeout after which comes out of "blocking mode" (in seconds)</param>
        /// <param name="saveResponseHeader">Indicates if the response header should be obtained</param>
        public HttpDataFetcher(ISdkHttpClient client,
                               IDeserializer<response> responseDeserializer,
                               int connectionFailureLimit = 5,
                               int connectionFailureTimeout = 15,
                               bool saveResponseHeader = true)
        {
            Guard.Argument(client, nameof(client)).NotNull();
            Guard.Argument(client.DefaultRequestHeaders, nameof(client.DefaultRequestHeaders)).NotNull();
            Guard.Argument(connectionFailureLimit, nameof(connectionFailureLimit)).Positive();
            Guard.Argument(connectionFailureTimeout, nameof(connectionFailureTimeout)).Positive();
            Guard.Argument(responseDeserializer, nameof(responseDeserializer)).NotNull();

            _client = client;
            _connectionFailureLimit = connectionFailureLimit;
            _connectionFailureTimeBetweenNewRequestsInSec = connectionFailureTimeout;
            _blockingModeActive = false;
            _responseDeserializer = responseDeserializer;
            _saveResponseHeaders = saveResponseHeader;
        }

        /// <summary>
        /// Asynchronously gets a <see cref="Stream" /> containing data fetched from the provided <see cref="Uri" />
        /// </summary>
        /// <param name="uri">The <see cref="Uri" /> of the resource to be fetched</param>
        /// <returns>A <see cref="Task" /> which, when completed will return a <see cref="Stream" /> containing fetched data</returns>
        /// <exception cref="CommunicationException">Failed to execute http get</exception>
        public virtual async Task<Stream> GetDataAsync(Uri uri)
        {
            ValidateConnection(uri);
            var responseMessage = new HttpResponseMessage();
            try
            {
                responseMessage = await _client.GetAsync(uri).ConfigureAwait(false);
                return await ProcessGetDataAsync(responseMessage, uri).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException)
                {
                    RecordFailure();
                    throw new CommunicationException("Failed to execute http get", uri.ToString(), responseMessage.StatusCode, ex);
                }
                throw;
            }
        }

        /// <summary>
        /// Gets a <see cref="Stream" /> containing data fetched from the provided <see cref="Uri" />
        /// </summary>
        /// <param name="uri">The <see cref="Uri" /> of the resource to be fetched</param>
        /// <returns>A <see cref="Task" /> which, when completed will return a <see cref="Stream" /> containing fetched data</returns>
        /// <exception cref="CommunicationException">Failed to execute http get</exception>
        public virtual Stream GetData(Uri uri)
        {
            ValidateConnection(uri);
            var responseMessage = new HttpResponseMessage();
            try
            {
                responseMessage = _client.GetAsync(uri).GetAwaiter().GetResult();
                return ProcessGetDataAsync(responseMessage, uri).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException)
                {
                    RecordFailure();
                    throw new CommunicationException("Failed to execute http get", uri.ToString(), responseMessage.StatusCode, ex);
                }
                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception.", Justification = "Ignore all deserialization issues")]
        private async Task<Stream> ProcessGetDataAsync(HttpResponseMessage responseMessage, Uri uri)
        {
            RecordSuccess();
            if (!responseMessage.IsSuccessStatusCode)
            {
                var responseContent = string.Empty;
                try
                {
                    responseContent = new StreamReader(responseMessage.Content.ReadAsStreamAsync().GetAwaiter().GetResult()).ReadToEndAsync().GetAwaiter().GetResult();
                    var memoryStream = new MemoryStream();
                    var writer = new StreamWriter(memoryStream);
                    await writer.WriteAsync(responseContent);
                    await writer.FlushAsync();
                    memoryStream.Position = 0;
                    var response = _responseDeserializer.Deserialize(memoryStream);
                    responseContent = response.action;
                }
                catch (Exception)
                {
                    // ignored
                }
                throw new CommunicationException($"Response StatusCode={responseMessage.StatusCode} does not indicate success. Msg={responseContent}", uri.ToString(), responseMessage.StatusCode, responseContent, null);
            }
            if (_saveResponseHeaders)
            {
                var responseHeaders = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in responseMessage.Headers)
                {
                    if (responseHeaders.ContainsKey(header.Key))
                    {
                        continue;
                    }
                    responseHeaders.Add(header.Key, header.Value);
                }
                ResponseHeaders = responseHeaders;
            }

            if (responseMessage.Content == null)
            {
                throw new CommunicationException("Missing content in the response", uri.ToString(), responseMessage.StatusCode, null);
            }
            return await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously gets a <see cref="Stream" /> containing data fetched from the provided <see cref="Uri" />
        /// </summary>
        /// <param name="uri">The <see cref="Uri" /> of the resource to be fetched</param>
        /// <param name="content">A <see cref="HttpContent" /> to be posted to the specific <see cref="Uri" /></param>
        /// <returns>A <see cref="Task" /> which, when successfully completed will return a <see cref="HttpResponseMessage" /></returns>
        /// <exception cref="CommunicationException">Failed to execute http post</exception>
        public virtual async Task<HttpResponseMessage> PostDataAsync(Uri uri, HttpContent content = null)
        {
            ValidateConnection(uri);
            var responseMessage = new HttpResponseMessage();
            try
            {
                responseMessage = await _client.PostAsync(uri, content ?? new StringContent(string.Empty)).ConfigureAwait(false);
                RecordSuccess();

                if (_saveResponseHeaders)
                {
                    var responseHeaders = new Dictionary<string, IEnumerable<string>>();
                    foreach (var header in responseMessage.Headers)
                    {
                        if (responseHeaders.ContainsKey(header.Key))
                        {
                            continue;
                        }
                        responseHeaders.Add(header.Key, header.Value);
                    }
                    ResponseHeaders = responseHeaders;
                }
                return responseMessage;
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException)
                {
                    RecordFailure();
                    throw new CommunicationException("Failed to execute http post", uri.ToString(), responseMessage.StatusCode, ex);
                }
                throw;
            }
        }

        /// <summary>
        /// Records that the request made was successful
        /// </summary>
        protected void RecordSuccess()
        {
            Interlocked.Exchange(ref _connectionFailureCount, 0);
            Interlocked.Exchange(ref _timeOfLastFailure, DateTime.MinValue.Ticks);
            _blockingModeActive = false;
        }

        /// <summary>
        /// Records that the request ended with HttpRequestException or was taking too long and was canceled
        /// </summary>
        protected void RecordFailure()
        {
            Interlocked.Increment(ref _connectionFailureCount);
            Interlocked.Exchange(ref _timeOfLastFailure, DateTime.Now.Ticks);
        }

        /// <summary>
        /// Validates if the request should be made or too many errors happens and should be omitted
        /// </summary>
        /// <param name="uri">The URI of the request to be made</param>
        /// <exception cref="CommunicationException">Failed to execute request due to previous failures.</exception>
        protected void ValidateConnection(Uri uri)
        {
            var timeOfLastFailure = new DateTime(_timeOfLastFailure);
            var resetTime = DateTime.Now.AddSeconds(-_connectionFailureTimeBetweenNewRequestsInSec);
            if (timeOfLastFailure < resetTime && _blockingModeActive)
            {
                RecordSuccess();
                return;
            }

            if (_connectionFailureCount >= _connectionFailureLimit && !_blockingModeActive)
            {
                _blockingModeActive = true;
            }

            if (_blockingModeActive)
            {
                throw new CommunicationException("Failed to execute request due to previous failures.", uri.ToString(), null);
            }
        }
    }
}
