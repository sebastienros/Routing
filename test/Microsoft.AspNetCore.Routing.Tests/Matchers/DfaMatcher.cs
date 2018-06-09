// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Routing.Matchers
{
    internal class DfaMatcher : Matcher
    {
        private readonly State[] _states;

        public DfaMatcher(State[] states)
        {
            _states = states;
        }

        public override Task MatchAsync(HttpContext httpContext, IEndpointFeature feature)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            if (feature == null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            var states = _states;
            var current = 0;

            var path = httpContext.Request.Path.Value;

            var start = 1; // PathString always has a leading slash
            var end = 0;
            while ((end = path.IndexOf('/', start)) >= 0)
            {
                current = states[current].Transitions.GetDestination(path, start, end - start);
                start = end + 1;
            }

            // residue
            var length = path.Length - start;
            if (length > 0)
            {
                current = states[current].Transitions.GetDestination(path, start, length);
            }

            var matches = states[current].Matches;
            feature.Endpoint = matches.Length == 0 ? null : matches[0];
            feature.Values = new RouteValueDictionary();

            return Task.CompletedTask;
        }
        
        public struct State
        {
            public bool IsAccepting;
            public Endpoint[] Matches;
            public JumpTable Transitions;
        }

        public abstract class JumpTable
        {
            public abstract int GetDestination(string text, int start, int length);
        }

        public class JumpTableBuilder
        {
            private readonly List<(string text, int destination)> _entries = new List<(string text, int destination)>();

            public int Depth { get; set; }

            public int Exit { get; set; }

            public void AddEntry(string text, int destination)
            {
                _entries.Add((text, destination));
            }

            public JumpTable Build()
            {
                return new SimpleJumpTable(Depth, Exit, _entries.ToArray());
            }
        }

        private class SimpleJumpTable : JumpTable
        {
            private readonly (string text, int destination)[] _entries;
            private readonly int _depth;
            private readonly int _exit;

            public SimpleJumpTable(int depth, int exit, (string text, int destination)[] entries)
            {
                _depth = depth;
                _exit = exit;
                _entries = entries;
            }

            public override int GetDestination(string text, int start, int length)
            {
                for (var i = 0; i < _entries.Length; i++)
                {
                    if (length == _entries[i].text.Length &&
                        string.Compare(
                        text,
                        start,
                        _entries[i].text,
                        0,
                        length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return _entries[i].destination;
                    }
                }

                return _exit;
            }
        }
    }
}
