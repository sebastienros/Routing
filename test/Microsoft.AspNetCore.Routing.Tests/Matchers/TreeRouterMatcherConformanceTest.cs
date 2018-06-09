﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Routing.Matchers
{
    public class TreeRouterMatcherConformanceTest : MatcherConformanceTest
    {
        internal override Matcher CreateMatcher(MatcherEndpoint endpoint)
        {
            var builder = new TreeRouterMatcherBuilder();
            builder.AddEndpoint(endpoint);
            return builder.Build();
        }
    }
}
