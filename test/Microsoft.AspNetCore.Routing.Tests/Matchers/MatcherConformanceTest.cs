// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.Routing.Matchers
{
    public abstract class MatcherConformanceTest
    {
        internal abstract Matcher CreateMatcher(MatcherEndpoint endpoint);

        [Fact]
        public virtual async Task Match_SingleLiteralSegment()
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher("/simple");
            var (httpContext, feature) = CreateContext("/simple");

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        [Fact]
        public virtual async Task Match_SingleLiteralSegment_TrailingSlash()
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher("/simple");
            var (httpContext, feature) = CreateContext("/simple/");

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        [Theory]
        [InlineData("/simple")]
        [InlineData("/sImpLe")]
        [InlineData("/SIMPLE")]
        public virtual async Task Match_SingleLiteralSegment_CaseInsensitive(string path)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher("/Simple");
            var (httpContext, feature) = CreateContext(path);

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        // Some matchers will optimize for the ASCII case
        [Theory]
        [InlineData("/SÏmple", "/SÏmple")]
        [InlineData("/ab\uD834\uDD1Ecd", "/ab\uD834\uDD1Ecd")] // surrogate pair
        public virtual async Task Match_SingleLiteralSegment_Unicode(string template, string path)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher(template);
            var (httpContext, feature) = CreateContext(path);

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        // Matchers should operate on the decoded representation - a matcher that calls 
        // `httpContext.Request.Path.ToString()` will break this test.
        [Theory]
        [InlineData("/S%mple", "/S%mple")]
        [InlineData("/S\\imple", "/S\\imple")] // surrogate pair
        public virtual async Task Match_SingleLiteralSegment_PercentEncoded(string template, string path)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher(template);
            var (httpContext, feature) = CreateContext(path);

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }


        [Theory]
        [InlineData("/imple")]
        [InlineData("/siple")]
        [InlineData("/simple1")]
        public virtual async Task NotMatch_SingleLiteralSegment(string path)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher("/simple");
            var (httpContext, feature) = CreateContext(path);

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertNotMatch(feature);
        }

        [Theory]
        [InlineData("simple")]
        [InlineData("/simple")]
        [InlineData("~/simple")]
        public virtual async Task Match_Sanitizies_Template(string template)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher(template);
            var (httpContext, feature) = CreateContext("/simple");

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        // Matchers do their own 'splitting' of the path into segments, so including
        // some extra variation here
        [Theory]
        [InlineData("/a/b", "/a/b")]
        [InlineData("/a/b", "/a/b/")]
        [InlineData("/a/b/c", "/a/b/c")]
        [InlineData("/a/b/c", "/a/b/c/")]
        [InlineData("/a/b/c/d", "/a/b/c/d")]
        [InlineData("/a/b/c/d", "/a/b/c/d/")]
        public virtual async Task Match_MultipleLiteralSegments(string template, string path)
        {
            // Arrange
            var (matcher, endpoint) = CreateMatcher(template);
            var (httpContext, feature) = CreateContext(path);

            // Act
            await matcher.MatchAsync(httpContext, feature);

            // Assert
            DispatcherAssert.AssertMatch(feature, endpoint);
        }

        internal static (HttpContext httpContext, IEndpointFeature feature) CreateContext(string path)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "TEST";
            httpContext.Request.Path = path;
            httpContext.RequestServices = CreateServices();

            var feature = new EndpointFeature();
            httpContext.Features.Set<IEndpointFeature>(feature);

            return (httpContext, feature);
        }

        // The older routing implementations retrieve services when they first execute.
        internal static IServiceProvider CreateServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.BuildServiceProvider();
        }

        internal static MatcherEndpoint CreateEndpoint(string template)
        {
            return new MatcherEndpoint(
                MatcherEndpoint.EmptyInvoker,
                template,
                null,
                0,
                EndpointMetadataCollection.Empty, "endpoint: " + template);
        }

        internal (Matcher matcher, MatcherEndpoint endpoint) CreateMatcher(string template)
        {
            var endpoint = CreateEndpoint(template);
            return (CreateMatcher(endpoint), endpoint);
        }
    }
}
